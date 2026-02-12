using DfsMonitor.Shared;
using FluentAssertions;

namespace DfsMonitor.Tests;

public class StatusStoreTests
{
    [Fact]
    public async Task SaveAndLoadLatest_Works()
    {
        var root = Path.Combine(Path.GetTempPath(), "dfs-monitor-status", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var store = new StatusStore();
        var snapshot = new CollectionSnapshot
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OverallHealth = HealthState.Warn,
            Namespaces = [new NamespaceSnapshot { NamespaceId = "x", NamespacePath = "\\\\contoso\\dfs" }]
        };

        await store.SaveSnapshotAsync(snapshot, root, root);
        var loaded = await store.LoadLatestAsync(root, root);

        loaded.Should().NotBeNull();
        loaded!.OverallHealth.Should().Be(HealthState.Warn);
    }
}
