using PIYA_API.Service.Interface;

namespace PIYA_API.Middleware;

public sealed class WebhookDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IWebhookService>();
                await service.ProcessPendingDeliveriesAsync(25, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Durable webhook delivery worker failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
