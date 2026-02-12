using DfsMonitor.Shared;

namespace DfsMonitor.Service;

public interface ICollectorOrchestrator
{
    Task<MonitorConfig> LoadConfigAsync(CancellationToken cancellationToken);
    Task<CollectionSnapshot> RunCollectionAsync(MonitorConfig config, CancellationToken cancellationToken);
}

public sealed class CollectorOrchestrator : ICollectorOrchestrator
{
    private readonly ILogger<CollectorOrchestrator> _logger;
    private readonly IDfsNamespaceCollector _namespaceCollector;
    private readonly IDfsReplicationCollector _replicationCollector;

    public CollectorOrchestrator(ILogger<CollectorOrchestrator> logger, IDfsNamespaceCollector namespaceCollector, IDfsReplicationCollector replicationCollector)
    {
        _logger = logger;
        _namespaceCollector = namespaceCollector;
        _replicationCollector = replicationCollector;
    }

    public async Task<MonitorConfig> LoadConfigAsync(CancellationToken cancellationToken)
    {
        var bootstrap = new MonitorConfig();
        var store = new UncConfigStore(bootstrap.Storage.ConfigUncPath, bootstrap.Storage.LocalCacheRootPath);
        return await store.LoadAsync(cancellationToken);
    }

    public async Task<CollectionSnapshot> RunCollectionAsync(MonitorConfig config, CancellationToken cancellationToken)
    {
        var snapshot = new CollectionSnapshot { CreatedAtUtc = DateTimeOffset.UtcNow, OverallHealth = HealthState.Ok };

        if (config.Collectors.DfsNamespace)
            snapshot.Namespaces = await _namespaceCollector.CollectAsync(config, cancellationToken);

        if (config.Collectors.DfsReplication)
            snapshot.DfsrGroups = await _replicationCollector.CollectAsync(config, cancellationToken);

        if (snapshot.Namespaces.Any(x => x.Health == HealthState.Critical) || snapshot.DfsrGroups.Any(x => x.Health == HealthState.Critical))
            snapshot.OverallHealth = HealthState.Critical;
        else if (snapshot.Namespaces.Any(x => x.Health == HealthState.Warn) || snapshot.DfsrGroups.Any(x => x.Health == HealthState.Warn))
            snapshot.OverallHealth = HealthState.Warn;

        var statusStore = new StatusStore();
        await statusStore.SaveSnapshotAsync(snapshot, config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath, cancellationToken);
        _logger.LogInformation("Collection completed at {Timestamp} with state {State}", snapshot.CreatedAtUtc, snapshot.OverallHealth);
        return snapshot;
    }
}
