using DfsMonitor.Service;
using DfsMonitor.Shared;
using Microsoft.Extensions.Logging.EventLog;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService();
    builder.Logging.AddEventLog(new EventLogSettings { SourceName = "DfsMonitor.Service" });
}

var loggerConfiguration = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.File("logs/service-.log", rollingInterval: RollingInterval.Day);

if (OperatingSystem.IsWindows())
{
    loggerConfiguration = loggerConfiguration.WriteTo.EventLog(source: "DfsMonitor.Service", manageEventSource: true);
}

Log.Logger = loggerConfiguration.CreateLogger();
builder.Services.AddSerilog();

builder.Services.AddSingleton<ICollectorOrchestrator, CollectorOrchestrator>();
builder.Services.AddSingleton<IDfsNamespaceCollector, DfsNamespaceCollector>();
builder.Services.AddSingleton<IDfsReplicationCollector, DfsReplicationCollector>();
builder.Services.AddSingleton<IStatusStore, StatusStore>();
builder.Services.AddSingleton<IRuntimeStateStore, RuntimeStateStore>();
builder.Services.AddSingleton<ICommandQueue, FileCommandQueue>();
builder.Services.AddHostedService<CollectionWorker>();

var app = builder.Build();
app.Run();
