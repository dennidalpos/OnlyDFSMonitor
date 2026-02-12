using DfsMonitor.Shared;
using FluentAssertions;

namespace DfsMonitor.Tests;

public class ConfigStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTripsConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "dfs-monitor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new UncConfigStore(Path.Combine(root, "config.json"), root);

        var config = new MonitorConfig();
        config.Namespaces.Add(new MonitoredNamespace { Path = "\\\\contoso\\dfs" });

        await store.SaveAsync(config);
        var loaded = await store.LoadAsync();

        loaded.Namespaces.Should().ContainSingle();
        loaded.Version.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Load_DefaultsWhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "dfs-monitor-tests", Guid.NewGuid().ToString("N"));
        var store = new UncConfigStore(Path.Combine(root, "config.json"), root);

        var loaded = await store.LoadAsync();
        loaded.Collection.PollingIntervalSeconds.Should().BeGreaterThan(0);
    }
}
