using System.Text;
using Microsoft.Win32;
using OnlyDFSMonitor.Core;

namespace OnlyDFSMonitor.Desktop;

public partial class MainWindow : Window
{
    private const string ServiceName = "OnlyDFSMonitorService";

    private readonly JsonFileStore _store = new();
    private readonly ServiceManager _serviceManager = new();
    private readonly CollectionEngine _engine = new();
    private Snapshot? _lastSnapshot;

    public MainWindow()
    {
        InitializeComponent();
        _ = RefreshServiceInfoAsync();
    }

    private static IEnumerable<string> SplitLines(string raw)
        => raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private List<NamespaceDefinition> ParseNamespaces(bool includeDisabled)
    {
        var parsed = new List<NamespaceDefinition>();

        foreach (var line in SplitLines(NamespacesBox.Text))
        {
            var enabled = true;
            var path = line;

            if (line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase))
            {
                path = line[3..].Trim();
            }
            else if (line.StartsWith("[ ]", StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                path = line[3..].Trim();
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!includeDisabled && !enabled)
            {
                continue;
            }

            parsed.Add(new NamespaceDefinition { Path = path, Enabled = enabled });
        }

        return parsed;
    }

    private MonitorConfiguration BuildConfig()
    {
        var autoDiscoverDfsrGroups = AutoDiscoverDfsrGroupsCheckBox.IsChecked != false;
        var includeDisabledNamespaces = IncludeDisabledNamespacesCheckBox.IsChecked == true;
        var manualGroups = SplitLines(DfsrGroupsBox.Text).ToList();

        return new MonitorConfiguration
        {
            Storage = new StorageOptions
            {
                ConfigPath = ConfigPathBox.Text.Trim(),
                SnapshotPath = SnapshotPathBox.Text.Trim(),
                CommandPath = CommandPathBox.Text.Trim(),
                OptionalUncRoot = UncRootBox.Text.Trim()
            },
            Namespaces = ParseNamespaces(includeDisabledNamespaces),
            DfsrGroups = autoDiscoverDfsrGroups ? [] : manualGroups
        };
    }

    private string GetServiceBinaryPath()
    {
        var path = ServiceBinaryPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Service executable path is required.");
        }

        return path;
    }

    private async Task SaveAsync()
    {
        var config = BuildConfig();
        if (string.IsNullOrWhiteSpace(config.Storage.ConfigPath))
        {
            throw new InvalidOperationException("Config path is required.");
        }

        await _store.SaveAsync(config.Storage.ConfigPath, config, CancellationToken.None);
        FooterText.Text = $"Configuration saved to {config.Storage.ConfigPath}.";
    }

    private void AppendLog(string message) => LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private void ShowError(string title, Exception ex)
    {
        AppendLog($"{title}: {ex.Message}");
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async Task RefreshServiceInfoAsync()
    {
        try
        {
            var status = await _serviceManager.QueryServiceAsync(ServiceName, CancellationToken.None);
            ServiceStatusText.Text = $"Status: {status.RawStatus}";
            AppendLog(status.Installed
                ? $"Service installed. Current state: {status.RawStatus}."
                : "Service not installed: app will run local collect-now mode.");
        }
        catch (Exception ex)
        {
            ServiceStatusText.Text = "Status: unavailable";
            AppendLog($"Unable to query service status: {ex.Message}");
        }
    }

    private async Task RunServiceActionAsync(
        Func<CancellationToken, Task<ScCommandResult>> action,
        string successMessage,
        string failureTitle)
    {
        try
        {
            var result = await action(CancellationToken.None);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"{failureTitle}. sc.exe exit code: {result.ExitCode}. {result.CombinedOutput}");
            }

            AppendLog(successMessage);
            if (!string.IsNullOrWhiteSpace(result.CombinedOutput))
            {
                AppendLog(result.CombinedOutput);
            }

            await RefreshServiceInfoAsync();
        }
        catch (Exception ex)
        {
            ShowError(failureTitle, ex);
        }
    }

    private async void RefreshServiceStatus_Click(object sender, RoutedEventArgs e)
        => await RefreshServiceInfoAsync();

    private async void SaveConfiguration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveAsync();
            AppendLog("Configuration persisted.");
        }
        catch (Exception ex)
        {
            ShowError("Configuration save failed", ex);
        }
    }

    private async void InstallService_Click(object sender, RoutedEventArgs e)
    {
        var binaryPath = GetServiceBinaryPath();
        await RunServiceActionAsync(
            ct => _serviceManager.InstallServiceAsync(ServiceName, binaryPath, autoStart: true, ct),
            "Service installed successfully.",
            "Service installation failed");
    }

    private async void UninstallService_Click(object sender, RoutedEventArgs e)
    {
        await RunServiceActionAsync(
            ct => _serviceManager.UninstallServiceAsync(ServiceName, ct),
            "Service removed successfully.",
            "Service removal failed");
    }

    private async void StartService_Click(object sender, RoutedEventArgs e)
    {
        await RunServiceActionAsync(
            ct => _serviceManager.StartServiceAsync(ServiceName, ct),
            "Service started successfully.",
            "Service start failed");
    }

    private async void StopService_Click(object sender, RoutedEventArgs e)
    {
        await RunServiceActionAsync(
            ct => _serviceManager.StopServiceAsync(ServiceName, ct),
            "Service stopped successfully.",
            "Service stop failed");
    }

    private async void CollectNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = BuildConfig();
            await SaveAsync();

            var installed = await IsServiceInstalledSafeAsync();

            if (installed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(config.Storage.CommandPath) ?? ".");
                await File.WriteAllTextAsync(config.Storage.CommandPath, "{}", CancellationToken.None);
                AppendLog("Collect-now command queued for Windows service.");
                return;
            }

            var snapshot = await _engine.CollectAsync(config, CancellationToken.None);
            await _store.SaveAsync(config.Storage.SnapshotPath, snapshot, CancellationToken.None);
            _lastSnapshot = snapshot;
            RenderFilteredSnapshot();
            AppendLog($"Local collect-now completed (service not installed). Overall health: {snapshot.OverallHealth}");
        }
        catch (Exception ex)
        {
            ShowError("Collect-now failed", ex);
        }
    }

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import configuration",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var imported = await _store.LoadStrictAsync<MonitorConfiguration>(dialog.FileName, CancellationToken.None);
            ApplyConfiguration(imported);
            AppendLog($"Configuration imported from {dialog.FileName}.");
        }
        catch (Exception ex)
        {
            ShowError("Configuration import failed", ex);
        }
    }

    private async void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export configuration",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "config.export.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var config = BuildConfig();
            await _store.SaveAsync(dialog.FileName, config, CancellationToken.None);
            AppendLog($"Configuration exported to {dialog.FileName}.");
        }
        catch (Exception ex)
        {
            ShowError("Configuration export failed", ex);
        }
    }

    private async void ImportSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import snapshot",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _lastSnapshot = await _store.LoadStrictAsync<Snapshot>(dialog.FileName, CancellationToken.None);
            RenderFilteredSnapshot();
            AppendLog($"Snapshot imported from {dialog.FileName}.");
        }
        catch (Exception ex)
        {
            ShowError("Snapshot import failed", ex);
        }
    }

    private void ApplyConfiguration(MonitorConfiguration config)
    {
        ConfigPathBox.Text = config.Storage.ConfigPath;
        SnapshotPathBox.Text = config.Storage.SnapshotPath;
        CommandPathBox.Text = config.Storage.CommandPath;
        UncRootBox.Text = config.Storage.OptionalUncRoot;

        var namespaces = config.Namespaces.Select(ns => $"[{(ns.Enabled ? 'x' : ' ')}] {ns.Path}");
        NamespacesBox.Text = string.Join(Environment.NewLine, namespaces);

        AutoDiscoverDfsrGroupsCheckBox.IsChecked = config.DfsrGroups.Count == 0;
        DfsrGroupsBox.Text = string.Join(Environment.NewLine, config.DfsrGroups);
        DfsrGroupsBox.IsEnabled = AutoDiscoverDfsrGroupsCheckBox.IsChecked == false;
    }

    private static bool IsVisibleByFilter(HealthState state, bool showOk, bool showWarn, bool showCritical, bool showUnknown)
        => state switch
        {
            HealthState.Ok => showOk,
            HealthState.Warn => showWarn,
            HealthState.Critical => showCritical,
            _ => showUnknown
        };

    private void RenderFilteredSnapshot()
    {
        if (_lastSnapshot is null)
        {
            SnapshotViewBox.Text = "No snapshot loaded.";
            return;
        }

        var showOk = ShowOkCheckBox.IsChecked != false;
        var showWarn = ShowWarnCheckBox.IsChecked != false;
        var showCritical = ShowCriticalCheckBox.IsChecked != false;
        var showUnknown = ShowUnknownCheckBox.IsChecked != false;

        var sb = new StringBuilder();
        sb.AppendLine($"CreatedAtUtc: {_lastSnapshot.CreatedAtUtc:O}");
        sb.AppendLine($"OverallHealth: {_lastSnapshot.OverallHealth}");
        sb.AppendLine();

        sb.AppendLine("Namespaces:");
        foreach (var ns in _lastSnapshot.Namespaces.Where(ns => IsVisibleByFilter(ns.Health, showOk, showWarn, showCritical, showUnknown)))
        {
            sb.AppendLine($"- {ns.Path} [{ns.Health}]");
            foreach (var target in ns.Targets)
            {
                var detail = target.Error is null ? "reachable" : $"error={target.Error}";
                sb.AppendLine($"    • {target.UncPath}: {detail}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("DFSR Groups:");
        foreach (var group in _lastSnapshot.DfsrGroups.Where(g => IsVisibleByFilter(g.Health, showOk, showWarn, showCritical, showUnknown)))
        {
            sb.AppendLine($"- {group.GroupName} [{group.Health}]");
            foreach (var backlog in group.Backlogs.Where(b => IsVisibleByFilter(b.Health, showOk, showWarn, showCritical, showUnknown)))
            {
                sb.AppendLine($"    • {backlog.Source} -> {backlog.Destination} | Backlog={backlog.BacklogCount?.ToString() ?? "n/a"} | Health={backlog.Health}");
            }
        }

        SnapshotViewBox.Text = sb.ToString();
    }

    private void SnapshotFilterCheckBox_OnChanged(object sender, RoutedEventArgs e) => RenderFilteredSnapshot();

    private void AutoDiscoverDfsrGroupsCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (DfsrGroupsBox is null)
        {
            return;
        }

        DfsrGroupsBox.IsEnabled = AutoDiscoverDfsrGroupsCheckBox.IsChecked == false;
        if (AutoDiscoverDfsrGroupsCheckBox.IsChecked == true)
        {
            DfsrGroupsBox.Text = string.Empty;
            AppendLog("DFS-R groups set to automatic discovery.");
        }
    }

    private void IncludeDisabledNamespacesCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        var includeDisabled = IncludeDisabledNamespacesCheckBox.IsChecked == true;
        AppendLog(includeDisabled
            ? "Disabled namespaces will be included in local collect operations."
            : "Disabled namespaces are excluded from local collect operations.");
    }

    private async Task<bool> IsServiceInstalledSafeAsync()
    {
        try
        {
            return await _serviceManager.IsServiceInstalledAsync(ServiceName, CancellationToken.None);
        }
        catch
        {
            return false;
        }
    }
}
