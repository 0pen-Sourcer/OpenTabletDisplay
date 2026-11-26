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
    private bool _sunshineBusy;
    private bool _displayBusy;
    private string _displayMode = "Unavailable";
    private string? _usbCurrentOperation;
    private DateTime _usbOperationStartedUtc;
    private string _sunshineStartupMode = "Unavailable";
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
            "Setup" => ("GET STARTED", "Set up & connect", "Install the display components and connect your tablet."),
            "Devices" => ("ANDROID DEVICES", "My devices", "Select an authorized Android device for the USB connection."),
            "Diagnostics" => ("SYSTEM INSIGHT", "Diagnostics", "Useful errors and complete local control."),
            _ => ("PREFERENCES", "Controls & help", "Manage streaming and your installation.")
        };
        PageEyebrow.Text = labels.Item1;
        PageHeading.Text = labels.Item2;
        PageSubheading.Text = labels.Item3;
        MainScroll.ScrollToTop();
        if (name == "Diagnostics") _ = RefreshLogsAsync();
    }

    public void NavigateToUninstall()
    {
        ShowPage("Settings");
        UninstallButton.Focus();
    }

    private void NavOverview_Click(object sender, RoutedEventArgs e) => ShowPage("Overview");
    private void NavSetup_Click(object sender, RoutedEventArgs e) => ShowPage("Setup");
    private void NavDevices_Click(object sender, RoutedEventArgs e) => ShowPage("Devices");
    private void NavDiagnostics_Click(object sender, RoutedEventArgs e) => ShowPage("Diagnostics");
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowPage("Settings");
    private void GoSetup_Click(object sender, RoutedEventArgs e) => ShowPage("Setup");
    private void HeroPrimary_Click(object sender, RoutedEventArgs e) =>
        ShowPage(_snapshot is { Installed: true, RuntimePresent: true } ? "Devices" : "Setup");
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
            var statusTask = Task.Run(SystemInspector.Read);
            var modeTask = Task.Run(SunshineServiceControl.ReadStartupMode);
            var displayTask = Task.Run(SunshineDisplayPreference.ReadMode);
            await Task.WhenAll(statusTask,modeTask,displayTask);
            var snapshot = await statusTask;
            _sunshineStartupMode = await modeTask;
            _displayMode = await displayTask;
            if (_closing) return;
            _snapshot = snapshot;
            AppStatusText.Text = snapshot.Installed ? snapshot.RuntimePresent ? "Installed" : "Needs repair"
                : snapshot.RuntimePresent ? "Setup incomplete" : "Not installed";
            AppDetailText.Text = snapshot.Detail;
            SunshineStatusText.Text = snapshot.Sunshine;
            SunshineReachabilityText.Text = snapshot.Sunshine switch
            {
                "Running" when snapshot.SunshineListening => "Sunshine's local streaming endpoint is responding",
                "Running" => "Service running · waiting for the streaming endpoint",
                "Service stopped" => "Streaming is off · select Start when needed",
                _ => "Sunshine is unavailable · see Diagnostics"
            };
            SunshineServiceDetail.Text = "Service: " + snapshot.Sunshine +
                (snapshot.Sunshine == "Running" ? snapshot.SunshineListening
                    ? " · Endpoint responding" : " · Endpoint not responding" : "");
            SunshineStartupText.Text = "Current startup: " + _sunshineStartupMode;
            string modeLabel = _displayMode switch
            {
                "ensure_active" => "PC monitor stays primary (recommended)",
                "ensure_primary" => "Tablet becomes primary during streaming",
                "Unmanaged" => "Sunshine setting changed outside OpenTabletDisplay",
                _ => "Display preference unavailable"
            };
            DisplayModeStatusText.Text = "Current choice: " + modeLabel;
            OnboardDisplayModeText.Text = "Current choice: " + modeLabel;
            DriverStatusText.Text = snapshot.Driver;
            AndroidStatusText.Text = snapshot.Enrolled ? "USB device enrolled" : "No USB device enrolled";
            HostAddressText.Text = string.IsNullOrEmpty(snapshot.Host) ? "Not yet connected" : snapshot.Host;

            bool installed = snapshot.Installed && snapshot.RuntimePresent;
            bool windowsReady = installed && snapshot.Driver == "Ready" &&
                snapshot.Sunshine == "Running" && snapshot.SunshineListening;
            HeroPrimaryButton.Content = installed ? "Manage tablet connection" : "Set up my display";
            SetupStepHeading.Text = installed ? "Windows setup completed" : "Install OpenTabletDisplay";
            SetupStepDescription.Text = installed
                ? "Sunshine and the virtual-display driver are installed. Do not reinstall to reconnect a tablet."
                : "Install and configure Sunshine and the display driver once. Windows will ask for administrator approval.";
            InstallButton.Content = installed ? "Repair Windows installation" : "Install";
            InstallDetailText.Text = installed && !_installing
                ? "Installation is complete. Use My devices to connect your tablet."
                : InstallDetailText.Text;
            HeroHeading.Text = _installing ? "Preparing your display."
                : !installed ? "Make room for more."
                : snapshot.Sunshine == "Service stopped" ? "Sunshine is switched off."
                : windowsReady ? "Ready for your Android screen."
                : "Finishing Sunshine startup.";
            HeroDescription.Text = _installing ? "Installing Windows components."
                : !installed ? snapshot.Detail
                : snapshot.Sunshine == "Service stopped" ? "Select Start Sunshine to resume streaming; no reinstall is necessary."
                : !windowsReady ? snapshot.Detail
                : "Open My devices to connect your tablet. Keep your Moonlight pairing.";
            SidebarStateText.Text = _installing ? "Installation in progress"
                : windowsReady ? "Windows ready" : installed ? "Streaming needs attention" : "Setup required";
            SidebarStateDot.Fill = windowsReady && !_installing
                ? new SolidColorBrush(Color.FromRgb(69, 208, 154))
                : new SolidColorBrush(Color.FromRgb(243, 185, 100));
            UpdateActions();

            if (!_installing && !_busy && !_sunshineBusy && !_displayBusy)
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
        bool blocked = _installing || _busy || _sunshineBusy || _displayBusy;
        bool baseReady = s is not null && SystemInspector.ReadyForUsb(s, blocked);
        bool serviceReady = !blocked && s is { Installed: true } &&
            (s.Sunshine is "Running" or "Service stopped");
        bool running = serviceReady && s!.Sunshine == "Running";
        bool stopped = serviceReady && s!.Sunshine == "Service stopped";

        SunshineStartButton.IsEnabled = stopped;
        SunshineStopButton.IsEnabled = running;
        SunshineSettingsStartButton.IsEnabled = stopped;
        SunshineSettingsStopButton.IsEnabled = running;
        SunshineAutomaticButton.IsEnabled = serviceReady && _sunshineStartupMode != "Automatic";
        SunshineManualButton.IsEnabled = serviceReady && _sunshineStartupMode != "Manual";
        bool displayConfigReady = !blocked && s is { Installed: true } &&
            (_displayMode is "ensure_active" or "ensure_primary");
        DisplayKeepPcButton.IsEnabled = displayConfigReady && _displayMode != "ensure_active";
        DisplayMakeTabletButton.IsEnabled = displayConfigReady && _displayMode != "ensure_primary";
        OnboardKeepPcButton.IsEnabled = DisplayKeepPcButton.IsEnabled;
        OnboardMakeTabletButton.IsEnabled = DisplayMakeTabletButton.IsEnabled;

        InstallButton.IsEnabled = !blocked && NativePackage.Discover() is not null;
        CheckUsbButton.IsEnabled = baseReady && !_scanning;
        ScanDeviceButton.IsEnabled = baseReady && !_scanning;
        EnrollButton.IsEnabled = baseReady && !_scanning &&
            DeviceList.SelectedItem is AndroidDevice d && d.State == "device";
        StartUsbButton.IsEnabled = baseReady;
        StopUsbButton.IsEnabled = !blocked && s is { Installed: true, RuntimePresent: true };
        DoctorButton.IsEnabled = !blocked && s is { RuntimePresent: true };
        UninstallButton.IsEnabled = !blocked && File.Exists(AppPaths.InstallState);
    }

    private void OpenWindowsDisplaySettings_Click(object sender, RoutedEventArgs e) =>
        OpenWindowsSettings("ms-settings:display", "System → Display");

    private void OpenInstalledApps_Click(object sender, RoutedEventArgs e) =>
        OpenWindowsSettings("ms-settings:appsfeatures", "Apps → Installed apps");

    private void OpenWindowsSettings(string uri,string manualPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute=true });
        }
        catch(Exception ex)
        {
            Alert("Windows Settings could not open: "+ex.Message+
                "\n\nPress Win + I, then open "+manualPath+".");
        }
    }

    private async void KeepPcPrimary_Click(object sender, RoutedEventArgs e) =>
        await RunDisplayPreferenceAsync(SunshineDisplayPreference.KeepPcPrimary);

    private async void MakeTabletPrimary_Click(object sender, RoutedEventArgs e) =>
        await RunDisplayPreferenceAsync(SunshineDisplayPreference.MakeTabletPrimary);

    private async Task RunDisplayPreferenceAsync(string action)
    {
        if(_installing || _busy || _sunshineBusy || _displayBusy ||
           !SunshineDisplayPreference.IsAllowed(action)) return;
        if(_displayMode is not ("ensure_active" or "ensure_primary"))
        {
            Alert("Sunshine's display setting is unavailable or was changed outside OpenTabletDisplay. Nothing was modified.");
            return;
        }
        string desired=SunshineDisplayPreference.ModeForAction(action);
        if(desired==_displayMode) return;
        string description=desired=="ensure_active"
            ? "Keep your physical PC monitor primary while the tablet extends your desktop."
            : "Make the tablet Windows' primary display during streaming. Applications may change position.";
        if(MessageBox.Show(this,
                description+"\n\nSunshine may briefly restart and disconnect Moonlight. Your pairing and drivers remain installed.\n\nApply?",
                "Change main display behavior",MessageBoxButton.YesNo,MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        _displayBusy=true;
        UpdateActions();
        Footer("Updating Sunshine display preference…");
        DateTime startedUtc=DateTime.UtcNow;
        try
        {
            using var process=SafeCommands.StartElevatedAction(action);
            await process.WaitForExitAsync();
            if(process.ExitCode!=0)
            {
                string reason=process.ExitCode==SunshineDisplayPreference.IdentityMismatch
                    ? "Administrator approval used a different account. No change was made."
                    : "Windows couldn't update the display preference.";
                if(process.ExitCode!=SunshineDisplayPreference.IdentityMismatch)
                    try
                    {
                        if(File.Exists(SunshineDisplayPreference.ErrorFile) &&
                            File.GetLastWriteTimeUtc(SunshineDisplayPreference.ErrorFile)>=startedUtc.AddSeconds(-2))
                        {
                            string logged=File.ReadAllText(SunshineDisplayPreference.ErrorFile);
                            if(!string.IsNullOrWhiteSpace(logged))reason=logged;
                        }
                    }
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
                throw new InvalidOperationException(reason);
            }
            Footer(desired=="ensure_active"
                ? "PC monitor will stay primary while Sunshine streams."
                : "Tablet will be primary during Sunshine streaming.");
        }
        catch(Win32Exception ex) when(ex.NativeErrorCode==1223)
        {
            Footer("Display change cancelled. Previous settings remain.");
        }
        catch(Exception ex)
        {
            Footer("Display preference update failed. See Diagnostics.");
            Alert("Unable to update display preference:\n"+ex.Message+
                "\n\nDo not reinstall Sunshine to fix this.");
        }
        finally
        {
            _displayBusy=false;
            await RefreshAsync();
            UpdateActions();
        }
    }

    private async void StartSunshine_Click(object sender, RoutedEventArgs e) =>
        await RunSunshineActionAsync(SunshineServiceControl.Start);

    private async void StopSunshine_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _installing || _sunshineBusy || _displayBusy) return;
        if (MessageBox.Show(this,
                "Stop Sunshine? This disconnects the current Moonlight stream. " +
                "Your tablet pairing, USB setup and virtual-display driver stay installed.",
                "Stop Sunshine", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        await RunSunshineActionAsync(SunshineServiceControl.Stop);
    }

    private async void SunshineAutomatic_Click(object sender, RoutedEventArgs e) =>
        await RunSunshineActionAsync(SunshineServiceControl.Automatic);

    private async void SunshineManual_Click(object sender, RoutedEventArgs e) =>
        await RunSunshineActionAsync(SunshineServiceControl.Manual);

    private async Task RunSunshineActionAsync(string action)
    {
        if (_installing || _busy || _sunshineBusy || _displayBusy ||
            !SunshineServiceControl.IsAllowed(action)) return;
        if (_snapshot is not { Installed: true } ||
            _snapshot.Sunshine is not ("Running" or "Service stopped"))
        {
            Alert("Sunshine's Windows service isn't available. Open Diagnostics for details.");
            return;
        }

        _sunshineBusy = true;
        UpdateActions();
        Footer("Applying Sunshine service setting…");
        DateTime attempt = DateTime.UtcNow;
        try
        {
            try
            {
                // Try the service's existing Windows permissions first.
                // If admin approval is required, use an allowlisted native UAC action.
                await SunshineServiceControl.ChangeAsync(action);
            }
            catch (Exception ex) when (SunshineServiceControl.IsPermissionDenied(ex))
            {
                using var elevated = SafeCommands.StartElevatedAction(action);
                await elevated.WaitForExitAsync();
                if (elevated.ExitCode != 0)
                {
                    string reason = elevated.ExitCode == SunshineServiceControl.IdentityMismatch
                        ? "Windows approved a different account or denied administrator access. No service change was made."
                        : "Windows could not change the Sunshine service.";
                    try
                    {
                        if (elevated.ExitCode != SunshineServiceControl.IdentityMismatch &&
                            File.Exists(SunshineServiceControl.LogPath) &&
                            File.GetLastWriteTimeUtc(SunshineServiceControl.LogPath) >= attempt.AddSeconds(-2))
                        {
                            string? last = File.ReadLines(SunshineServiceControl.LogPath).LastOrDefault();
                            if (!string.IsNullOrWhiteSpace(last)) reason = last;
                        }
                    }
                    catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
                    throw new InvalidOperationException(reason);
                }
            }

            Footer(action switch {
                SunshineServiceControl.Start => "Sunshine started. Moonlight can connect.",
                SunshineServiceControl.Stop => "Sunshine stopped. Streaming is off.",
                SunshineServiceControl.Automatic => "Sunshine will start with Windows.",
                _ => "Sunshine will start manually."
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Footer("Sunshine change cancelled. Existing service settings were kept.");
        }
        catch (Exception ex)
        {
            Footer("Sunshine operation failed. Existing installation was kept.");
            Alert("Unable to change Sunshine: " + ex.Message +
                "\n\nOpen Diagnostics for details. You don't need to reinstall Sunshine.");
        }
        finally
        {
            _sunshineBusy = false;
            await RefreshAsync();
            UpdateActions();
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installing || _busy || _sunshineBusy || _displayBusy) return;
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
        if (exitCode == NativeSetup.IdentityMismatch)
            return "Administrator approval used a different Windows account or did not grant an administrator token. No setup changes were made.";
        try
        {
            if (!File.Exists(AppPaths.InstallLog) ||
                File.GetLastWriteTimeUtc(AppPaths.InstallLog) < attemptStartUtc.AddSeconds(-2))
                return "Native installation stopped before writing its log. Review Windows security or the extracted package.";
            var lines = File.ReadLines(AppPaths.InstallLog).Reverse();
            var error = lines.FirstOrDefault(line =>
                line.Contains("INSTALL FAILED", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(error)) return error;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {}
        return "Native setup exited with code " + exitCode + ". See Diagnostics for details.";
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
            else if (_usbCurrentOperation is string operation)
            {
                var elapsed = DateTime.UtcNow - _usbOperationStartedUtc;
                UsbOperationStatus.Text = "Working on USB " + operation + " · " +
                    elapsed.ToString(@"m\:ss") +
                    " elapsed. Android may ask for permission; do not unplug the cable.";
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
        if (_scanning || _installing || _busy || _sunshineBusy || _displayBusy || _snapshot is null ||
            !SystemInspector.ReadyForUsb(_snapshot, _installing || _busy || _sunshineBusy || _displayBusy))
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
        if (_busy || _installing || _sunshineBusy || _displayBusy || _snapshot is not { Installed: true, RuntimePresent: true })
        {
            Alert("Windows setup is still running or incomplete. Finish setup before using the USB transport.");
            return;
        }
        if (command != "stop" && !SystemInspector.ReadyForUsb(_snapshot, _installing || _busy || _sunshineBusy || _displayBusy))
        {
            Alert("Sunshine, USB tools or the virtual-display driver is not ready. Run diagnostics before starting a connection.");
            return;
        }
        _busy = true;
        _usbCurrentOperation = command;
        _usbOperationStartedUtc = DateTime.UtcNow;
        UsbOperationStatus.Text = "Starting USB " + command + "…";
        UsbOperationProgress.Visibility = Visibility.Visible;
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
                UsbOperationStatus.Text = "USB " + command + " failed. Check Diagnostics.";
                Footer("USB " + command + " failed. See Diagnostics for detailed logs.");
            }
            else
            {
                UsbOperationStatus.Text = command switch {
                    "start" => "USB connection ready. Open Moonlight on the tablet.",
                    "stop" => "USB connection stopped. The Windows installation is kept.",
                    _ => "Tablet authorization saved."
                };
                Footer("USB " + command + " completed.");
                if (command == "enroll") Alert("Device enrollment completed.", MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Alert("USB " + command + " could not finish:\n" + ex.Message);
            UsbOperationStatus.Text = "USB " + command + " could not finish. Check Diagnostics.";
            Footer("USB " + command + " failed. No security protections were modified.");
        }
        finally
        {
            _usbCurrentOperation = null;
            _busy = false;
            UsbOperationProgress.Visibility = Visibility.Collapsed;
            UpdateActions();
        }
    }

    private async void DoctorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _installing || _sunshineBusy || _displayBusy || !File.Exists(AppPaths.RuntimeExe)) return;
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

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _installing || _sunshineBusy || _displayBusy) return;
        if (!File.Exists(AppPaths.InstallState))
        {
            Alert("No installed OpenTabletDisplay integration was found.");
            return;
        }
        _busy = true;
        UpdateActions();
        RemovalCapabilities available;
        try { available = await Task.Run(RemovalSelection.Inspect); }
        catch (Exception ex)
        {
            Alert("Unable to inspect installed components: " + ex.Message);
            _busy = false;
            UpdateActions();
            return;
        }
        _busy = false;
        UpdateActions();

        var picker = new UninstallSelectionWindow(available) { Owner = this };
        if (picker.ShowDialog() != true) return;
        bool sunshine = picker.RemoveSunshine;
        bool driver = picker.RemoveDriver;
        if (!RemovalSelection.Validate(sunshine, driver, out var reason))
        {
            Alert(reason + "\nNo components have been removed.");
            return;
        }
        string selected = "• OpenTabletDisplay app, USB agent, local files and app-owned settings";
        if (sunshine) selected += "\n• Owned Sunshine installation (Moonlight streaming will stop)";
        if (driver) selected += "\n• Owned virtual display device (active Moonlight streams will end; driver package may remain)";
        string kept = (sunshine ? "" : "\nSunshine will remain installed.") +
                      (driver ? "" : "\nThe virtual-display driver will remain installed.");
        if (MessageBox.Show(this,
                "Remove the following components?\n\n" + selected + "\n" + kept +
                "\n\nAdministrator approval is required. This can't be undone automatically.",
                "Confirm component removal", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var flags = new List<string>();
        if (sunshine) flags.Add(RemovalSelection.SunshineFlag);
        if (driver) flags.Add(RemovalSelection.DriverFlag);
        try
        {
            using var process = SafeCommands.StartElevatedAction("--native-remove", flags);
            // The installed GUI must close to allow its own EXE to be removed.
            _timer.Stop();
            _closing = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Alert("Could not start removal: " + ex.Message +
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
