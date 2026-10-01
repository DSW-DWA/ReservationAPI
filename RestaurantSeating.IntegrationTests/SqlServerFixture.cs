using Testcontainers.MsSql;

namespace RestaurantSeating.IntegrationTests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? container;
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        // Optional external SQL Server.
        var configured = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            ConnectionString = configured;
            return;
        }

        container = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await container.StartAsync();
        ConnectionString = container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}
