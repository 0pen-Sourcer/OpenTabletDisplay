using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text.Json;

namespace OpenTabletDisplay;

internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FSDL");
    public static string RuntimeExe => Path.Combine(Root, "FSDL.exe");
    public static string InstallState => Path.Combine(Root, "install-state.json");
    public static string InstallLog => Path.Combine(Root, "logs", "install.log");
    public static string AgentLog => Path.Combine(Root, "runtime", "agent.log");
    public static string AdbExe => Path.Combine(Root, "runtime", "tools", "platform-tools", "adb.exe");
    public static string Enrollment => Path.Combine(Root, "enrollment.json");
    public static string SessionState => Path.Combine(Root, "runtime", "state.json");
    public static string LogsDirectory => Path.Combine(Root, "logs");


}

internal sealed record SystemSnapshot(
    bool Installed,
    bool RuntimePresent,
    string Sunshine,
    string Driver,
    bool Enrolled,
    string Host,
    string Detail,
    bool AdbPresent)
{
    public bool WindowsReady => Installed && Sunshine == "Running" && Driver == "Ready";
}

internal static class SystemInspector
{
    public static SystemSnapshot Read()
    {
        bool installed = File.Exists(AppPaths.InstallState);
        bool runtime = File.Exists(AppPaths.RuntimeExe);
        bool adb = File.Exists(AppPaths.AdbExe);
        var sunshine = ReadSunshine();
        var driver = ReadDisplay();
        var enrolled = File.Exists(AppPaths.Enrollment);
        string host = ReadHost();
        string detail = !installed ? runtime ? "Setup was interrupted before completion."
            : "Run one-time setup to prepare Windows." :
            !runtime ? "USB runtime files are missing: repair required."
            : sunshine == "Service stopped" ? "Sunshine is off. Start it when you want to stream."
            : sunshine != "Running" ? "Sunshine isn't available. Check its service."
            : driver != "Ready" ? "The Windows virtual-display driver needs attention."
            : "Windows dependencies are ready.";
        return new(installed, runtime, sunshine, driver, enrolled, host, detail, adb);
    }

    private static string ReadSunshine()
    {
        try
        {
            using var service = ServiceController.GetServices()
                .FirstOrDefault(s => string.Equals(s.ServiceName, "SunshineService", StringComparison.OrdinalIgnoreCase));
            if (service != null)
                return service.Status == ServiceControllerStatus.Running ? "Running" : "Service stopped";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "Status unavailable";
        }

        var roots = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sunshine", "sunshine.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sunshine", "tools", "sunshine.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Sunshine", "sunshine.exe")
        };
        return roots.Any(File.Exists) ? "Service missing" : "Not detected";
    }

    private static string ReadDisplay()
    {
        try
        {
            using var query = new ManagementObjectSearcher(
                "SELECT DeviceID, HardwareID, Status FROM Win32_PnPEntity " +
                "WHERE PNPClass = 'Display' OR DeviceID LIKE 'ROOT%'");
            using var results = query.Get();
            foreach (ManagementBaseObject device in results)
            {
                var ids = device["HardwareID"] as string[] ?? Array.Empty<string>();
                string instance = device["DeviceID"]?.ToString() ?? "";
                bool match = ids.Any(id => string.Equals(id, @"ROOT\MttVDD", StringComparison.OrdinalIgnoreCase))
                             || instance.StartsWith(@"ROOT\MTTVDD\", StringComparison.OrdinalIgnoreCase);
                if (match) return string.Equals(device["Status"]?.ToString(), "OK", StringComparison.OrdinalIgnoreCase)
                    ? "Ready" : "Needs attention";
            }
            return "Not detected";
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return "Status unavailable";
        }
    }

    private static string ReadHost()
    {
        try
        {
            if (!File.Exists(AppPaths.SessionState)) return "";
            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SessionState));
            string host = doc.RootElement.TryGetProperty("host_ip", out var prop) ? prop.GetString() ?? "" : "";
            if (IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetwork)
                return host;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {}
        return "";
    }

    public static bool ReadyForUsb(SystemSnapshot s, bool setupOrCommandRunning) =>
        !setupOrCommandRunning && s.Installed && s.RuntimePresent && s.AdbPresent &&
        s.Sunshine == "Running" && s.Driver == "Ready";
}
