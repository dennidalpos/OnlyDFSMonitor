using DfsMonitor.Shared;

namespace DfsMonitor.Service;

public interface ICollectorOrchestrator
{
    Task<MonitorConfig> LoadConfigAsync(CancellationToken cancellationToken);
    Task<CollectionSnapshot> RunCollectionAsync(MonitorConfig config, CancellationToken cancellationToken, string trigger = "schedule");
    Task<RuntimeState> GetRuntimeStateAsync(MonitorConfig config, CancellationToken cancellationToken);
}

public sealed class CollectorOrchestrator : ICollectorOrchestrator
{
    private readonly ILogger<CollectorOrchestrator> _logger;
    private readonly IDfsNamespaceCollector _namespaceCollector;
    private readonly IDfsReplicationCollector _replicationCollector;
    private readonly IStatusStore _statusStore;
    private readonly IRuntimeStateStore _runtimeStateStore;

    public CollectorOrchestrator(
        ILogger<CollectorOrchestrator> logger,
        IDfsNamespaceCollector namespaceCollector,
        IDfsReplicationCollector replicationCollector,
        IStatusStore statusStore,
        IRuntimeStateStore runtimeStateStore)
    {
        _logger = logger;
        _namespaceCollector = namespaceCollector;
        _replicationCollector = replicationCollector;
        _statusStore = statusStore;
        _runtimeStateStore = runtimeStateStore;
    }

    public async Task<MonitorConfig> LoadConfigAsync(CancellationToken cancellationToken)
    {
        var bootstrap = new MonitorConfig();
        var store = new UncConfigStore(bootstrap.Storage.ConfigUncPath, bootstrap.Storage.LocalCacheRootPath);
        return await store.LoadAsync(cancellationToken);
    }

    public Task<RuntimeState> GetRuntimeStateAsync(MonitorConfig config, CancellationToken cancellationToken)
    {
        return _runtimeStateStore.LoadAsync(config.Storage.LocalCacheRootPath, config.Storage.RuntimeStatePath, cancellationToken);
    }

    public async Task<CollectionSnapshot> RunCollectionAsync(MonitorConfig config, CancellationToken cancellationToken, string trigger = "schedule")
    {
        var state = await _runtimeStateStore.LoadAsync(config.Storage.LocalCacheRootPath, config.Storage.RuntimeStatePath, cancellationToken);
        state.IsCollectorRunning = true;
        state.LastStartedUtc = DateTimeOffset.UtcNow;
        state.LastResult = $"Running ({trigger})";
        state.LastError = null;
        await _runtimeStateStore.SaveAsync(config.Storage.LocalCacheRootPath, config.Storage.RuntimeStatePath, state, cancellationToken);

        var snapshot = new CollectionSnapshot { CreatedAtUtc = DateTimeOffset.UtcNow, OverallHealth = HealthState.Ok };

        try
        {
            if (config.Collectors.DfsNamespace)
                snapshot.Namespaces = await _namespaceCollector.CollectAsync(config, cancellationToken);

            if (config.Collectors.DfsReplication)
                snapshot.DfsrGroups = await _replicationCollector.CollectAsync(config, cancellationToken);

            if (snapshot.Namespaces.Any(x => x.Health == HealthState.Critical) || snapshot.DfsrGroups.Any(x => x.Health == HealthState.Critical))
                snapshot.OverallHealth = HealthState.Critical;
            else if (snapshot.Namespaces.Any(x => x.Health == HealthState.Warn) || snapshot.DfsrGroups.Any(x => x.Health == HealthState.Warn))
                snapshot.OverallHealth = HealthState.Warn;

            await _statusStore.SaveSnapshotAsync(snapshot, config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath, cancellationToken);
            state.LastResult = $"Completed ({trigger})";
            _logger.LogInformation("Collection completed at {Timestamp} trigger={Trigger} state={State}", snapshot.CreatedAtUtc, trigger, snapshot.OverallHealth);
        }
        catch (Exception ex)
        {
            state.LastResult = $"Failed ({trigger})";
            state.LastError = ex.Message;
            _logger.LogError(ex, "Collection failed for trigger={Trigger}", trigger);
            throw;
        }
        finally
        {
            state.IsCollectorRunning = false;
            state.LastCompletedUtc = DateTimeOffset.UtcNow;
            await _runtimeStateStore.SaveAsync(config.Storage.LocalCacheRootPath, config.Storage.RuntimeStatePath, state, cancellationToken);
        }

        return snapshot;
    }
}
