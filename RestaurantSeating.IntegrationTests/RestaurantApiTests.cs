using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RestaurantSeating.Api.Data;
using RestaurantSeating.Api.Models.Entities;
using RestaurantSeating.Api.Models.Responses;

namespace RestaurantSeating.IntegrationTests;

// Shared SQL Server; isolated database and host per test.
public sealed class RestaurantApiTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string databaseName = "RestaurantTests_" + Guid.NewGuid().ToString("N");
    private string connectionString = "";
    private ApiFactory factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        await ExecuteSqlAsync(sqlServer.ConnectionString, $"CREATE DATABASE [{databaseName}];");
        connectionString = new SqlConnectionStringBuilder(sqlServer.ConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;
        factory = new ApiFactory(connectionString);
        client = factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null)
            await factory.DisposeAsync();
        SqlConnection.ClearAllPools();
        await ExecuteSqlAsync(sqlServer.ConnectionString,
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
    }

    [Fact]
    public async Task Database_starts_empty()
    {
        var state = await StateAsync();
        Assert.Empty(state.Tables);
        Assert.Empty(state.Groups);
        Assert.Null(state.NextHistoryBeforeId);
        Assert.Equal(0, await CountTriggersAsync());
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Theory]
    [InlineData("{\"capacity\":1}")]
    [InlineData("{\"capacity\":7}")]
    [InlineData("{}")]
    public async Task Invalid_tables_return_400_without_changing_state(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        await AssertProblemAsync(await client.PostAsync("/api/tables", content), 400, "Capacity");
        Assert.Empty((await StateAsync()).Tables);
    }

    [Fact]
    public async Task Adding_table_seats_waiting_groups_and_skips_groups_that_do_not_fit()
    {
        var large = await ArriveAsync(6);
        var first = await ArriveAsync(2);
        var second = await ArriveAsync(2);
        Assert.All((await StateAsync()).Groups, group => Assert.Equal(GroupStatus.Waiting, group.Status));
        var table = await AddTableAsync(4);
        Assert.Equal(4, table.OccupiedSeats);
        Assert.Equal(0, table.AvailableSeats);
        Assert.Equal(GroupStatus.Waiting, (await GetGroupAsync(large.Id)).Status);
        Assert.Equal(table.Id, (await GetGroupAsync(first.Id)).TableId);
        Assert.Equal(table.Id, (await GetGroupAsync(second.Id)).TableId);

        var laterTable = await AddTableAsync(6);
        Assert.Equal(laterTable.Id, (await GetGroupAsync(large.Id)).TableId);
        Assert.Equal(table.Id, (await GetGroupAsync(first.Id)).TableId);
    }

    [Fact]
    public async Task Concurrent_table_creation_assigns_distinct_ids()
    {
        var tables = await Task.WhenAll(AddTableAsync(2), AddTableAsync(4));
        Assert.NotEqual(tables[0].Id, tables[1].Id);
        Assert.Equal(2, (await StateAsync()).Tables.Count);
    }

    [Fact]
    public async Task Arrival_is_persisted_and_initialization_is_repeatable()
    {
        var table = await AddTableAsync(2);
        var group = await ArriveAsync(2);
        Assert.Equal(GroupStatus.Seated, group.Status);
        Assert.Equal(table.Id, group.TableId);

        await using var secondHost = new ApiFactory(connectionString);
        using var secondClient = secondHost.CreateClient();
        Assert.Equal(group, await GetGroupAsync(group.Id, secondClient));
        var state = await StateAsync();
        Assert.Single(state.Tables);
        Assert.Equal(2, state.Tables.Single(t => t.Id == table.Id).OccupiedSeats);
    }

    [Fact]
    public async Task Migration_history_matches_the_current_model()
    {
        await using var db = CreateDbContext();
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());

        await using var blocker = CreateDbContext();
        await using var transaction = await blocker.BeginTransactionAsync(true, CancellationToken.None);
        var previousTimeout = db.Database.GetCommandTimeout();
        var initialization = db.InitializeAsync();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6));
            Assert.False(initialization.IsCompleted);
        }
        finally
        {
            await transaction.RollbackAsync();
            await initialization;
        }
        Assert.Equal(previousTimeout, db.Database.GetCommandTimeout());
    }

    [Fact]
    public async Task Completion_seats_waiters_and_preserves_order_of_those_still_waiting()
    {
        await AddTableAsync(4);
        var seated = await ArriveAsync(4);
        var large = await ArriveAsync(6);
        var small = await ArriveAsync(2);
        var later = await ArriveAsync(3);
        Assert.Equal(GroupStatus.Waiting, large.Status);
        Assert.Equal(GroupStatus.Waiting, small.Status);

        // Free 4 seats: skip 6, seat 2, keep 3 waiting.
        using var response = await client.PostAsync($"/api/groups/{seated.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completed = (await response.Content.ReadFromJsonAsync<GroupResponse>(Json))!;
        Assert.Equal(GroupStatus.Completed, completed.Status);
        Assert.Equal(seated.TableId, completed.TableId);
        Assert.NotNull(completed.EndedAt);
        Assert.Equal(GroupStatus.Seated, (await GetGroupAsync(small.Id)).Status);
        var state = await StateAsync();
        var waiting = state.Groups.Where(g => g.Status == GroupStatus.Waiting);
        Assert.Equal(new[] { large.Id, later.Id }, waiting.Select(g => g.Id));

        await AssertProblemAsync(await client.PostAsync($"/api/groups/{seated.Id}/complete", null), 409);
        var afterRepeat = await StateAsync();
        Assert.Equal(state.Groups.ToArray(), afterRepeat.Groups.ToArray());
        Assert.Equal(state.Tables.ToArray(), afterRepeat.Tables.ToArray());
    }

    [Fact]
    public async Task Leaving_retains_history_and_cannot_be_repeated()
    {
        await AddTableAsync(2);
        var seated = await ArriveAsync(2);
        var waiting = await ArriveAsync(1);
        using var left = await client.PostAsync($"/api/groups/{waiting.Id}/leave", null);
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        Assert.Equal(GroupStatus.Left, (await GetGroupAsync(waiting.Id)).Status);
        Assert.DoesNotContain((await StateAsync()).Groups, g => g.Status == GroupStatus.Waiting);
        await AssertProblemAsync(await client.PostAsync($"/api/groups/{waiting.Id}/leave", null), 409);
        await AssertProblemAsync(await client.PostAsync($"/api/groups/{waiting.Id}/complete", null), 409);
        using var completed = await client.PostAsync($"/api/groups/{seated.Id}/complete", null);
        completed.EnsureSuccessStatusCode();
        Assert.Equal(GroupStatus.Left, (await GetGroupAsync(waiting.Id)).Status);

        for (var i = 0; i < 100; i++)
        {
            var group = await ArriveAsync(6);
            using var response = await client.PostAsync($"/api/groups/{group.Id}/leave", null);
            response.EnsureSuccessStatusCode();
        }
        var active = await ArriveAsync(6);
        var page = await StateAsync();
        Assert.Equal(101, page.Groups.Count);
        Assert.Contains(page.Groups, g => g.Id == active.Id);
        Assert.NotNull(page.NextHistoryBeforeId);
        var next = (await client.GetFromJsonAsync<RestaurantResponse>(
            $"/api/restaurant?historyBeforeId={page.NextHistoryBeforeId}", Json))!;
        Assert.Null(next.NextHistoryBeforeId);
        Assert.Equal(new[] { seated.Id, waiting.Id, active.Id }, next.Groups.Select(g => g.Id));
    }

    [Fact]
    public async Task Invalid_transitions_and_unknown_groups_return_problem_details()
    {
        await AddTableAsync(2);
        var seated = await ArriveAsync(2);
        var waiting = await ArriveAsync(6);
        await AssertProblemAsync(await client.PostAsync($"/api/groups/{waiting.Id}/complete", null), 409);
        await AssertProblemAsync(await client.PostAsync($"/api/groups/{seated.Id}/leave", null), 409);
        await AssertProblemAsync(await client.PostAsync("/api/groups/99999/leave", null), 404);
        await AssertProblemAsync(await client.PostAsync("/api/groups/99999/complete", null), 404);
    }

    [Theory]
    [InlineData("{\"size\":0}")]
    [InlineData("{\"size\":7}")]
    [InlineData("{}")]
    public async Task Invalid_arrivals_return_400_without_writing_data(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        await AssertProblemAsync(await client.PostAsync("/api/groups", content), 400, "Size");
        var state = await StateAsync();
        Assert.Empty(state.Groups);
    }

    [Fact]
    public async Task Concurrent_arrivals_from_two_hosts_cannot_take_the_same_last_seats()
    {
        await AddTableAsync(2);
        var pause = new PauseArrivalInterceptor();
        await using var firstHost = new ApiFactory(connectionString, pause);
        using var firstClient = firstHost.CreateClient();
        await using var secondHost = new ApiFactory(connectionString);
        using var secondClient = secondHost.CreateClient();
        var first = ArriveAsync(2, firstClient);
        Task<GroupResponse>? second = null;
        try
        {
            var sessionId = await pause.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = ArriveAsync(2, secondClient);
            await AssertBlockedByAsync(sessionId);
        }
        finally
        {
            pause.Resume.TrySetResult();
            await first;
            if (second is not null) await second;
        }
        var groups = new[] { await first, await second! };
        Assert.Single(groups, g => g.Status == GroupStatus.Seated);
        Assert.Single(groups, g => g.Status == GroupStatus.Waiting);
        var state = await StateAsync();
        Assert.Equal(2, state.Tables.Sum(t => t.OccupiedSeats));
        Assert.All(state.Tables, t => Assert.InRange(t.OccupiedSeats, 0, t.Capacity));
    }

    [Fact]
    public async Task Failure_during_automatic_seating_rolls_back_completion_and_all_seating_changes()
    {
        await AddTableAsync(4);
        var seated = await ArriveAsync(4);
        var first = await ArriveAsync(2);
        var second = await ArriveAsync(2);
        await using var failingHost = new ApiFactory(connectionString, new FailSeatingInterceptor(second.Id));
        using var failingClient = failingHost.CreateClient();
        await AssertProblemAsync(await failingClient.PostAsync($"/api/groups/{seated.Id}/complete", null), 500);
        Assert.Equal(GroupStatus.Seated, (await GetGroupAsync(seated.Id)).Status);
        Assert.Equal(GroupStatus.Waiting, (await GetGroupAsync(first.Id)).Status);
        Assert.Equal(GroupStatus.Waiting, (await GetGroupAsync(second.Id)).Status);
        using var success = await client.PostAsync($"/api/groups/{seated.Id}/complete", null);
        success.EnsureSuccessStatusCode();
        Assert.Equal(GroupStatus.Seated, (await GetGroupAsync(first.Id)).Status);
        Assert.Equal(GroupStatus.Seated, (await GetGroupAsync(second.Id)).Status);
    }

    private async Task AssertBlockedByAsync(int sessionId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = @sessionId;", connection);
        command.Parameters.AddWithValue("@sessionId", sessionId);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if ((int)(await command.ExecuteScalarAsync())! > 0) return;
            await Task.Delay(50);
        }
        Assert.Fail("Second arrival did not wait for the first transaction.");
    }

    private RestaurantDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlServer(connectionString).Options);

    private async Task<GroupResponse> ArriveAsync(int size, HttpClient? http = null)
    {
        using var response = await (http ?? client).PostAsJsonAsync("/api/groups", new { size });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupResponse>(Json))!;
    }

    private async Task<TableResponse> AddTableAsync(int capacity, HttpClient? http = null)
    {
        using var response = await (http ?? client).PostAsJsonAsync("/api/tables", new { capacity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TableResponse>(Json))!;
    }

    private async Task<GroupResponse> GetGroupAsync(long id, HttpClient? http = null) =>
        (await StateAsync(http)).Groups.Single(g => g.Id == id);

    private async Task<RestaurantResponse> StateAsync(HttpClient? http = null) =>
        (await (http ?? client).GetFromJsonAsync<RestaurantResponse>("/api/restaurant", Json))!;

    private async Task<int> CountTriggersAsync()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.triggers WHERE is_ms_shipped = 0;", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, int expectedStatus, string? field = null)
    {
        using (response)
        {
            Assert.Equal(expectedStatus, (int)response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            using var problem = JsonDocument.Parse(body);
            Assert.Equal(expectedStatus, problem.RootElement.GetProperty("status").GetInt32());
            Assert.True(problem.RootElement.TryGetProperty("title", out _));
            if (field is not null)
            {
                var errors = problem.RootElement.GetProperty("errors").GetProperty(field);
                Assert.NotEmpty(errors.EnumerateArray());
                Assert.All(errors.EnumerateArray(), error => Assert.False(string.IsNullOrWhiteSpace(error.GetString())));
            }
            Assert.DoesNotContain("SqlException", body);
            Assert.DoesNotContain("Injected test failure", body);
        }
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
