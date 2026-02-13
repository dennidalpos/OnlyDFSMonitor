using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlyDFSMonitor.Core;

namespace OnlyDFSMonitor.Service;

public sealed class CollectorWorker : BackgroundService
{
    private readonly JsonFileStore _store;
    private readonly CollectionEngine _engine;
    private readonly ILogger<CollectorWorker> _logger;

    public CollectorWorker(JsonFileStore store, CollectionEngine engine, ILogger<CollectorWorker> logger)
    {
        _store = store;
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fallback = new MonitorConfiguration();
        while (!stoppingToken.IsCancellationRequested)
        {
            var config = await _store.LoadAsync(fallback.Storage.ConfigPath, () => new MonitorConfiguration(), stoppingToken);
            if (File.Exists(config.Storage.CommandPath))
            {
                _logger.LogInformation("Collect-now command detected");
                File.Delete(config.Storage.CommandPath);
            }

            try
            {
                var snapshot = await _engine.CollectAsync(config, stoppingToken);
                await _store.SaveAsync(config.Storage.SnapshotPath, snapshot, stoppingToken);
                _logger.LogInformation("Snapshot saved to {Path}", config.Storage.SnapshotPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Collection failed but worker will continue");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, config.Collection.PollingSeconds)), stoppingToken);
        }
    }
}
