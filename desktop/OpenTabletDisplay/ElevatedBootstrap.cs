using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace OpenTabletDisplay;

/// <summary>
/// Elevated, fixed-purpose installer launcher. This is the SAME compiled WPF
/// executable in a separate UAC-approved process, not a PowerShell-hosted UI.
/// It captures PowerShell startup failures that previously disappeared before
/// install.log was created. It NEVER changes execution policy or AV settings.
/// </summary>
internal static class ElevatedBootstrap
{
    public const string Argument = "--elevated-setup";
    public const int IdentityMismatch = 73;
    public const int PreflightFailure = 74;
    public const int PowerShellFailure = 75;
    public static string LogPath => Path.Combine(AppPaths.LogsDirectory, "bootstrap-setup.log");

    public static async Task<int> RunAsync(string expectedSid)
    {
        // Check the exact logon identity before touching per-user state.
        string actualSid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        if (expectedSid.Length < 5 || actualSid != expectedSid)
            return IdentityMismatch;

        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            string script = AppPaths.InstallerScript ??
                throw new FileNotFoundException("Packaged install.ps1 not found.");
            string ps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(ps)) throw new FileNotFoundException("Windows PowerShell not installed.", ps);

            await File.WriteAllTextAsync(LogPath,
                $"OpenTabletDisplay elevated setup bootstrap, UTC {DateTime.UtcNow:O}\r\n" +
                $"Original Windows SID verified. Script: {script}\r\n" +
                "System and antivirus security settings remain unchanged.\r\n",
                Encoding.UTF8);

            // This preflight is read-only. Windows client systems commonly use
            // Restricted; the script may therefore fail to launch when unsigned.
            try
            {
                var policy = await SafeCommands.ExecuteAsync(ps,
                    new[] { "-NoProfile", "-NonInteractive", "-Command",
                        "Get-ExecutionPolicy | Out-String" }, TimeSpan.FromSeconds(12));
                await AppendAsync("PowerShell execution policy: " +
                    (policy.StandardOutput.Trim() is { Length: > 0 } p ? p : "unavailable") + "\r\n");
            }
            catch (Exception ex) { await AppendAsync("Policy check unavailable: " + ex.Message + "\r\n"); }

            var cmd = new ProcessStartInfo(ps)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(script)!
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File",
                                        script, "-Elevated", "-ExpectedUserSid", expectedSid })
                cmd.ArgumentList.Add(arg);

            using var child = new Process { StartInfo = cmd };
            if (!child.Start()) throw new InvalidOperationException(
                "Windows refused to launch the setup script.");
            // Capture both pipes concurrently to avoid buffer deadlocks.
            Task<string> output = child.StandardOutput.ReadToEndAsync();
            Task<string> errors = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync();
            string stdout = await output;
            string stderr = await errors;
            await AppendAsync("Script exit code: " + child.ExitCode + "\r\n" +
                "[stdout]\r\n" + Limit(stdout) + "\r\n[stderr]\r\n" + Limit(stderr) + "\r\n");
            return child.ExitCode == 0 ? 0 : PowerShellFailure;
        }
        catch (Exception ex)
        {
            try { await AppendAsync("Bootstrap failure: " + ex + "\r\n"); } catch {}
            return PreflightFailure;
        }
    }

    private static string Limit(string text) => text.Length > 20000 ? text[^20000..] : text;
    private static Task AppendAsync(string text) =>
        File.AppendAllTextAsync(LogPath, text, Encoding.UTF8);
}
