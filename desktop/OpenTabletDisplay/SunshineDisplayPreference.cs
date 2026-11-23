using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTabletDisplay;

/// <summary>
/// Explicit, narrow repair for a historical OpenTabletDisplay configuration:
/// old releases used ensure_primary, making the streamed VDD the Windows main
/// display. Convert only our previously-applied setting to ensure_active.
/// Other Sunshine settings, Windows defaults, drivers and pairings are retained.
/// </summary>
internal static class SunshineDisplayPreference
{
    internal const string Action = "--sunshine-keep-host-primary";
    internal const int IdentityMismatch = 73;
    internal const int Failed = 75;
    internal static string ErrorFile => Path.Combine(AppPaths.LogsDirectory, "display-preference-error.txt");

    internal static async Task<int> RunAsync(string originatingSid)
    {
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? "";
        if (originatingSid.Length < 5 || !string.Equals(sid, originatingSid, StringComparison.Ordinal) ||
            !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return IdentityMismatch;

        try
        {
            if (!File.Exists(AppPaths.InstallState))
                throw new InvalidOperationException("OpenTabletDisplay is not installed.");

            var state = JsonSerializer.Deserialize<InstalledRecord>(
                File.ReadAllText(AppPaths.InstallState)) ??
                throw new InvalidDataException("The installed state is incomplete.");
            var previous = state.SunshineChanges.FirstOrDefault(c =>
                string.Equals(c.Key,"dd_configuration_option",StringComparison.OrdinalIgnoreCase));

            // Only change the exact setting that OTD itself previously wrote.
            if (previous is null || !previous.AppliedPresent ||
                previous.AppliedValue is not ("ensure_primary" or "ensure_active"))
                throw new InvalidOperationException(
                    "This installation does not own Sunshine's display mode. No change was made.");

            string config = SunshineSetup.ConfigFile() ??
                throw new FileNotFoundException("Sunshine configuration was not found.");
            if (!string.Equals(Path.GetFullPath(config),
                    Path.GetFullPath(state.SunshineConfigPath ?? ""),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Sunshine's configuration location changed. No change was made.");
            foreach (var path in new[] { Path.GetDirectoryName(config)!, config, AppPaths.Root })
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidOperationException(
                        "Refusing to edit a redirected application or Sunshine configuration path.");

            string[] existing = File.ReadAllLines(config);
            var mode = SunshineConfiguration.Get(existing, "dd_configuration_option");
            if (!mode.present || mode.value != previous.AppliedValue)
                throw new InvalidOperationException(
                    "Sunshine's mode was changed outside OpenTabletDisplay. Your setting was preserved.");
            if (mode.value == "ensure_active") return 0;

            string[] updated = SunshineConfiguration.Set(
                existing, "dd_configuration_option", "ensure_active");
            string backup = config + ".otd-primary-backup";
            File.Copy(config,backup,true);
            try
            {
                SunshineConfiguration.WriteAtomic(config,updated);
                await SunshineSetup.RestartAsync(new SetupLog(
                    Path.Combine(AppPaths.LogsDirectory,"sunshine-display-restart.log")));
                previous.AppliedValue = "ensure_active";
                string staging = AppPaths.InstallState + ".display-pending";
                File.WriteAllText(staging,JsonSerializer.Serialize(state,
                    new JsonSerializerOptions {WriteIndented=true}));
                File.Move(staging,AppPaths.InstallState,true);
            }
            catch
            {
                // Revert only the exact key we changed, and only if it still
                // has our value. Keep unrelated user edits made concurrently.
                if (File.Exists(config))
                {
                    var now=File.ReadAllLines(config);
                    if (SunshineConfiguration.Get(now,"dd_configuration_option").value=="ensure_active")
                        SunshineConfiguration.WriteAtomic(config,
                            SunshineConfiguration.Set(now,"dd_configuration_option","ensure_primary"));
                }
                throw;
            }
            finally
            {
                try { if(File.Exists(backup))File.Delete(backup); }
                catch(IOException) {}
            }
            return 0;
        }
        catch(Exception ex)
        {
            try
            {
                if(Directory.Exists(AppPaths.LogsDirectory))
                    File.WriteAllText(ErrorFile,
                        "Display preference update failed: "+ex.GetType().Name+
                        ": "+ex.Message);
            }
            catch(IOException) {}
            catch(UnauthorizedAccessException) {}
            return Failed;
        }
    }
}
