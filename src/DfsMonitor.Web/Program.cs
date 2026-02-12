using System.Globalization;
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

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.MapGet("/api/health", async (IStatusStore statusStore, UncConfigStore configStore) =>
{
    var config = await configStore.LoadAsync();
    var latest = await statusStore.LoadLatestAsync(config.Storage.StatusUncRootPath, config.Storage.LocalCacheRootPath);
    return Results.Ok(new { status = latest?.OverallHealth.ToString() ?? "Unknown", latest = latest?.CreatedAtUtc });
}).RequireAuthorization();

app.MapGet("/api/config", async (UncConfigStore store) => Results.Ok(await store.LoadAsync())).RequireAuthorization();
app.MapPut("/api/config", async (MonitorConfig config, UncConfigStore store) => { await store.SaveAsync(config); return Results.NoContent(); }).RequireAuthorization();

app.MapPost("/api/collect/now", () => Results.Accepted(value: new { message = "Collection trigger accepted. TODO integrate with service controller." })).RequireAuthorization();

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

    csv.WriteField("Namespace"); csv.WriteField("Folder"); csv.WriteField("Target"); csv.WriteField("Reachable"); csv.WriteField("PriorityClass"); csv.WriteField("PriorityRank"); csv.NextRecord();
    foreach (var ns in latest.Namespaces)
    foreach (var folder in ns.Folders)
    foreach (var target in folder.Targets)
    {
        csv.WriteField(ns.NamespacePath); csv.WriteField(folder.FolderPath); csv.WriteField(target.UncPath); csv.WriteField(target.Reachable); csv.WriteField(target.PriorityClass); csv.WriteField(target.PriorityRank); csv.NextRecord();
    }

    await writer.FlushAsync();
    return Results.File(stream.ToArray(), "text/csv", "dfs-monitor-latest.csv");
}).RequireAuthorization();

app.Run();
