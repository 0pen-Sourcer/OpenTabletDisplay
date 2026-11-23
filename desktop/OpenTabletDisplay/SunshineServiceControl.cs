using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace OpenTabletDisplay;

/// <summary>
/// Narrow, user-triggered control of the existing Sunshine Windows service.
/// No installer, shell, credentials, antivirus settings or other services are
/// touched. Changes to startup mode never start or stop Sunshine implicitly.
/// </summary>
internal static class SunshineServiceControl
{
    internal const string ServiceName = "SunshineService";
    internal const int IdentityMismatch = 73;
    internal const int OperationFailed = 79;
    internal const string Start = "--sunshine-start";
    internal const string Stop = "--sunshine-stop";
    internal const string Automatic = "--sunshine-automatic";
    internal const string Manual = "--sunshine-manual";

    internal static string LogPath => Path.Combine(AppPaths.LogsDirectory, "sunshine-service.log");

    internal static bool IsAllowed(string action) =>
        action is Start or Stop or Automatic or Manual;

    internal static bool IsPermissionDenied(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException ||
        ex is Win32Exception win && win.NativeErrorCode is 5 or 1314 ||
        (uint)ex.HResult == 0x80070005 ||
        ex.InnerException is not null && IsPermissionDenied(ex.InnerException);

    internal static string ReadStartupMode()
    {
        try
        {
            using var query = new ManagementObjectSearcher(
                "SELECT StartMode FROM Win32_Service WHERE Name='SunshineService'");
            using var result = query.Get();
            string? mode = result.Cast<ManagementBaseObject>()
                .FirstOrDefault()?["StartMode"]?.ToString();
            return mode switch
            {
                "Auto" => "Automatic",
                "Manual" => "Manual",
                "Disabled" => "Disabled",
                null => "Unavailable",
                _ => mode
            };
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException
                                   or System.Runtime.InteropServices.COMException)
        {
            return "Unavailable";
        }
    }

    internal static async Task ChangeAsync(string action)
    {
        if (!IsAllowed(action)) throw new ArgumentException("Unsupported Sunshine operation.");
        if (action is Start or Stop)
        {
            await Task.Run(() => ChangeServiceState(action));
            return;
        }
        await Task.Run(() => ChangeStartMode(action));
    }

    internal static async Task<int> RunElevatedAsync(string action, string expectedUserSid)
    {
        // Fail closed before any file or service operation. Windows UAC consent
        // for a different admin account must not silently change their service.
        string actualSid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        using var identity = WindowsIdentity.GetCurrent();
        if (!IsAllowed(action) || string.IsNullOrWhiteSpace(expectedUserSid) ||
            actualSid != expectedUserSid ||
            !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return IdentityMismatch;

        try
        {
            await ChangeAsync(action);
            WriteLog(action + ": completed");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLog(action + ": " + ex.GetType().Name + ": " + ex.Message);
            return OperationFailed;
        }
    }

    private static void ChangeServiceState(string action)
    {
        using var service = new ServiceController(ServiceName);
        // Querying Status throws when the service is missing. Never fall back
        // to terminating sunshine.exe: it could be a different user process.
        service.Refresh();
        var status = service.Status;
        var desired = action == Start
            ? ServiceControllerStatus.Running
            : ServiceControllerStatus.Stopped;
        if (status == desired) return;

        if (action == Start)
        {
            if (status == ServiceControllerStatus.StopPending)
                service.WaitForStatus(ServiceControllerStatus.Stopped,
                    TimeSpan.FromSeconds(30));
            service.Refresh();
            if (service.Status == ServiceControllerStatus.Stopped) service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running,
                TimeSpan.FromSeconds(35));
        }
        else
        {
            if (status == ServiceControllerStatus.StartPending)
                service.WaitForStatus(ServiceControllerStatus.Running,
                    TimeSpan.FromSeconds(30));
            service.Refresh();
            if (service.Status == ServiceControllerStatus.Running)
            {
                if (!service.CanStop)
                    throw new InvalidOperationException("Windows reports that Sunshine cannot be stopped.");
                service.Stop();
            }
            service.WaitForStatus(ServiceControllerStatus.Stopped,
                TimeSpan.FromSeconds(35));
        }
        service.Refresh();
        if (service.Status != desired)
            throw new InvalidOperationException("Sunshine did not reach the requested service state.");
    }

    private static void ChangeStartMode(string action)
    {
        string mode = action == Automatic ? "Automatic" : "Manual";
        // WMI ChangeStartMode uses the native Windows service API and returns
        // a documented status code. It is not a PowerShell/sc.exe invocation.
        using var service = new ManagementObject(
            new ManagementPath("Win32_Service.Name='" + ServiceName + "'"));
        using var parameters = service.GetMethodParameters("ChangeStartMode");
        parameters["StartMode"] = mode;
        using var result = service.InvokeMethod("ChangeStartMode", parameters, null);
        if (result is null || result["ReturnValue"] is null)
            throw new InvalidOperationException("Windows did not return a Sunshine startup-mode result.");
        uint status = Convert.ToUInt32(result["ReturnValue"], CultureInfo.InvariantCulture);
        if (status == 2)
            throw new UnauthorizedAccessException("Administrator approval is required to change Sunshine startup settings.");
        if (status != 0)
            throw new InvalidOperationException(
                "Windows could not change Sunshine's startup preference (code " + status + ").");
    }

    private static void WriteLog(string line)
    {
        try
        {
            // Only this one app-owned log is written, never Sunshine's config
            // or credentials. Report errors, but never suppress service failure.
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            File.AppendAllText(LogPath, "[" + DateTimeOffset.Now.ToString("O") +
                "] " + line + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
