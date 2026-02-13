using OnlyDFSMonitor.Core;

namespace OnlyDFSMonitor.Desktop;

public partial class MainWindow : Window
{
    private readonly JsonFileStore _store = new();
    private readonly ServiceManager _serviceManager = new();
    private readonly CollectionEngine _engine = new();

    public MainWindow() => InitializeComponent();

    private MonitorConfiguration BuildConfig() => new()
    {
        Storage = new StorageOptions
        {
            ConfigPath = ConfigPathBox.Text,
            SnapshotPath = SnapshotPathBox.Text,
            OptionalUncRoot = UncRootBox.Text
        },
        Namespaces = NamespacesBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(x => new NamespaceDefinition { Path = x.Trim() }).ToList(),
        DfsrGroups = GroupsBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList()
    };

    private async Task SaveAsync()
    {
        var config = BuildConfig();
        await _store.SaveAsync(config.Storage.ConfigPath, config, CancellationToken.None);
        FooterText.Text = "Configuration saved.";
    }

    private void AppendLog(string message) => LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private async void SaveConfiguration_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync();
        AppendLog("Configuration persisted.");
    }

    private async void InstallService_Click(object sender, RoutedEventArgs e)
    {
        var code = await _serviceManager.RunScAsync("create OnlyDFSMonitorService binPath= \"C:\\OnlyDFSMonitor\\OnlyDFSMonitor.Service.exe\" start= auto", CancellationToken.None);
        AppendLog($"Service install exit code: {code}");
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
        var snapshot = await _engine.CollectAsync(config, CancellationToken.None);
        await _store.SaveAsync(config.Storage.SnapshotPath, snapshot, CancellationToken.None);
        AppendLog($"Collect-now completed with overall health: {snapshot.OverallHealth}");
    }
}
