using DfsMonitor.Shared;
using FluentAssertions;

namespace DfsMonitor.Tests;

public class RuntimeAndQueueTests
{
    [Fact]
    public async Task RuntimeState_RoundTrip_Works()
    {
        var root = Path.Combine(Path.GetTempPath(), "dfs-monitor-runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var store = new RuntimeStateStore();
        var state = new RuntimeState { IsCollectorRunning = true, LastResult = "Running" };

        await store.SaveAsync(root, "runtime.json", state);
        var loaded = await store.LoadAsync(root, "runtime.json");

        loaded.IsCollectorRunning.Should().BeTrue();
        loaded.LastResult.Should().Be("Running");
    }

    [Fact]
    public async Task CommandQueue_EnqueueDequeue_Works()
    {
        var root = Path.Combine(Path.GetTempPath(), "dfs-monitor-queue", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var cfg = new MonitorConfig();
        cfg.Storage.LocalCacheRootPath = root;

        var queue = new FileCommandQueue();
        await queue.EnqueueCollectNowAsync(cfg, new CollectNowCommand { RequestedBy = "test" });

        var items = await queue.DequeueCollectNowAsync(cfg);
        items.Should().ContainSingle();
        items[0].RequestedBy.Should().Be("test");
    }
}
