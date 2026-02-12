using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using DfsMonitor.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace DfsMonitor.Tests;

public class WebApiIntegrationTests
{
    [Fact]
    public async Task AuthorizedEndpoints_RequireAuthentication()
    {
        await using var fixture = await TestWebAppFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/config");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CollectNow_QueuesCommand_WhenAuthorized()
    {
        await using var fixture = await TestWebAppFixture.CreateAsync();
        using var client = fixture.CreateAuthorizedClient();

        var response = await client.PostAsync("/api/collect/now", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var queueFiles = Directory.GetFiles(fixture.CommandQueuePath, "collect-now-*.json");
        queueFiles.Should().ContainSingle();

        var payload = await File.ReadAllTextAsync(queueFiles[0]);
        payload.Should().Contain("test-user");
    }

    [Fact]
    public async Task StatusAndReports_ReturnSnapshot_WhenAuthorized()
    {
        await using var fixture = await TestWebAppFixture.CreateAsync();
        using var client = fixture.CreateAuthorizedClient();

        var statusResponse = await client.GetAsync("/api/status/latest");
        var jsonReportResponse = await client.GetAsync("/api/report/latest.json");
        var csvReportResponse = await client.GetAsync("/api/report/latest.csv");

        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        jsonReportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        csvReportResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusContent = await statusResponse.Content.ReadAsStringAsync();
        statusContent.Should().Contain("ns-1");

        var csvContent = await csvReportResponse.Content.ReadAsStringAsync();
        csvContent.Should().Contain("Namespace,Folder,Target");
        csvContent.Should().Contain("\\\\contoso\\dfs");
    }

    private sealed class TestWebAppFactory(string tempRoot, string configPath, string localCachePath)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Mode"] = "Jwt",
                    ["Auth:Jwt:Issuer"] = TestWebAppFixture.JwtIssuer,
                    ["Auth:Jwt:Audience"] = TestWebAppFixture.JwtAudience,
                    ["Auth:Jwt:SigningKey"] = TestWebAppFixture.JwtKey
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<UncConfigStore>();
                services.AddSingleton(new UncConfigStore(configPath, localCachePath));
            });

            builder.UseContentRoot(tempRoot);
        }
    }

    private sealed class TestWebAppFixture : IAsyncDisposable
    {
        public const string JwtIssuer = "dfs-monitor-tests";
        public const string JwtAudience = "dfs-monitor-tests-api";
        public const string JwtKey = "integration-tests-signing-key-32-bytes";

        private readonly string _tempRoot;

        private TestWebAppFixture(string tempRoot, string localCachePath, string configPath, TestWebAppFactory factory)
        {
            _tempRoot = tempRoot;
            LocalCachePath = localCachePath;
            ConfigPath = configPath;
            Factory = factory;
        }

        public string LocalCachePath { get; }
        public string ConfigPath { get; }
        public string CommandQueuePath => Path.Combine(LocalCachePath, "commands");
        public TestWebAppFactory Factory { get; }

        public static async Task<TestWebAppFixture> CreateAsync()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "dfs-monitor-web-tests", Guid.NewGuid().ToString("N"));
            var configRoot = Path.Combine(tempRoot, "config");
            var localCacheRoot = Path.Combine(tempRoot, "cache");
            var statusRoot = Path.Combine(tempRoot, "status");
            var configPath = Path.Combine(configRoot, "config.json");

            Directory.CreateDirectory(configRoot);
            Directory.CreateDirectory(localCacheRoot);
            Directory.CreateDirectory(statusRoot);

            var configStore = new UncConfigStore(configPath, localCacheRoot);
            var monitorConfig = new MonitorConfig
            {
                Storage =
                {
                    ConfigUncPath = configPath,
                    LocalCacheRootPath = localCacheRoot,
                    StatusUncRootPath = statusRoot,
                    RuntimeStatePath = "runtime-state.json",
                    CommandQueuePath = "commands"
                },
                Namespaces = [new MonitoredNamespace { Id = "ns-1", Path = "\\\\contoso\\dfs" }]
            };
            await configStore.SaveAsync(monitorConfig);

            var statusStore = new StatusStore();
            await statusStore.SaveSnapshotAsync(new CollectionSnapshot
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                OverallHealth = HealthState.Ok,
                Namespaces =
                [
                    new NamespaceSnapshot
                    {
                        NamespaceId = "ns-1",
                        NamespacePath = "\\\\contoso\\dfs",
                        Folders =
                        [
                            new NamespaceFolderSnapshot
                            {
                                FolderPath = "\\\\contoso\\dfs\\home",
                                Targets =
                                [
                                    new NamespaceTargetSnapshot
                                    {
                                        UncPath = "\\\\fileserver\\home",
                                        Server = "fileserver",
                                        Reachable = true,
                                        PriorityClass = "GlobalHigh",
                                        PriorityRank = 0,
                                        Ordering = 1,
                                        State = "Online"
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }, statusRoot, localCacheRoot);

            var runtimeStore = new RuntimeStateStore();
            await runtimeStore.SaveAsync(localCacheRoot, "runtime-state.json", new RuntimeState
            {
                IsCollectorRunning = false,
                LastResult = "Completed"
            });

            var factory = new TestWebAppFactory(Directory.GetCurrentDirectory(), configPath, localCacheRoot);
            return new TestWebAppFixture(tempRoot, localCacheRoot, configPath, factory);
        }

        public HttpClient CreateAuthorizedClient()
        {
            var client = Factory.CreateClient();
            var token = CreateJwt();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public ValueTask DisposeAsync()
        {
            Factory.Dispose();
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private static string CreateJwt()
        {
            var creds = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: JwtIssuer,
                audience: JwtAudience,
                claims:
                [
                    new Claim(ClaimTypes.Name, "test-user")
                ],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
