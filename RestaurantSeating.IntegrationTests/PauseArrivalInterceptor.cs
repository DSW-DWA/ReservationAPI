using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantSeating.Api.Models.Entities;

namespace RestaurantSeating.IntegrationTests;

public sealed class PauseArrivalInterceptor : SaveChangesInterceptor
{
    public TaskCompletionSource<int> Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var db = eventData.Context!;
        if (db.ChangeTracker.Entries<GuestGroup>().Any(entry => entry.State == EntityState.Added))
        {
            // Hold the application lock before the first INSERT.
            Paused.TrySetResult(((SqlConnection)db.Database.GetDbConnection()).ServerProcessId);
            await Resume.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        return result;
    }
}
