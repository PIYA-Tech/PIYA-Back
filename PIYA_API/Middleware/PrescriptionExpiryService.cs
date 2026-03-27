using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;

namespace PIYA_API.Middleware;

/// <summary>
/// Background service that runs every hour and marks Active prescriptions whose
/// ExpiresAt is in the past as Expired.
/// </summary>
public class PrescriptionExpiryService(
    IServiceScopeFactory scopeFactory,
    ILogger<PrescriptionExpiryService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<PrescriptionExpiryService> _logger = logger;
    private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PrescriptionExpiryService started.");

        // Run once on startup, then every hour.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireOverduePrescriptionsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in PrescriptionExpiryService tick.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ExpireOverduePrescriptionsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();

        var now = DateTime.UtcNow;

        // Only update Active prescriptions whose ExpiresAt has passed.
        var expired = await db.Prescriptions
            .Where(p => p.Status == PrescriptionStatus.Active && p.ExpiresAt < now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var rx in expired)
        {
            rx.Status = PrescriptionStatus.Expired;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("PrescriptionExpiryService: marked {Count} prescription(s) as Expired.", expired.Count);
    }
}
