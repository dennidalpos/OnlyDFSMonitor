using DfsMonitor.Service;
using DfsMonitor.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfsMonitor.Tests;

public class OrchestratorIntegrationTests
{
    [Fact]
    public async Task RunCollectionAsync_PersistsSnapshotAndRuntimeState()
    {
        var nsCollector = new FakeNamespaceCollector();
        var dfsrCollector = new FakeReplicationCollector();
        var statusStore = new StatusStore();
        var runtimeStore = new RuntimeStateStore();
        var orchestrator = new CollectorOrchestrator(new NullLogger<CollectorOrchestrator>(), nsCollector, dfsrCollector, statusStore, runtimeStore);

        var root = Path.Combine(Path.GetTempPath(), "dfs-monitor-orchestrator", Guid.NewGuid().ToString("N"));
        var config = new MonitorConfig
        {
            Storage =
            {
                LocalCacheRootPath = root,
                StatusUncRootPath = Path.Combine(root, "status-out"),
                RuntimeStatePath = "runtime.json"
            }
        };

        var snapshot = await orchestrator.RunCollectionAsync(config, CancellationToken.None, trigger: "manual");
        var latest = await statusStore.LoadLatestAsync(config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath);
        var runtime = await runtimeStore.LoadAsync(config.Storage.LocalCacheRootPath, config.Storage.RuntimeStatePath);

        snapshot.OverallHealth.Should().Be(HealthState.Warn);
        latest.Should().NotBeNull();
        latest!.Namespaces.Should().ContainSingle();
        latest.DfsrGroups.Should().ContainSingle();
        runtime.IsCollectorRunning.Should().BeFalse();
        runtime.LastResult.Should().Contain("Completed");
    }

    private sealed class FakeNamespaceCollector : IDfsNamespaceCollector
    {
        public Task<List<NamespaceSnapshot>> CollectAsync(MonitorConfig config, CancellationToken cancellationToken)
        {
            return Task.FromResult(new List<NamespaceSnapshot>
            {
                new()
                {
                    NamespaceId = "ns1",
                    NamespacePath = "\\\\contoso\\dfs",
                    Health = HealthState.Ok
                }
            });
        }
    }

    private sealed class FakeReplicationCollector : IDfsReplicationCollector
    {
        public Task<List<DfsrGroupSnapshot>> CollectAsync(MonitorConfig config, CancellationToken cancellationToken)
        {
            return Task.FromResult(new List<DfsrGroupSnapshot>
            {
                new()
                {
                    GroupName = "rg1",
                    Health = HealthState.Warn,
                    Members = [new DfsrMemberSnapshot { MemberName = "node1", Health = HealthState.Warn }]
                }
            });
        }
    }
}
