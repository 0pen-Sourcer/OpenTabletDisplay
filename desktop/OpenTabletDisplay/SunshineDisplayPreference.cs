using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTabletDisplay;

/// <summary>
/// Controls only the Sunshine display-mode setting previously installed by
/// OpenTabletDisplay. Preserves the pre-install value for safe uninstall.
/// No service installation, driver changes, display-number guessing or shell.
/// </summary>
internal static class SunshineDisplayPreference
{
    internal const string KeepPcPrimary = "--sunshine-keep-host-primary";
    internal const string MakeTabletPrimary = "--sunshine-make-tablet-primary";
    internal const int IdentityMismatch = 73;
    internal const int Failed = 75;
    internal const string Key = "dd_configuration_option";
    internal static string ErrorFile => Path.Combine(AppPaths.LogsDirectory,"display-preference-error.txt");

    internal static bool IsAllowed(string? action) =>
        action is KeepPcPrimary or MakeTabletPrimary;
    internal static string ModeForAction(string action) => action switch
    {
        KeepPcPrimary => "ensure_active",
        MakeTabletPrimary => "ensure_primary",
        _ => throw new ArgumentException("Unsupported Sunshine display preference.")
    };

    internal static string ReadMode()
    {
        try
        {
            var state = LoadState();
            if (state is null) return "Unavailable";
            var change = FindOwnedChange(state);
            if (change is null || !change.AppliedPresent ||
                change.AppliedValue is not ("ensure_active" or "ensure_primary"))
                return "Unmanaged";
            string? config = SunshineSetup.ConfigFile();
            if (config is null || !string.Equals(
                Path.GetFullPath(config),Path.GetFullPath(state.SunshineConfigPath ?? ""),
                StringComparison.OrdinalIgnoreCase)) return "Unmanaged";
            var current = SunshineConfiguration.Get(File.ReadAllLines(config),Key);
            if (!current.present || current.value != change.AppliedValue) return "Unmanaged";
            return current.value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or JsonException or ArgumentException)
        {
            return "Unavailable";
        }
    }

    internal static async Task<int> RunAsync(string action, string originatingSid)
    {
        // Reject a different-account UAC prompt before reading/writing files.
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? "";
        if (!IsAllowed(action) || originatingSid.Length < 5 ||
            !string.Equals(sid, originatingSid, StringComparison.Ordinal) ||
            !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return IdentityMismatch;

        try
        {
            string desired = ModeForAction(action);
            var state = LoadState() ??
                throw new InvalidOperationException("OpenTabletDisplay is not installed.");
            var previous = FindOwnedChange(state);
            if (previous is null || !previous.AppliedPresent ||
                previous.AppliedValue is not ("ensure_primary" or "ensure_active"))
                throw new InvalidOperationException(
                    "OpenTabletDisplay does not own this Sunshine preference. No changes were made.");

            string config = SunshineSetup.ConfigFile() ??
                throw new FileNotFoundException("Sunshine configuration is missing.");
            if (!string.Equals(Path.GetFullPath(config),
                Path.GetFullPath(state.SunshineConfigPath ?? ""),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Sunshine's configuration location changed. No changes were made.");

            foreach (var path in new[] {Path.GetDirectoryName(config)!,config,AppPaths.Root})
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidOperationException("Refusing redirected configuration or app files.");

            string[] initial = File.ReadAllLines(config);
            var current = SunshineConfiguration.Get(initial,Key);
            if (!current.present || current.value != previous.AppliedValue)
                throw new InvalidOperationException(
                    "Someone changed this setting directly in Sunshine. Your manual change was preserved.");

            string originalMode = current.value!;
            bool changed = originalMode != desired;
            bool restartRequired = false;
            if (changed)
            {
                using var service = new ServiceController("SunshineService");
                service.Refresh();
                if (service.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.Stopped))
                    throw new InvalidOperationException(
                        "Sunshine is changing state. Retry once it is running or stopped.");
                restartRequired = service.Status == ServiceControllerStatus.Running;
            }

            string backup = config+".otd-display-mode-backup";
            bool completed = false;
            if (changed) File.Copy(config,backup,true);
            try
            {
                if (changed)
                {
                    var updated = SunshineConfiguration.Set(initial,Key,desired);
                    SunshineConfiguration.WriteAtomic(config,updated);
                    // Only restart if user was already streaming/ready. Do NOT
                    // start Sunshine if user deliberately switched it off.
                    if (restartRequired)
                        await SunshineSetup.RestartAsync(new SetupLog(
                            Path.Combine(AppPaths.LogsDirectory,"sunshine-display-restart.log")));
                }

                previous.AppliedValue=desired;
                state.PreferredDisplayMode=desired;
                string staged=AppPaths.InstallState+".display-pending";
                File.WriteAllText(staged,JsonSerializer.Serialize(state,
                    new JsonSerializerOptions {WriteIndented=true}));
                File.Move(staged,AppPaths.InstallState,true);
                completed=true;
            }
            catch
            {
                if (changed && File.Exists(config))
                {
                    try
                    {
                        var currentLines = File.ReadAllLines(config);
                        if (SunshineConfiguration.Get(currentLines,Key).value==desired)
                            SunshineConfiguration.WriteAtomic(config,
                                SunshineConfiguration.Set(currentLines,Key,originalMode));
                        if (restartRequired)
                            await SunshineSetup.RestartAsync(new SetupLog(
                                Path.Combine(AppPaths.LogsDirectory,"sunshine-display-rollback.log")));
                    }
                    catch
                    {
                        // Preserve original backup for user-assisted recovery.
                        throw new InvalidOperationException(
                            "Display preference could not be applied or safely rolled back. " +
                            "Your original Sunshine configuration backup was preserved.");
                    }
                }
                throw;
            }
            finally
            {
                if (completed && changed)
                {
                    try {if (File.Exists(backup))File.Delete(backup);}
                    catch (IOException) {}
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.GetType().Name+": "+ex.Message);
            return Failed;
        }
    }

    private static InstalledRecord? LoadState()
    {
        if (!File.Exists(AppPaths.InstallState)) return null;
        return JsonSerializer.Deserialize<InstalledRecord>(File.ReadAllText(AppPaths.InstallState));
    }

    private static SunshineChange? FindOwnedChange(InstalledRecord state) =>
        state.SunshineChanges.FirstOrDefault(c =>
            string.Equals(c.Key,Key,StringComparison.OrdinalIgnoreCase));

    private static void WriteError(string error)
    {
        try
        {
            foreach (string dir in new[] {AppPaths.Root,AppPaths.LogsDirectory})
                if (Directory.Exists(dir) &&
                    File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint)) return;
            if (File.Exists(ErrorFile) &&
                File.GetAttributes(ErrorFile).HasFlag(FileAttributes.ReparsePoint)) return;
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            File.WriteAllText(ErrorFile,"Display preference update failed: "+error);
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }
}
