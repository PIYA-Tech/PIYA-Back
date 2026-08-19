using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Service.Interface;

namespace PIYA_API.Middleware;

/// <summary>
/// Removes QR token rows whose expiry is older than the configured retention
/// period. Runs once after host startup and then daily.
/// </summary>
public sealed class QrTokenCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<SecurityOptions> securityOptions,
    ILogger<QrTokenCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private readonly int _retentionDays = securityOptions.Value.QrTokenCleanupDays;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "QR token cleanup failed; it will retry at the next interval");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Execute one cleanup pass. Public for deterministic health and invocation
    /// tests; normal application code should rely on the hosted schedule.
    /// </summary>
    public async Task<int> CleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = scopeFactory.CreateAsyncScope();
        var qrService = scope.ServiceProvider.GetRequiredService<IQRService>();
        var deleted = await qrService.CleanupExpiredTokensAsync(_retentionDays);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Removed {Count} QR token rows older than {RetentionDays} days",
                deleted,
                _retentionDays);
        }

        return deleted;
    }
}
