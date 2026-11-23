using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenTabletDisplay;

// Only our own installation record authorizes dependency removal. The OS
// must independently identify the EXACT target before deleting anything.
internal sealed record RemovalCapabilities(
    bool SunshineAllowed, string SunshineReason,
    bool DriverAllowed, string DriverReason);

internal static class RemovalSelection
{
    internal const string SunshineFlag = "--remove-owned-sunshine";
    internal const string DriverFlag = "--remove-owned-virtual-device";

    internal static bool TryParse(string[] flags, out bool sunshine, out bool driver)
    {
        sunshine = false;
        driver = false;
        if (flags.Length > 2) return false;
        foreach (string flag in flags)
        {
            if (flag == SunshineFlag && !sunshine) sunshine = true;
            else if (flag == DriverFlag && !driver) driver = true;
            else return false; // no arbitrary paths, aliases or duplicates
        }
        return true;
    }

    internal static InstalledRecord? ReadState()
    {
        try
        {
            if (!File.Exists(AppPaths.InstallState)) return null;
            var state = JsonSerializer.Deserialize<InstalledRecord>(File.ReadAllText(AppPaths.InstallState));
            // Do not trust records with missing identity or ownership fields.
            return state is { Version.Length: > 0, VddInstances: not null,
                SunshineChanges: not null } ? state : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static RemovalCapabilities Inspect()
    {
        var state = ReadState();
        string? product = state?.SunshineOwned == true ? FindOwnedSunshineMsi() : null;
        bool sunshineAllowed = state?.SunshineOwned == true && product is not null;
        var candidates = state?.DriverOwned == true
            ? state.VddInstances.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
        bool driverAllowed = candidates.Count > 0 && candidates.All(IsOwnedDisplayInstance);
        return new(
            sunshineAllowed,
            sunshineAllowed ? "Installed by OpenTabletDisplay. May also be used by other applications." :
                "Kept: an owned Sunshine MSI installation could not be verified.",
            driverAllowed,
            driverAllowed ? "Remove only the recorded virtual display device. Its driver package may remain in Windows." :
                "Kept: OpenTabletDisplay cannot prove ownership of this display device.");
    }

    // The original installer installs a PINNED Sunshine MSI. A later upgrade
    // or a second independent Sunshine install should NOT be uninstalled by us.
    internal static string? FindOwnedSunshineMsi()
    {
        var matches = new List<string>();
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (root is null) continue;
                foreach (var keyName in root.GetSubKeyNames())
                {
                    if (!Guid.TryParse(keyName, out var guid)) continue;
                    using var key = root.OpenSubKey(keyName);
                    if (key is null) continue;
                    if (!string.Equals(key.GetValue("DisplayName")?.ToString(), "Sunshine",
                        StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(key.GetValue("DisplayVersion")?.ToString(),
                        SetupConstants.SunshineVersion, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Convert.ToInt32(key.GetValue("WindowsInstaller", 0)) != 1) continue;
                    matches.Add(guid.ToString("B").ToUpperInvariant());
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException
                                       or IOException or FormatException or InvalidCastException)
            {
                return null;
            }
        }
        matches = matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    internal static bool IsOwnedDisplayInstance(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256 ||
            !Regex.IsMatch(id, @"\A[A-Za-z0-9_\\&{}#.-]+\z"))
            return false;
        try
        {
            // Verification comes from exact current PnP hardware IDs, NOT a
            // friendly-name or a common ROOT\DISPLAY prefix.
            return VirtualDriverSetup.DeviceIds().Contains(id, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is System.Management.ManagementException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool Validate(bool sunshine, bool driver, out string reason)
    {
        var state = ReadState();
        if (state is null)
        {
            reason = "The original OpenTabletDisplay installation record is unavailable.";
            return false;
        }
        if (sunshine && (!state.SunshineOwned || FindOwnedSunshineMsi() is null))
        {
            reason = "Sunshine ownership or its exact pinned MSI identity could not be verified.";
            return false;
        }
        if (driver && (!state.DriverOwned || state.VddInstances.Count == 0 ||
             !state.VddInstances.All(IsOwnedDisplayInstance)))
        {
            reason = "The exact virtual display device cannot be proven to belong to OpenTabletDisplay.";
            return false;
        }
        reason = "";
        return true;
    }

    internal static void SelfTest()
    {
        if (!TryParse(Array.Empty<string>(), out var a, out var b) || a || b)
            throw new InvalidOperationException("Default uninstall must keep dependencies.");
        if (!TryParse(new[]{ SunshineFlag, DriverFlag }, out a, out b) || !a || !b)
            throw new InvalidOperationException("Selected dependency flags rejected.");
        if (TryParse(new[]{ SunshineFlag, SunshineFlag }, out _, out _) ||
            TryParse(new[]{ "--remove-all" }, out _, out _) ||
            TryParse(new[]{ DriverFlag, "--path=C:\\" }, out _, out _))
            throw new InvalidOperationException("Uninstall argument allowlist regression.");
    }
}
