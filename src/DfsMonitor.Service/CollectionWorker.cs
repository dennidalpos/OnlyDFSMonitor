using DfsMonitor.Shared;

namespace DfsMonitor.Service;

public sealed class CollectionWorker : BackgroundService
{
    private readonly ILogger<CollectionWorker> _logger;
    private readonly ICollectorOrchestrator _orchestrator;
    private readonly ICommandQueue _commandQueue;

    public CollectionWorker(ILogger<CollectionWorker> logger, ICollectorOrchestrator orchestrator, ICommandQueue commandQueue)
    {
        _logger = logger;
        _orchestrator = orchestrator;
        _commandQueue = commandQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DFS monitor worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = await _orchestrator.LoadConfigAsync(stoppingToken);
                var commands = await _commandQueue.DequeueCollectNowAsync(config, stoppingToken);

                if (commands.Count > 0)
                {
                    foreach (var cmd in commands)
                    {
                        _logger.LogInformation("Received collect-now command {CommandId} requestedBy={RequestedBy}", cmd.Id, cmd.RequestedBy);
                        await _orchestrator.RunCollectionAsync(config, stoppingToken, trigger: "manual");
                    }
                }
                else
                {
                    await _orchestrator.RunCollectionAsync(config, stoppingToken, trigger: "schedule");
                }

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
