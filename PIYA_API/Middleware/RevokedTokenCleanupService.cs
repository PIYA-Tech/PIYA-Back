using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;

namespace PIYA_API.Middleware;

/// <summary>
/// Periodically removes expired rows from the RevokedTokens table.
/// A revoked token whose ExpiresAt has passed is no longer a security
/// concern (the signature validator already rejects expired tokens),
/// so those rows can be safely deleted.
/// Runs once per hour.
/// </summary>
public class RevokedTokenCleanupService(IServiceScopeFactory scopeFactory, ILogger<RevokedTokenCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();

                var deleted = await db.RevokedTokens
                    .Where(r => r.ExpiresAt < DateTime.UtcNow)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deleted > 0)
                    logger.LogInformation("[RevokedTokenCleanup] Removed {Count} expired revoked-token rows", deleted);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "[RevokedTokenCleanup] Error during cleanup — will retry in {Interval}", _interval);
            }

            await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
