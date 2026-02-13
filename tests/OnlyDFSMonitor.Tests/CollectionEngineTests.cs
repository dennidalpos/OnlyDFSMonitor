using System.Text.Json;

namespace OnlyDFSMonitor.Tests;

public class CollectionEngineTests
{
    [Fact]
    public async Task CollectAsync_ShouldEmitNamespacesAndGroups_WhenGroupsProvided()
    {
        var engine = new CollectionEngine(new FakeRunner("[]"));
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

    [Fact]
    public async Task CollectAsync_ShouldAutoDiscoverGroups_WhenNotConfigured()
    {
        var payload = JsonSerializer.Serialize(new[] { "RG-A", "RG-B" });
        var engine = new CollectionEngine(new FakeRunner(payload));
        var config = new MonitorConfiguration
        {
            Namespaces = [new NamespaceDefinition { Path = "\\\\domain\\dfs\\shared" }]
        };

        var snapshot = await engine.CollectAsync(config, CancellationToken.None);

        snapshot.DfsrGroups.Select(x => x.GroupName).Should().BeEquivalentTo(["RG-A", "RG-B"]);
    }

    private sealed class FakeRunner : ISystemCommandRunner
    {
        private readonly string _payload;

        public FakeRunner(string payload) => _payload = payload;

        public Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string fileName, string arguments, CancellationToken ct)
            => Task.FromResult((0, _payload, string.Empty));
    }
}
