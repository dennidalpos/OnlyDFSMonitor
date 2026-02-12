using System.Globalization;
using System.ServiceProcess;
using System.Runtime.Versioning;
using System.Text;
using CsvHelper;
using DfsMonitor.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var authMode = builder.Configuration["Auth:Mode"] ?? "Negotiate";

if (authMode.Equals("Jwt", StringComparison.OrdinalIgnoreCase))
{
    var issuer = builder.Configuration["Auth:Jwt:Issuer"] ?? "dfs-monitor";
    var audience = builder.Configuration["Auth:Jwt:Audience"] ?? "dfs-monitor-api";
    var key = builder.Configuration["Auth:Jwt:SigningKey"] ?? "dev-placeholder-signing-key-change-me";

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
        });
}
else
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
}

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

app.MapGet("/api/auth/jwt-placeholder", (IConfiguration configuration) =>
{
    var issuer = configuration["Auth:Jwt:Issuer"] ?? "dfs-monitor";
    var audience = configuration["Auth:Jwt:Audience"] ?? "dfs-monitor-api";
    return Results.Ok(new
    {
        tokenType = "Bearer",
        message = "JWT mode is enabled. Integrate your identity provider to mint tokens using Auth:Jwt settings.",
        issuer,
        audience
    });
}).AllowAnonymous();

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
    var result = ServiceControlApi.GetStatus();
    result.Runtime = runtime;
    return Results.Ok(result);
}).RequireAuthorization();

app.MapPost("/api/service/start", () => ServiceControlApi.Start()).RequireAuthorization();
app.MapPost("/api/service/stop", () => ServiceControlApi.Stop()).RequireAuthorization();

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
    csv.WriteField("State");
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
        csv.WriteField(target.State);
        csv.NextRecord();
    }

    await writer.FlushAsync();
    return Results.File(stream.ToArray(), "text/csv", "dfs-monitor-latest.csv");
}).RequireAuthorization();

app.Run();

public partial class Program;

internal static class ServiceControlApi
{
    public static ServiceControlResult GetStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ServiceControlResult { ServiceStatus = "Unsupported on non-Windows" };
        }

        return GetStatusWindows();
    }

    public static IResult Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Results.Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Service start is unsupported on non-Windows hosts.");
        }

        return ControlServiceWindows(
            shouldRun: status => status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending,
            mutate: controller => controller.Start(),
            target: ServiceControllerStatus.Running,
            action: "start");
    }

    public static IResult Stop()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Results.Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Service stop is unsupported on non-Windows hosts.");
        }

        return ControlServiceWindows(
            shouldRun: status => status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending,
            mutate: controller => controller.Stop(),
            target: ServiceControllerStatus.Stopped,
            action: "stop");
    }

    [SupportedOSPlatform("windows")]
    private static ServiceControlResult GetStatusWindows()
    {
        try
        {
            using var controller = new ServiceController("DfsMonitor.Service");
            return new ServiceControlResult { ServiceStatus = controller.Status.ToString() };
        }
        catch (Exception ex)
        {
            return new ServiceControlResult { ServiceStatus = $"Unknown ({ex.Message})" };
        }
    }

    [SupportedOSPlatform("windows")]
    private static IResult ControlServiceWindows(Func<ServiceControllerStatus, bool> shouldRun, Action<ServiceController> mutate, ServiceControllerStatus target, string action)
    {
        try
        {
            using var controller = new ServiceController("DfsMonitor.Service");
            if (shouldRun(controller.Status))
            {
                mutate(controller);
                controller.WaitForStatus(target, TimeSpan.FromSeconds(20));
            }

            return Results.Ok(new ServiceControlResult { ServiceStatus = controller.Status.ToString() });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "DfsMonitor.Service was not found.", detail: ex.Message);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: $"Service {action} failed due to permissions.", detail: ex.Message);
        }
        catch (Exception ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: $"Unexpected error while trying to {action} DfsMonitor.Service.", detail: ex.Message);
        }
    }
}
