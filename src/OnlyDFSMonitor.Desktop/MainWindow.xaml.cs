using OnlyDFSMonitor.Core;

namespace OnlyDFSMonitor.Desktop;

public partial class MainWindow : Window
{
    private const string ServiceName = "OnlyDFSMonitorService";

    private readonly JsonFileStore _store = new();
    private readonly ServiceManager _serviceManager = new();
    private readonly CollectionEngine _engine = new();

    public MainWindow()
    {
        InitializeComponent();
        _ = RefreshServiceInfoAsync();
    }

    private MonitorConfiguration BuildConfig() => new()
    {
        Storage = new StorageOptions
        {
            ConfigPath = ConfigPathBox.Text,
            SnapshotPath = SnapshotPathBox.Text,
            OptionalUncRoot = UncRootBox.Text
        },
        Namespaces = NamespacesBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => new NamespaceDefinition { Path = x.Trim() })
            .ToList()
    };

    private async Task SaveAsync()
    {
        var config = BuildConfig();
        await _store.SaveAsync(config.Storage.ConfigPath, config, CancellationToken.None);
        FooterText.Text = "Configuration saved.";
    }

    private void AppendLog(string message) => LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private async Task RefreshServiceInfoAsync()
    {
        try
        {
            var installed = await _serviceManager.IsServiceInstalledAsync(ServiceName, CancellationToken.None);
            AppendLog(installed
                ? "Service installed: collect-now can trigger background service or local mode."
                : "Service not installed: app will run local collect-now mode.");
        }
        catch
        {
            AppendLog("Unable to query service status. Local mode is still available.");
        }
    }

    private async void SaveConfiguration_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync();
        AppendLog("Configuration persisted.");
    }

    private async void InstallService_Click(object sender, RoutedEventArgs e)
    {
        var code = await _serviceManager.RunScAsync("create OnlyDFSMonitorService binPath= \"C:\\OnlyDFSMonitor\\OnlyDFSMonitor.Service.exe\" start= auto", CancellationToken.None);
        AppendLog($"Service install exit code: {code}");
        await RefreshServiceInfoAsync();
    }

    private async void StartService_Click(object sender, RoutedEventArgs e)
    {
        var code = await _serviceManager.RunScAsync("start OnlyDFSMonitorService", CancellationToken.None);
        AppendLog($"Service start exit code: {code}");
    }

    private async void StopService_Click(object sender, RoutedEventArgs e)
    {
        var code = await _serviceManager.RunScAsync("stop OnlyDFSMonitorService", CancellationToken.None);
        AppendLog($"Service stop exit code: {code}");
    }

    private async void CollectNow_Click(object sender, RoutedEventArgs e)
    {
        var config = BuildConfig();
        await SaveAsync();

        var installed = false;
        try
        {
            installed = await _serviceManager.IsServiceInstalledAsync(ServiceName, CancellationToken.None);
        }
        catch
        {
            installed = false;
        }

        if (installed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(config.Storage.CommandPath) ?? ".");
            await File.WriteAllTextAsync(config.Storage.CommandPath, "{}", CancellationToken.None);
            AppendLog("Collect-now command queued for Windows service.");
            return;
        }

        var snapshot = await _engine.CollectAsync(config, CancellationToken.None);
        await _store.SaveAsync(config.Storage.SnapshotPath, snapshot, CancellationToken.None);
        AppendLog($"Local collect-now completed (service not installed). Overall health: {snapshot.OverallHealth}");
    }
}
