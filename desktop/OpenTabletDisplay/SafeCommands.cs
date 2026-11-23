using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTabletDisplay;

internal sealed record CommandOutcome(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
    public string Summary => string.IsNullOrWhiteSpace(StandardError) ? StandardOutput.Trim() : StandardError.Trim();
}

internal static class SafeCommands
{
    // No shell execution, command interpolation, or arbitrary user-supplied executable paths.
    public static async Task<CommandOutcome> ExecuteAsync(string exe, IEnumerable<string> args,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException("Required component is not installed.", exe);
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        foreach (var value in args) start.ArgumentList.Add(value);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Windows refused to start the process.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            throw new InvalidOperationException(
                "Windows or endpoint protection denied access to the executable. Review its security verdict; do not disable antivirus or bypass the warning.", ex);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Windows could not start the executable: " + ex.Message, ex);
        }
        var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderr = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            return new CommandOutcome(process.ExitCode, output, errors);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch {}
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("The operation exceeded its safety deadline and was stopped. No antivirus or system security settings were changed.");
        }
    }

    public static Process StartElevatedInstaller(string installer)
    {
        if (!File.Exists(installer)) throw new FileNotFoundException("Installer script not found", installer);
        // A UAC-filtered administrator token often lacks an enabled
        // Administrators group. Do not reject based on token group membership.
        // Instead, pass the originating user's SID to the elevated script,
        // which checks it BEFORE writing any per-user files or system settings.
        using var identity = WindowsIdentity.GetCurrent();
        string originatingSid = identity.User?.Value ??
            throw new InvalidOperationException("Could not determine Windows account identity. Setup was not started.");
        bool isSetup = string.Equals(Path.GetFileName(installer), "install.ps1",
            StringComparison.OrdinalIgnoreCase);
        // The *compiled desktop app* is its own fixed-purpose elevated helper:
        // it records launch failures that would otherwise be lost in a hidden
        // PowerShell process. No arbitrary user-supplied script is accepted.
        if (isSetup)
        {
            string desktopExe = Environment.ProcessPath ??
                throw new InvalidOperationException("Could not identify this executable.");
            var start = new ProcessStartInfo(desktopExe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(desktopExe)!
            };
            start.ArgumentList.Add(ElevatedBootstrap.Argument);
            start.ArgumentList.Add(originatingSid);
            return Process.Start(start) ?? throw new InvalidOperationException("Windows did not start the elevated helper.");
        }
        // Uninstall stays a separately approved operation. It cannot run the
        // in-place EXE bootstrap because that would lock the EXE while removing it.
        var uninstall = new ProcessStartInfo(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = true, Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(installer)!,
            WindowStyle = ProcessWindowStyle.Normal
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File",
                                    installer, "-Elevated", "-ExpectedUserSid", originatingSid })
            uninstall.ArgumentList.Add(arg);
        return Process.Start(uninstall) ?? throw new InvalidOperationException("Windows did not start the elevated uninstaller.");
    }

    public static bool IsValidAndroidSerial(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        Regex.IsMatch(value, @"\A[A-Za-z0-9._:-]+\z", RegexOptions.CultureInvariant);

    public static void OpenTrustedUrl(string url)
    {
        if (url is not ("https://localhost:47990" or
            "https://play.google.com/store/apps/details?id=com.limelight" or
            "https://github.com/0pen-Sourcer/OpenTabletDisplay/blob/main/docs/TROUBLESHOOTING.md" or
            "https://github.com/0pen-Sourcer/OpenTabletDisplay/issues/new/choose"))
            throw new ArgumentException("Unapproved URL.", nameof(url));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
