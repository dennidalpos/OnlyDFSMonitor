using DfsMonitor.Shared;

namespace DfsMonitor.Service;

public sealed class CollectionWorker : BackgroundService
{
    private readonly ILogger<CollectionWorker> _logger;
    private readonly ICollectorOrchestrator _orchestrator;

    public CollectionWorker(ILogger<CollectionWorker> logger, ICollectorOrchestrator orchestrator)
    {
        _logger = logger;
        _orchestrator = orchestrator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DFS monitor worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = await _orchestrator.LoadConfigAsync(stoppingToken);
                await _orchestrator.RunCollectionAsync(config, stoppingToken);
                var delay = TimeSpan.FromSeconds(Math.Max(10, config.Collection.PollingIntervalSeconds));
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Collection cycle failed");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }
}
