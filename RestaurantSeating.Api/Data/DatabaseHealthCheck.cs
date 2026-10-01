using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RestaurantSeating.Api.Data;

public sealed class DatabaseHealthCheck(RestaurantDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database unavailable.");
}
