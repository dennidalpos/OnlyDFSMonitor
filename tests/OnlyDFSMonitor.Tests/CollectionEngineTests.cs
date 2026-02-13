namespace OnlyDFSMonitor.Tests;

public class CollectionEngineTests
{
    [Fact]
    public async Task CollectAsync_ShouldEmitNamespacesAndGroups()
    {
        var engine = new CollectionEngine();
        var config = new MonitorConfiguration
        {
            Namespaces = [new NamespaceDefinition { Path = "\\\\domain\\dfs\\shared" }],
            DfsrGroups = ["RG-HQ"]
        };

        var snapshot = await engine.CollectAsync(config, CancellationToken.None);

        snapshot.Namespaces.Should().ContainSingle();
        snapshot.DfsrGroups.Should().ContainSingle();
        snapshot.DfsrGroups[0].Backlogs.Should().ContainSingle();
    }
}
