using System.Windows;

namespace OpenTabletDisplay;

public partial class UninstallSelectionWindow : Window
{
    public bool RemoveSunshine => SunshineCheck.IsChecked == true;
    public bool RemoveDriver => DriverCheck.IsChecked == true;

    internal UninstallSelectionWindow(RemovalCapabilities available)
    {
        InitializeComponent();
        SunshineCheck.IsEnabled = available.SunshineAllowed;
        SunshineDetail.Text = available.SunshineReason;
        DriverCheck.IsEnabled = available.DriverAllowed;
        DriverDetail.Text = available.DriverReason;
    }

    internal static void SelfTest()
    {
        var unavailable = new UninstallSelectionWindow(new RemovalCapabilities(
            false, "Keep Sunshine", false, "Keep display driver"));
        if (unavailable.RemoveSunshine || unavailable.RemoveDriver ||
            unavailable.SunshineCheck.IsEnabled || unavailable.DriverCheck.IsEnabled ||
            unavailable.AppCheck.IsChecked != true)
            throw new System.InvalidOperationException("Uninstall default selection or ownership guard failed.");
        unavailable.Close();

        var available = new UninstallSelectionWindow(new RemovalCapabilities(
            true, "Owner verified", true, "Driver verified"));
        if (available.RemoveSunshine || available.RemoveDriver ||
            !available.SunshineCheck.IsEnabled || !available.DriverCheck.IsEnabled)
            throw new System.InvalidOperationException("Opt-in uninstall selection regression.");
        available.Close();
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
