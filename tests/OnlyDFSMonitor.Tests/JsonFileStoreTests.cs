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

    [Fact]
    public async Task LoadAsync_ShouldUseFallback_WhenJsonIsInvalid()
    {
        var root = Path.Combine(Path.GetTempPath(), "onlydfsmonitor-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "broken.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "{ not-valid-json", CancellationToken.None);

        var store = new JsonFileStore();
        var loaded = await store.LoadAsync(path, () => new MonitorConfiguration { DfsrGroups = ["fallback"] }, CancellationToken.None);

        loaded.DfsrGroups.Should().ContainSingle().Which.Should().Be("fallback");
    }

    [Fact]
    public async Task LoadStrictAsync_ShouldThrow_WhenJsonMissingOrInvalid()
    {
        var root = Path.Combine(Path.GetTempPath(), "onlydfsmonitor-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "missing.json");
        var store = new JsonFileStore();

        var missing = () => store.LoadStrictAsync<MonitorConfiguration>(path, CancellationToken.None);
        await missing.Should().ThrowAsync<FileNotFoundException>();

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "[]", CancellationToken.None);
        var invalid = () => store.LoadStrictAsync<MonitorConfiguration>(path, CancellationToken.None);
        await invalid.Should().ThrowAsync<InvalidDataException>();
    }
}
