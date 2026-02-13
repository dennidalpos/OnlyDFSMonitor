namespace OnlyDFSMonitor.Core;

public sealed class CollectionEngine
{
    public async Task<Snapshot> CollectAsync(MonitorConfiguration config, CancellationToken ct)
    {
        var snapshot = new Snapshot();

        foreach (var ns in config.Namespaces.Where(n => n.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            snapshot.Namespaces.Add(new NamespaceStatus
            {
                Path = ns.Path,
                Health = HealthState.Unknown,
                Targets = [new() { UncPath = ns.Path, Reachable = true }]
            });
        }

        foreach (var group in config.DfsrGroups)
        {
            ct.ThrowIfCancellationRequested();
            snapshot.DfsrGroups.Add(new DfsrGroupStatus
            {
                GroupName = group,
                Health = HealthState.Unknown,
                Backlogs = [new() { Source = "member-a", Destination = "member-b", Folder = null, BacklogCount = null, Health = HealthState.Unknown, Details = "Query delegated to PowerShell collector on Windows host." }]
            });
        }

        snapshot.OverallHealth = snapshot.Namespaces.Concat<object>(snapshot.DfsrGroups).Any() ? HealthState.Unknown : HealthState.Ok;
        await Task.Yield();
        return snapshot;
    }
}
