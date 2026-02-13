namespace DfsMonitor.Shared;

public enum HealthState { Ok, Warn, Critical, Unknown }

public sealed class MonitorConfig
{
    public StorageOptions Storage { get; set; } = new();
    public CollectionOptions Collection { get; set; } = new();
    public List<MonitoredNamespace> Namespaces { get; set; } = [];
    public DfsrOptions Dfsr { get; set; } = new();
    public CollectorToggleOptions Collectors { get; set; } = new();
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; } = 1;
}

public sealed class StorageOptions
{
    public string ConfigUncPath { get; set; } = @"\\fileserver\dfs-monitor\config\config.json";
    public string StatusUncRootPath { get; set; } = @"\\fileserver\dfs-monitor\status";
    public string LocalCacheRootPath { get; set; } = "cache";
    public string RuntimeStatePath { get; set; } = "runtime-state.json";
    public string CommandQueuePath { get; set; } = "commands";
}

public sealed class CollectionOptions
{
    public int PollingIntervalSeconds { get; set; } = 300;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int RetryCount { get; set; } = 2;
    public int MaxParallelism { get; set; } = 8;
    public ThresholdOptions Thresholds { get; set; } = new();
    public int EventLogSampleCount { get; set; } = 100;
}

public sealed class ThresholdOptions
{
    public int WarnUnreachableTargets { get; set; } = 1;
    public int CriticalUnreachableTargets { get; set; } = 3;
    public int WarnBacklog { get; set; } = 50;
    public int CriticalBacklog { get; set; } = 250;
}

public sealed class CollectorToggleOptions
{
    public bool DfsNamespace { get; set; } = true;
    public bool DfsReplication { get; set; } = true;
    public bool EventLog { get; set; } = true;
}

public sealed class MonitoredNamespace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class DfsrOptions
{
    public bool AutoDiscoverGroups { get; set; } = true;
    public List<string> ReplicationGroups { get; set; } = [];
}

public sealed class CollectionSnapshot
{
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public HealthState OverallHealth { get; set; } = HealthState.Unknown;
    public List<NamespaceSnapshot> Namespaces { get; set; } = [];
    public List<DfsrGroupSnapshot> DfsrGroups { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

public sealed class NamespaceSnapshot
{
    public string NamespaceId { get; set; } = string.Empty;
    public string NamespacePath { get; set; } = string.Empty;
    public HealthState Health { get; set; } = HealthState.Unknown;
    public DateTimeOffset LastCheckedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<NamespaceFolderSnapshot> Folders { get; set; } = [];
}

public sealed class NamespaceFolderSnapshot
{
    public string FolderPath { get; set; } = string.Empty;
    public List<NamespaceTargetSnapshot> Targets { get; set; } = [];
}

public sealed class NamespaceTargetSnapshot
{
    public string UncPath { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Share { get; set; } = string.Empty;
    public string PriorityClass { get; set; } = "Unknown";
    public int? PriorityRank { get; set; }
    public int? Ordering { get; set; }
    public string? State { get; set; }
    public bool Reachable { get; set; }
    public long? LatencyMs { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset LastCheckedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DfsrGroupSnapshot
{
    public string GroupName { get; set; } = string.Empty;
    public HealthState Health { get; set; } = HealthState.Unknown;
    public List<DfsrMemberSnapshot> Members { get; set; } = [];
    public List<DfsrConnectionSnapshot> Connections { get; set; } = [];
}

public sealed class DfsrMemberSnapshot
{
    public string MemberName { get; set; } = string.Empty;
    public string ServiceState { get; set; } = "Unknown";
    public HealthState Health { get; set; } = HealthState.Unknown;
    public List<string> RecentWarnings { get; set; } = [];
}

public sealed class DfsrConnectionSnapshot
{
    public string SourceMember { get; set; } = string.Empty;
    public string DestinationMember { get; set; } = string.Empty;
    public string? ReplicatedFolder { get; set; }
    public int? BacklogCount { get; set; }
    public string BacklogState { get; set; } = "Unknown";
    public string? Details { get; set; }
}

public sealed class RuntimeState
{
    public bool IsCollectorRunning { get; set; }
    public DateTimeOffset? LastStartedUtc { get; set; }
    public DateTimeOffset? LastCompletedUtc { get; set; }
    public string? LastResult { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CollectNowCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RequestedBy { get; set; } = "api";
    public string? Reason { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ServiceControlResult
{
    public string ServiceName { get; set; } = "DfsMonitor.Service";
    public string ServiceStatus { get; set; } = "Unknown";
    public RuntimeState Runtime { get; set; } = new();
}
