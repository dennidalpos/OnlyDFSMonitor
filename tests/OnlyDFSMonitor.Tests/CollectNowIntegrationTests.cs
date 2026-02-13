namespace OnlyDFSMonitor.Tests;

public class CollectNowIntegrationTests
{
    [Fact]
    public async Task CreatingCommandAndCollecting_ShouldPersistSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "onlydfsmonitor-integration", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config.json");
        var commandPath = Path.Combine(root, "commands", "collect-now.json");
        var snapshotPath = Path.Combine(root, "status", "latest.json");

        var store = new JsonFileStore();
        var engine = new CollectionEngine();

        var config = new MonitorConfiguration
        {
            Storage = new StorageOptions
            {
                ConfigPath = configPath,
                CommandPath = commandPath,
                SnapshotPath = snapshotPath
            },
            Namespaces = [new NamespaceDefinition { Path = "\\\\domain\\dfs\\public" }]
        };

        await store.SaveAsync(configPath, config, CancellationToken.None);
        Directory.CreateDirectory(Path.GetDirectoryName(commandPath)!);
        await File.WriteAllTextAsync(commandPath, "{}", CancellationToken.None);

        var loaded = await store.LoadAsync(configPath, () => new MonitorConfiguration(), CancellationToken.None);
        File.Exists(loaded.Storage.CommandPath).Should().BeTrue();
        File.Delete(loaded.Storage.CommandPath);

        var snapshot = await engine.CollectAsync(loaded, CancellationToken.None);
        await store.SaveAsync(snapshotPath, snapshot, CancellationToken.None);

        File.Exists(snapshotPath).Should().BeTrue();
    }
}
