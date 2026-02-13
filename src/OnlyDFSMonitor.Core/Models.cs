namespace OnlyDFSMonitor.Core;

public enum HealthState { Ok, Warn, Critical, Unknown }

public sealed class MonitorConfiguration
{
    public StorageOptions Storage { get; set; } = new();
    public CollectionOptions Collection { get; set; } = new();
    public List<NamespaceDefinition> Namespaces { get; set; } = [];
    public List<string> DfsrGroups { get; set; } = [];
}

public sealed class StorageOptions
{
    public string ConfigPath { get; set; } = @"C:\OnlyDFSMonitor\config\config.json";
    public string SnapshotPath { get; set; } = @"C:\OnlyDFSMonitor\status\latest.json";
    public string CommandPath { get; set; } = @"C:\OnlyDFSMonitor\commands\collect-now.json";
    public string OptionalUncRoot { get; set; } = @"\\fileserver\dfs-monitor";
}

public sealed class CollectionOptions
{
    public int PollingSeconds { get; set; } = 300;
    public int TimeoutSeconds { get; set; } = 20;
    public int RetryCount { get; set; } = 2;
    public int MaxParallelism { get; set; } = 6;
    public int WarnBacklog { get; set; } = 50;
    public int CriticalBacklog { get; set; } = 250;
}

public sealed class NamespaceDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class Snapshot
{
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public HealthState OverallHealth { get; set; } = HealthState.Unknown;
    public List<NamespaceStatus> Namespaces { get; set; } = [];
    public List<DfsrGroupStatus> DfsrGroups { get; set; } = [];
}

public sealed class NamespaceStatus
{
    public string Path { get; set; } = string.Empty;
    public HealthState Health { get; set; } = HealthState.Unknown;
    public List<TargetStatus> Targets { get; set; } = [];
}

public sealed class TargetStatus
{
    public string UncPath { get; set; } = string.Empty;
    public bool Reachable { get; set; }
    public string? Error { get; set; }
}

public sealed class DfsrGroupStatus
{
    public string GroupName { get; set; } = string.Empty;
    public HealthState Health { get; set; } = HealthState.Unknown;
    public List<DfsrBacklogStatus> Backlogs { get; set; } = [];
}

public sealed class DfsrBacklogStatus
{
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string? Folder { get; set; }
    public int? BacklogCount { get; set; }
    public HealthState Health { get; set; } = HealthState.Unknown;
    public string? Details { get; set; }
}
