using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantSeating.Api.Models.Entities;

namespace RestaurantSeating.IntegrationTests;

// Fail after the previous save.
public sealed class FailSeatingInterceptor(long groupId) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<GuestGroup>().Any(entry =>
                entry.State == EntityState.Modified && entry.Entity.Id == groupId && entry.Entity.Status == GroupStatus.Seated))
            throw new InvalidOperationException("Injected test failure.");
        return ValueTask.FromResult(result);
    }
}
