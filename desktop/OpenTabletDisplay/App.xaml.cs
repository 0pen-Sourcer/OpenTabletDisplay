using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace OpenTabletDisplay;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnApplicationException;
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--native-install")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await NativeSetup.InstallAsync(e.Args[1]));
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--native-remove")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!RemovalSelection.TryParse(e.Args.Skip(2).ToArray(),
                out bool removeSunshine, out bool removeDriver))
            {
                Shutdown(76);
                return;
            }
            int result = await NativeRemove.RunAsync(e.Args[1], removeSunshine, removeDriver);
            MessageBox.Show(
                result == 0
                    ? "OpenTabletDisplay removal completed. A Windows restart may finish deleting files that were open."
                    : "Removal could not be completed. Some selected changes might already have taken effect. Review the details at:\n" +
                      (NativeRemove.LastLogPath ?? "No uninstall log was created."),
                "OpenTabletDisplay", MessageBoxButton.OK,
                result == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Shutdown(result);
            return;
        }
        if (e.Args.Length == 2 && SunshineDisplayPreference.IsAllowed(e.Args[0]))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int result = await SunshineDisplayPreference.RunAsync(e.Args[0],e.Args[1]);
            Shutdown(result);
            return;
        }
        if (e.Args.Length == 2 && SunshineServiceControl.IsAllowed(e.Args[0]))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int result = await SunshineServiceControl.RunElevatedAsync(e.Args[0], e.Args[1]);
            Shutdown(result);
            return;
        }
        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!SafeCommands.IsValidAndroidSerial("R58N4_test-123") ||
                    SafeCommands.IsValidAndroidSerial("serial & powershell.exe") ||
                    SafeCommands.IsValidAndroidSerial("../danger"))
                    throw new InvalidOperationException("Device serial validation regression.");
                var sanitized = DiagnosticReader.Sanitize("password: veryprivate 192.168.1.1");
                if (sanitized.Contains("veryprivate") || sanitized.Contains("192.168.1.1"))
                    throw new InvalidOperationException("Diagnostic redaction regression.");
                NativeSetup.SelfTest();
                RemovalSelection.SelfTest();
                UninstallSelectionWindow.SelfTest();
                if (!SunshineDisplayPreference.IsAllowed(SunshineDisplayPreference.KeepPcPrimary) ||
                    !SunshineDisplayPreference.IsAllowed(SunshineDisplayPreference.MakeTabletPrimary) ||
                    SunshineDisplayPreference.IsAllowed("--sunshine-delete-display") ||
                    SunshineDisplayPreference.ModeForAction(SunshineDisplayPreference.KeepPcPrimary)!="ensure_active" ||
                    SunshineDisplayPreference.ModeForAction(SunshineDisplayPreference.MakeTabletPrimary)!="ensure_primary")
                    throw new InvalidOperationException("Display preference allowlist regression.");
                if (!SunshineServiceControl.IsAllowed(SunshineServiceControl.Start) ||
                    !SunshineServiceControl.IsAllowed(SunshineServiceControl.Stop) ||
                    !SunshineServiceControl.IsAllowed(SunshineServiceControl.Automatic) ||
                    !SunshineServiceControl.IsAllowed(SunshineServiceControl.Manual) ||
                    SunshineServiceControl.IsAllowed("--sunshine-delete") ||
                    SunshineServiceControl.IsAllowed("--native-remove"))
                    throw new InvalidOperationException("Sunshine service command allowlist regression.");
                // Offline package-integrity and policy-sensitive setup paths
                // are checked by CI; never run privileged installation here.
                var window = new MainWindow();
                if (window.Title != "OpenTabletDisplay")
                    throw new InvalidOperationException("WPF main window title regression.");
                window.Close();
                Shutdown(0);
            }
            catch { Shutdown(1); }
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, name: @"Local\OpenTabletDisplay.Desktop", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("OpenTabletDisplay is already running. Return to its existing window.",
                "OpenTabletDisplay", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
        if (e.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            ((MainWindow)MainWindow).NavigateToUninstall();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) {}
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnApplicationException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("The desktop interface encountered an error. No antivirus or driver settings were modified by the error handler.\n\n" +
                        e.Exception.Message, "OpenTabletDisplay", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
