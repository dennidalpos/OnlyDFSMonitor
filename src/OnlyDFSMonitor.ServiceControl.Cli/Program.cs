using OnlyDFSMonitor.Core;

if (args.Length == 0)
{
    Console.WriteLine("Usage: OnlyDFSMonitor.ServiceControl.Cli <install|start|stop|status>");
    return;
}

var manager = new ServiceManager();
var cmd = args[0].ToLowerInvariant();
var scArgs = cmd switch
{
    "install" => "create OnlyDFSMonitorService binPath= \"C:\\OnlyDFSMonitor\\OnlyDFSMonitor.Service.exe\" start= auto",
    "start" => "start OnlyDFSMonitorService",
    "stop" => "stop OnlyDFSMonitorService",
    "status" => "query OnlyDFSMonitorService",
    _ => throw new ArgumentOutOfRangeException(nameof(args), "Invalid command")
};

var exitCode = await manager.RunScAsync(scArgs, CancellationToken.None);
Console.WriteLine($"sc.exe exit code: {exitCode}");
