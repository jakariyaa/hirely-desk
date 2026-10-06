using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CvPlatform.Web;

public sealed class SupportTicketSweepService(IServiceProvider services, ILogger<SupportTicketSweepService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<CvPlatform.Infrastructure.Support.SupportTicketProcessor>();
                await processor.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Support ticket sweep failed; will retry in {Interval}", Interval);
                await Task.Delay(Interval, stoppingToken);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
