using System;
using System.Windows;
using System.Windows.Threading;

namespace OpenTabletDisplay;

public partial class App : Application
{
    private void OnApplicationException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("The desktop interface encountered an error. No driver or antivirus settings were changed.\n\n" +
                        e.Exception.Message, "OpenTabletDisplay", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnApplicationException;
        base.OnStartup(e);
    }
}
