using System.Globalization;
using System.ServiceProcess;
using System.Text;
using CsvHelper;
using DfsMonitor.Shared;
using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization();
builder.Services.AddRazorPages();

builder.Services.AddSingleton(sp =>
{
    var cfg = new MonitorConfig();
    return new UncConfigStore(cfg.Storage.ConfigUncPath, cfg.Storage.LocalCacheRootPath);
});
builder.Services.AddSingleton<IStatusStore, StatusStore>();
builder.Services.AddSingleton<IRuntimeStateStore, RuntimeStateStore>();
builder.Services.AddSingleton<ICommandQueue, FileCommandQueue>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.MapGet("/api/health", async (IStatusStore statusStore, UncConfigStore configStore, IRuntimeStateStore runtimeStateStore) =>
{
    var config = await configStore.LoadAsync();
    var latest = await statusStore.LoadLatestAsync(config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath);
    var runtime = await runtimeStateStore.LoadAsync(config.Storage.LocalCacheRootPath, config.Storage.RuntimeStatePath);
    return Results.Ok(new
    {
        status = latest?.OverallHealth.ToString() ?? "Unknown",
        latest = latest?.CreatedAtUtc,
        runtime
    });
}).RequireAuthorization();

app.MapGet("/api/config", async (UncConfigStore store) => Results.Ok(await store.LoadAsync())).RequireAuthorization();

app.MapPut("/api/config", async (MonitorConfig config, UncConfigStore store) =>
{
    await store.SaveAsync(config);
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/collect/now", async (HttpContext http, UncConfigStore configStore, ICommandQueue queue) =>
{
    var config = await configStore.LoadAsync();
    var command = new CollectNowCommand
    {
        RequestedBy = http.User?.Identity?.Name ?? "api-user",
        Reason = "Manual collect-now request"
    };
    await queue.EnqueueCollectNowAsync(config, command);
    return Results.Accepted(value: command);
}).RequireAuthorization();

app.MapGet("/api/service/status", async (UncConfigStore configStore, IRuntimeStateStore runtimeStateStore) =>
{
    var cfg = await configStore.LoadAsync();
    var runtime = await runtimeStateStore.LoadAsync(cfg.Storage.LocalCacheRootPath, cfg.Storage.RuntimeStatePath);
    var status = GetServiceStatus();
    return Results.Ok(new ServiceControlResult { ServiceStatus = status, Runtime = runtime });
}).RequireAuthorization();

app.MapPost("/api/service/start", () =>
{
    TryControlService(controller =>
    {
        if (controller.Status == ServiceControllerStatus.Stopped || controller.Status == ServiceControllerStatus.StopPending)
        {
            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }
    });
    return Results.Accepted();
}).RequireAuthorization();

app.MapPost("/api/service/stop", () =>
{
    TryControlService(controller =>
    {
        if (controller.Status == ServiceControllerStatus.Running || controller.Status == ServiceControllerStatus.StartPending)
        {
            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
    });
    return Results.Accepted();
}).RequireAuthorization();

app.MapGet("/api/status/latest", async (IStatusStore statusStore, UncConfigStore configStore) =>
{
    var config = await configStore.LoadAsync();
    var latest = await statusStore.LoadLatestAsync(config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath);
    return latest is null ? Results.NotFound() : Results.Ok(latest);
}).RequireAuthorization();

app.MapGet("/api/status/namespaces/{id}", async (string id, IStatusStore statusStore, UncConfigStore configStore) =>
{
    var config = await configStore.LoadAsync();
    var latest = await statusStore.LoadLatestAsync(config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath);
    var item = latest?.Namespaces.FirstOrDefault(x => x.NamespaceId.Equals(id, StringComparison.OrdinalIgnoreCase));
    return item is null ? Results.NotFound() : Results.Ok(item);
}).RequireAuthorization();

app.MapGet("/api/status/dfsr/{id}", async (string id, IStatusStore statusStore, UncConfigStore configStore) =>
{
    var config = await configStore.LoadAsync();
    var latest = await statusStore.LoadLatestAsync(config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath);
    var item = latest?.DfsrGroups.FirstOrDefault(x => x.GroupName.Equals(id, StringComparison.OrdinalIgnoreCase));
    return item is null ? Results.NotFound() : Results.Ok(item);
}).RequireAuthorization();

app.MapGet("/api/report/latest.json", async (IStatusStore statusStore, UncConfigStore configStore) =>
{
    var config = await configStore.LoadAsync();
    var latest = await statusStore.LoadLatestAsync(config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath);
    return latest is null ? Results.NotFound() : Results.Json(latest, JsonDefaults.Options);
}).RequireAuthorization();

app.MapGet("/api/report/latest.csv", async (IStatusStore statusStore, UncConfigStore configStore) =>
{
    var config = await configStore.LoadAsync();
    var latest = await statusStore.LoadLatestAsync(config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath);
    if (latest is null) return Results.NotFound();

    await using var stream = new MemoryStream();
    await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
    await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

    csv.WriteField("Namespace");
    csv.WriteField("Folder");
    csv.WriteField("Target");
    csv.WriteField("Server");
    csv.WriteField("Reachable");
    csv.WriteField("PriorityClass");
    csv.WriteField("PriorityRank");
    csv.WriteField("Ordering");
    csv.NextRecord();

    foreach (var ns in latest.Namespaces)
    foreach (var folder in ns.Folders)
    foreach (var target in folder.Targets)
    {
        csv.WriteField(ns.NamespacePath);
        csv.WriteField(folder.FolderPath);
        csv.WriteField(target.UncPath);
        csv.WriteField(target.Server);
        csv.WriteField(target.Reachable);
        csv.WriteField(target.PriorityClass);
        csv.WriteField(target.PriorityRank);
        csv.WriteField(target.Ordering);
        csv.NextRecord();
    }

    await writer.FlushAsync();
    return Results.File(stream.ToArray(), "text/csv", "dfs-monitor-latest.csv");
}).RequireAuthorization();

app.Run();

static string GetServiceStatus()
{
    if (!OperatingSystem.IsWindows()) return "Unsupported on non-Windows";
    try
    {
        using var controller = new ServiceController("DfsMonitor.Service");
        return controller.Status.ToString();
    }
    catch (Exception ex)
    {
        return $"Unknown ({ex.Message})";
    }
}

static void TryControlService(Action<ServiceController> action)
{
    if (!OperatingSystem.IsWindows()) return;
    using var controller = new ServiceController("DfsMonitor.Service");
    action(controller);
}
