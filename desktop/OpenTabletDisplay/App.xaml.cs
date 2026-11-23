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
        if (e.Args.Length == 2 &&
            string.Equals(e.Args[0], ElevatedBootstrap.Argument, StringComparison.Ordinal))
        {
            // This fixed-purpose UAC helper has no user-facing WPF window.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int result = await ElevatedBootstrap.RunAsync(e.Args[1]);
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
