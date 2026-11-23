using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OpenTabletDisplay;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Process? _installer;
    private bool _installing;
    private bool _busy;
    private bool _refreshing;
    private bool _scanning;
    private bool _closing;
    private string _currentPage = "Overview";
    private SystemSnapshot? _snapshot;
    private DateTime _lastLogTime = DateTime.MinValue;
    private DateTime _attemptStartUtc = DateTime.MinValue;
    private readonly List<AndroidDevice> _devices = new();

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await TickAsync();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            int dark = 1;
            var handle = new WindowInteropHelper(this).Handle;
            _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        }
        catch (DllNotFoundException) { }
        LoadBrandArt();
        ShowPage("Overview");
        _timer.Start();
        _ = RefreshAsync();
    }

    private void LoadBrandArt()
    {
        foreach (var folder in new[] { Path.Combine(AppContext.BaseDirectory, "assets"),
                     Path.Combine(AppPaths.Root, "assets") })
        {
            var asset = Path.Combine(folder, "OpenTabletDisplay.png");
            if (!File.Exists(asset)) continue;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(asset, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                BrandImage.Source = image;
                HeroImage.Source = image;
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {}
        }
    }

    private void ShowPage(string name)
    {
        _currentPage = name;
        foreach (var (section, panel, nav) in new (string, StackPanel, Button)[] {
            ("Overview", OverviewPage, NavOverview), ("Setup", SetupPage, NavSetup),
            ("Devices", DevicesPage, NavDevices), ("Diagnostics", DiagnosticsPage, NavDiagnostics),
            ("Settings", SettingsPage, NavSettings) })
        {
            panel.Visibility = section == name ? Visibility.Visible : Visibility.Collapsed;
            nav.Background = section == name ? new SolidColorBrush(Color.FromRgb(37, 60, 94))
                                            : Brushes.Transparent;
            nav.Foreground = section == name ? Brushes.White
                                             : new SolidColorBrush(Color.FromRgb(160, 182, 213));
        }
        var labels = name switch {
            "Overview" => ("YOUR WORKSPACE", "Overview", "Your second monitor, without a second monitor."),
            "Setup" => ("GET STARTED", "Set up & connect", "Every step visible. Every permission intentional."),
            "Devices" => ("USB DEVICE WORKSPACE", "My devices", "Authorize, select and reconnect your Android screen."),
            "Diagnostics" => ("SYSTEM INSIGHT", "Diagnostics", "Useful errors and complete local control."),
            _ => ("PREFERENCES", "Controls & help", "Reliable settings, transparent security.")
        };
        PageEyebrow.Text = labels.Item1;
        PageHeading.Text = labels.Item2;
        PageSubheading.Text = labels.Item3;
        MainScroll.ScrollToTop();
        if (name == "Diagnostics") _ = RefreshLogsAsync();
    }

    private void NavOverview_Click(object sender, RoutedEventArgs e) => ShowPage("Overview");
    private void NavSetup_Click(object sender, RoutedEventArgs e) => ShowPage("Setup");
    private void NavDevices_Click(object sender, RoutedEventArgs e) => ShowPage("Devices");
    private void NavDiagnostics_Click(object sender, RoutedEventArgs e) => ShowPage("Diagnostics");
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowPage("Settings");
    private void GoSetup_Click(object sender, RoutedEventArgs e) => ShowPage("Setup");
    private void GoDiagnostics_Click(object sender, RoutedEventArgs e) => ShowPage("Diagnostics");
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void Footer(string message) => FooterText.Text = message;

    private async Task RefreshAsync()
    {
        if (_refreshing || _closing) return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        try
        {
            var snapshot = await Task.Run(SystemInspector.Read);
            if (_closing) return;
            _snapshot = snapshot;
            AppStatusText.Text = snapshot.Installed ? snapshot.RuntimePresent ? "Installed" : "Needs repair"
                : snapshot.RuntimePresent ? "Setup incomplete" : "Not installed";
            AppDetailText.Text = snapshot.Detail;
            SunshineStatusText.Text = snapshot.Sunshine;
            DriverStatusText.Text = snapshot.Driver;
            AndroidStatusText.Text = snapshot.Enrolled ? "Enrolled" : "Not paired";
            HostAddressText.Text = string.IsNullOrEmpty(snapshot.Host) ? "Not yet connected" : snapshot.Host;

            var ready = snapshot.WindowsReady && snapshot.RuntimePresent;
            HeroHeading.Text = _installing ? "Preparing your display."
                : ready ? "Ready for your Android screen." : "Make room for more.";
            HeroDescription.Text = _installing ? "Windows setup is running with explicit administrator approval."
                : ready ? "Connect your authorized Android device and open Desktop in Moonlight."
                : snapshot.Detail;
            SidebarStateText.Text = _installing ? "Installation in progress"
                : ready ? "Windows components ready" : snapshot.Installed ? "Needs attention" : "Setup required";
            SidebarStateDot.Fill = ready && !_installing ? new SolidColorBrush(Color.FromRgb(69, 208, 154))
                : new SolidColorBrush(Color.FromRgb(243, 185, 100));
            UpdateActions();

            if (!_installing && !_busy)
                Footer("Sunshine: " + snapshot.Sunshine + "  •  Virtual display: " + snapshot.Driver);
        }
        catch (Exception ex)
        {
            Footer("Unable to inspect Windows component status: " + ex.Message);
        }
        finally
        {
            _refreshing = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void UpdateActions()
    {
        var s = _snapshot;
        bool baseReady = s is not null && SystemInspector.ReadyForUsb(s, _installing || _busy);
        InstallButton.IsEnabled = !_installing && !_busy && NativePackage.Discover() is not null;
        CheckUsbButton.IsEnabled = baseReady && !_scanning;
        ScanDeviceButton.IsEnabled = baseReady && !_scanning;
        EnrollButton.IsEnabled = baseReady && !_scanning && DeviceList.SelectedItem is AndroidDevice d && d.State == "device";
        StartUsbButton.IsEnabled = baseReady;
        StopUsbButton.IsEnabled = !_installing && !_busy && s is { Installed: true, RuntimePresent: true };
        DoctorButton.IsEnabled = !_installing && !_busy && s is { RuntimePresent: true };
        UninstallButton.IsEnabled = !_installing && !_busy && File.Exists(AppPaths.InstallState);
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installing || _busy) return;
        if (NativePackage.Discover() is null)
        {
            Alert("The extracted release is incomplete. Download the full Windows ZIP, not GitHub's Source code ZIP.");
            return;
        }
        try
        {
            // Remember the attempt time, so an old RC2 failure is never
            // presented as the outcome of a blocked UAC attempt.
            _attemptStartUtc = DateTime.UtcNow;
            _installer = SafeCommands.StartElevatedAction("--native-install");
            _installing = true;
            _lastLogTime = _attemptStartUtc;
            InstallProgress.Visibility = Visibility.Visible;
            InstallerWarning.Visibility = Visibility.Collapsed;
            InstallDetailText.Text = "Installer started. Watching the actual Windows installation log…";
            Footer("Installing verified dependencies. Windows and Android security prompts remain in your control.");
            UpdateActions();
            ShowPage("Setup");
            await _installer.WaitForExitAsync();
            int exit = _installer.ExitCode;
            _installer.Dispose();
            _installer = null;
            _installing = false;
            InstallProgress.Visibility = Visibility.Collapsed;
            await RefreshAsync();
            await RefreshLogsAsync();
            string lastError = GetRelevantInstallFailure(_attemptStartUtc, exit);
            if (exit == 0 && _snapshot is { Installed: true, RuntimePresent: true })
            {
                InstallDetailText.Text = "Setup completed. Connect Android and authorize USB debugging.";
                Footer("Windows setup completed.");
                Alert("Setup completed. Connect the Android device, approve USB debugging and pair Moonlight.",
                    MessageBoxImage.Information);
            }
            else
            {
                InstallDetailText.Text = "Setup failed. " + lastError;
                Footer("Setup failed. Open Diagnostics for the exact error and dependency logs.");
                Alert("Installation did not complete. " + lastError +
                      "\n\nReview Diagnostics and antivirus protection history. Do not disable your security software.",
                      MessageBoxImage.Warning);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            InstallDetailText.Text = "Windows administrator approval was cancelled.";
            Footer("Installer cancelled. No new setup operation was started.");
        }
        catch (Exception ex)
        {
            _installing = false;
            InstallProgress.Visibility = Visibility.Collapsed;
            Footer("Installer could not start: " + ex.Message);
            Alert("Windows could not start the installer: " + ex.Message);
        }
        finally
        {
            _installing = false;
            UpdateActions();
        }
    }

    private string GetRelevantInstallFailure(DateTime attemptStartUtc, int exitCode)
    {
        if (exitCode == ElevatedBootstrap.IdentityMismatch)
            return "UAC approved a different Windows account. No setup changes were made.";
        try
        {
            // An older setup attempt may never have reached install.log. The separate
            // bootstrap log contains the actual PowerShell error from THIS run.
            if (File.Exists(ElevatedBootstrap.LogPath) &&
                File.GetLastWriteTimeUtc(ElevatedBootstrap.LogPath) >= attemptStartUtc.AddSeconds(-2))
            {
                string raw = File.ReadAllText(ElevatedBootstrap.LogPath);
                if (raw.Contains("running scripts is disabled", StringComparison.OrdinalIgnoreCase) ||
                    raw.Contains("cannot be loaded because running scripts", StringComparison.OrdinalIgnoreCase))
                    return "Windows blocked unsigned PowerShell scripts under its current execution policy. " +
                        "The development installer cannot proceed without a trusted signed or native setup. " +
                        "We will not ask you to lower Windows security. See Diagnostics for details.";
                if (raw.Contains("not digitally signed", StringComparison.OrdinalIgnoreCase))
                    return "The unsigned installer script was blocked by PowerShell signature policy. " +
                        "See Diagnostics; do not bypass script policy.";
                string excerpt = raw.Contains("[stderr]", StringComparison.Ordinal)
                    ? raw.Split("[stderr]", StringSplitOptions.None).Last().Trim()
                    : raw.Split('\n').LastOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";
                if (!string.IsNullOrEmpty(excerpt))
                    return excerpt.Length <= 550 ? excerpt : excerpt[^550..];
            }
            if (!File.Exists(AppPaths.InstallLog) ||
                File.GetLastWriteTimeUtc(AppPaths.InstallLog) < attemptStartUtc.AddSeconds(-2))
                return "Elevated setup stopped before creating its installation log. Open Diagnostics to see the startup log.";
            var log = File.ReadLines(AppPaths.InstallLog).Reverse().FirstOrDefault(
                line => line.Contains("INSTALL FAILED", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(log)) return log;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {}
        return "See installation and dependency logs for the cause.";
    }

    private async Task TickAsync()
    {
        if (_closing) return;
        try
        {
            if (_installing)
            {
                var detail = await Task.Run(() => {
                    try {
                        if (!File.Exists(AppPaths.InstallLog)) return ("Installer is preparing to write its log.", DateTime.MinValue);
                        var modified = File.GetLastWriteTimeUtc(AppPaths.InstallLog);
                        var last = File.ReadLines(AppPaths.InstallLog).LastOrDefault() ?? "No progress entry yet.";
                        return (last, modified);
                    } catch (IOException) { return ("The installer log is temporarily in use.", DateTime.MinValue); }
                });
                if (detail.Item2 >= _attemptStartUtc.AddSeconds(-2)) _lastLogTime = detail.Item2;
                InstallDetailText.Text = detail.Item1;
                if (DateTime.UtcNow - _lastLogTime > TimeSpan.FromSeconds(100))
                {
                    InstallerWarning.Visibility = Visibility.Visible;
                    InstallerWarning.Text = "No progress logged recently. Setup might be waiting for a system operation or security review.";
                }
                else InstallerWarning.Visibility = Visibility.Collapsed;
            }
            else if (!_refreshing)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex) { Footer("Status update failed: " + ex.Message); }
    }

    private async void CheckUsbButton_Click(object sender, RoutedEventArgs e) => await ScanDevicesAsync();
    private async Task ScanDevicesAsync()
    {
        if (_scanning || _installing || _busy || _snapshot is null ||
            !SystemInspector.ReadyForUsb(_snapshot, _installing || _busy))
        {
            Footer("Finish Windows installation before checking the USB transport.");
            return;
        }
        _scanning = true;
        UsbSetupStatus.Text = "Checking USB authorization…";
        DeviceScanStatus.Text = "Scanning…";
        UpdateActions();
        try
        {
            var result = await SafeCommands.ExecuteAsync(AppPaths.AdbExe,
                new[] { "devices", "-l" }, TimeSpan.FromSeconds(15));
            if (!result.Success) throw new InvalidOperationException(result.Summary);
            _devices.Clear();
            foreach (string line in result.StandardOutput.Split('\n'))
            {
                var match = Regex.Match(line.Trim(), @"\A([A-Za-z0-9._:-]{1,128})\s+(device|unauthorized|offline)\b");
                if (!match.Success) continue;
                var serial = match.Groups[1].Value;
                var state = match.Groups[2].Value;
                _devices.Add(new AndroidDevice(serial, state));
            }
            DeviceList.ItemsSource = null;
            DeviceList.ItemsSource = _devices;
            if (_devices.Count == 1) DeviceList.SelectedIndex = 0;
            string explanation = _devices.Count == 0 ? "No Android device found. Check the cable and USB debugging."
                : _devices.Any(d => d.State == "unauthorized") ? "Unlock Android and approve the USB debugging prompt."
                : _devices.All(d => d.State == "offline") ? "ADB sees the device offline. Reconnect USB."
                : _devices.Count > 1 ? "Choose which authorized Android device to use."
                : "Authorized Android device detected.";
            UsbSetupStatus.Text = explanation;
            DeviceScanStatus.Text = explanation;
            Footer(explanation);
        }
        catch (Exception ex)
        {
            UsbSetupStatus.Text = "USB check failed";
            DeviceScanStatus.Text = "USB check failed";
            Footer(ex.Message);
            Alert("USB device check failed:\n" + ex.Message);
        }
        finally { _scanning = false; UpdateActions(); }
    }

    private async void EnrollButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not AndroidDevice selected || selected.State != "device" ||
            !SafeCommands.IsValidAndroidSerial(selected.Serial)) return;
        if (MessageBox.Show(this, "Use the Android device " + selected.Serial +
                " for your extended display? Existing pairing is not changed without confirmation.",
                "Enroll Android device", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await RunTransportCommandAsync("enroll", selected.Serial);
    }

    private async void StartUsbButton_Click(object sender, RoutedEventArgs e) => await RunTransportCommandAsync("start");
    private async void StopUsbButton_Click(object sender, RoutedEventArgs e) => await RunTransportCommandAsync("stop");

    private async Task RunTransportCommandAsync(string command, string? serial = null)
    {
        if (_busy || _installing || _snapshot is not { Installed: true, RuntimePresent: true })
        {
            Alert("Windows setup is still running or incomplete. Finish setup before using the USB transport.");
            return;
        }
        if (command != "stop" && !SystemInspector.ReadyForUsb(_snapshot, _installing || _busy))
        {
            Alert("Sunshine, USB tools or the virtual-display driver is not ready. Run diagnostics before starting a connection.");
            return;
        }
        _busy = true;
        UpdateActions();
        Footer("Running USB " + command + "…");
        try
        {
            var args = serial is null ? new[] { command } : new[] { command, serial };
            var result = await SafeCommands.ExecuteAsync(AppPaths.RuntimeExe, args,
                command == "start" ? TimeSpan.FromMinutes(4) : TimeSpan.FromSeconds(45));
            await RefreshAsync();
            if (!result.Success)
            {
                string detail = result.Summary;
                if (detail.Length > 1500) detail = detail[^1500..];
                Alert("USB " + command + " failed (exit " + result.ExitCode + ").\n\n" + detail);
                Footer("USB " + command + " failed. See Diagnostics for detailed logs.");
            }
            else
            {
                Footer("USB " + command + " completed.");
                if (command == "enroll") Alert("Device enrollment completed.", MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Alert("USB " + command + " could not finish:\n" + ex.Message);
            Footer("USB " + command + " failed. No security protections were modified.");
        }
        finally
        {
            _busy = false;
            UpdateActions();
        }
    }

    private async void DoctorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _installing || !File.Exists(AppPaths.RuntimeExe)) return;
        _busy = true;
        UpdateActions();
        Footer("Running local connection diagnostics…");
        try
        {
            var result = await SafeCommands.ExecuteAsync(AppPaths.RuntimeExe,
                new[] { "doctor" }, TimeSpan.FromSeconds(35));
            var logs = await Task.Run(DiagnosticReader.BuildReport);
            DiagnosticsOutput.Text = logs + "\n\n[Live connection doctor]\n" +
                DiagnosticReader.Sanitize(result.StandardOutput + "\n" + result.StandardError);
            Footer(result.Success ? "Connection doctor completed." :
                   "Connection doctor reported an error. See diagnostic output.");
        }
        catch (Exception ex)
        {
            DiagnosticsOutput.Text += "\n\n[Connection doctor error]\n" + ex.Message;
            Footer("Connection doctor could not complete.");
        }
        finally { _busy = false; UpdateActions(); }
    }

    private async Task RefreshLogsAsync()
    {
        try { DiagnosticsOutput.Text = await Task.Run(DiagnosticReader.BuildReport); }
        catch (Exception ex) { DiagnosticsOutput.Text = "Unable to load diagnostic logs:\n" + ex.Message; }
    }
    private async void RefreshLogsButton_Click(object sender, RoutedEventArgs e) => await RefreshLogsAsync();

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog {
            Filter = "Text report|*.txt", FileName = "OpenTabletDisplay-diagnostics.txt",
            Title = "Save sanitized diagnostic report"
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            DiagnosticReader.Export(picker.FileName, DiagnosticsOutput.Text);
            Alert("Sanitized report saved. Review its contents before sharing.", MessageBoxImage.Information);
        }
        catch (Exception ex) { Alert("Could not export the diagnostic report:\n" + ex.Message); }
    }

    private void LogsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(AppPaths.LogsDirectory))
        {
            Alert("The logs folder does not exist yet. Try installation or diagnostics first.");
            return;
        }
        try { Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.LogsDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { Alert("Unable to open logs folder:\n" + ex.Message); }
    }

    private void CopyHostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not { Host.Length: > 0 } || !IPAddress.TryParse(_snapshot.Host, out _))
        {
            Alert("The USB host address is not available yet. Connect Android and start the bridge.");
            return;
        }
        Clipboard.SetText(_snapshot.Host);
        Footer("Moonlight host address copied to clipboard.");
    }

    private void MoonlightButton_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://play.google.com/store/apps/details?id=com.limelight");
    private void SunshineButton_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://localhost:47990");
    private void HelpButton_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/0pen-Sourcer/OpenTabletDisplay/blob/main/docs/TROUBLESHOOTING.md");
    private void ReportButton_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/0pen-Sourcer/OpenTabletDisplay/issues/new/choose");

    private void OpenUrl(string url)
    {
        try { SafeCommands.OpenTrustedUrl(url); }
        catch (Exception ex) { Alert("Unable to open the trusted website:\n" + ex.Message); }
    }

    private void AppFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(AppPaths.Root))
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true }); }
            catch (Exception ex) { Alert("Cannot open app folder: " + ex.Message); }
        }
        else Alert("The app has not yet been installed.");
    }

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _installing) return;
        if (!File.Exists(AppPaths.InstallState)) { Alert("No installed integration was found."); return; }
        if (MessageBox.Show(this,
                "This will remove OpenTabletDisplay and restore only app-owned Sunshine settings. Existing Sunshine and virtual-display drivers will remain. After UAC approval the window will close so Windows can remove the application's files.\n\nContinue?",
                "Uninstall OpenTabletDisplay", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;
        try
        {
            // The GUI must exit before deleting its own executable. Do not run
            // hidden cmd.exe cleanup or falsely report success before setup exits.
            using var process = SafeCommands.StartElevatedAction("--native-remove");
            _timer.Stop();
            _closing = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Alert("Could not start uninstall: " + ex.Message +
                  "\nNo security restrictions were changed.");
        }
    }

    private void Alert(string text, MessageBoxImage icon = MessageBoxImage.Warning) =>
        MessageBox.Show(this, text, "OpenTabletDisplay", MessageBoxButton.OK, icon);

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_installing)
        {
            var choice = MessageBox.Show(this,
                "Windows setup is still running. Closing the desktop window will NOT stop the elevated installer. Keep the control center open to watch setup?",
                "Installation in progress", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes) { e.Cancel = true; return; }
        }
        _closing = true;
        _timer.Stop();
    }

    private sealed record AndroidDevice(string Serial, string State)
    {
        public string Display => Serial + "  ·  " + State;
        public override string ToString() => Display;
    }
}
