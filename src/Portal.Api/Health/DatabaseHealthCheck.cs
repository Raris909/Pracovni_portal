using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Portal.Api.Health;

public sealed class DatabaseHealthCheck(NpgsqlDataSource database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = database.CreateCommand("SELECT count(*) FROM portal.roles WHERE name IN ('candidate', 'company')");
            var count = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
            return count == 2 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Required roles are missing.");
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("Database is unavailable or not initialized.", exception);
        }
    }
}
