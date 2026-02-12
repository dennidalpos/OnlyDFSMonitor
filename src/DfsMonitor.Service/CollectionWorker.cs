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

        FileSystemWatcher? watcher = null;
        SemaphoreSlim signal = new(0, 1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = await _orchestrator.LoadConfigAsync(stoppingToken);
                watcher = EnsureWatcher(config, watcher, signal);
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
                await WaitForSignalOrDelayAsync(signal, delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Collection cycle failed");
                await WaitForSignalOrDelayAsync(signal, TimeSpan.FromSeconds(15), stoppingToken);
            }
        }

        watcher?.Dispose();
        signal.Dispose();
    }

    private static FileSystemWatcher EnsureWatcher(MonitorConfig config, FileSystemWatcher? current, SemaphoreSlim signal)
    {
        var root = Path.IsPathRooted(config.Storage.CommandQueuePath)
            ? config.Storage.CommandQueuePath
            : Path.Combine(config.Storage.LocalCacheRootPath, config.Storage.CommandQueuePath);
        Directory.CreateDirectory(root);

        if (current is not null && string.Equals(current.Path, root, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        current?.Dispose();
        var watcher = new FileSystemWatcher(root, "collect-now-*.json")
        {
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
        };

        void Wake(object? _, EventArgs __)
        {
            if (signal.CurrentCount == 0)
            {
                signal.Release();
            }
        }

        watcher.Created += Wake;
        watcher.Changed += Wake;
        watcher.Renamed += Wake;
        watcher.Error += (_, _) => Wake(null, EventArgs.Empty);

        return watcher;
    }

    private static async Task WaitForSignalOrDelayAsync(SemaphoreSlim signal, TimeSpan delay, CancellationToken token)
    {
        var delayTask = Task.Delay(delay, token);
        var signalTask = signal.WaitAsync(token);
        await Task.WhenAny(delayTask, signalTask);
    }
}
