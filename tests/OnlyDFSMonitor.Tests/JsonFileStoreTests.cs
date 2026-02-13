namespace OnlyDFSMonitor.Tests;

public class JsonFileStoreTests
{
    [Fact]
    public async Task SaveAndLoad_Config_ShouldRoundtrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "onlydfsmonitor-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "config.json");
        var store = new JsonFileStore();

        var config = new MonitorConfiguration { DfsrGroups = ["RG-A"] };
        await store.SaveAsync(path, config, CancellationToken.None);
        var loaded = await store.LoadAsync(path, () => new MonitorConfiguration(), CancellationToken.None);

        loaded.DfsrGroups.Should().ContainSingle().Which.Should().Be("RG-A");
    }
}
