using OnlyDFSMonitor.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnlyDFSMonitor.Core;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices(services =>
    {
        services.AddSingleton<JsonFileStore>();
        services.AddSingleton<CollectionEngine>();
        services.AddHostedService<CollectorWorker>();
    })
    .Build();

await host.RunAsync();
