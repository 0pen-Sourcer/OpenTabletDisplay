using System.Windows;

namespace OpenTabletDisplay;

public partial class UninstallSelectionWindow : Window
{
    public bool RemoveSunshine => SunshineCheck.IsChecked == true;
    public bool RemoveDriver => DriverCheck.IsChecked == true;

    public UninstallSelectionWindow(RemovalCapabilities available)
    {
        InitializeComponent();
        SunshineCheck.IsEnabled = available.SunshineAllowed;
        SunshineDetail.Text = available.SunshineReason;
        DriverCheck.IsEnabled = available.DriverAllowed;
        DriverDetail.Text = available.DriverReason;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        // Selecting Sunshine or the recorded display is intentionally opt-in.
        if ((RemoveSunshine && !SunshineCheck.IsEnabled) ||
            (RemoveDriver && !DriverCheck.IsEnabled)) return;
        DialogResult = true;
    }
}
