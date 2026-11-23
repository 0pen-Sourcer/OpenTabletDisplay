using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OpenTabletDisplay;

internal static class NativeRemove
{
    [DllImport("kernel32.dll",EntryPoint="MoveFileExW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existing,string? replacement,uint flags);
    private const uint DelayUntilReboot=0x4;
    internal const int IdentityMismatch=73;
    internal const int Failed=75;

    internal static async Task<int> RunAsync(
        string expectedSid, bool removeSunshine = false, bool removeDriver = false)
    {
        string current=WindowsIdentity.GetCurrent().User?.Value??"";
        using var identity=WindowsIdentity.GetCurrent();
        if(current!=expectedSid || !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return IdentityMismatch;
        string root=AppPaths.Root;
        if(!Directory.Exists(root))return 0;
        // Absolutely never recurse into an app root redirected to another
        // filesystem subtree through a reparse point / junction.
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)return Failed;
        // Validate the entire deletion plan BEFORE stopping the agent or
        // modifying configuration. An altered plan must fail closed.
        if (!RemovalSelection.Validate(removeSunshine, removeDriver, out _))
            return Failed;
        string? sunshineMsi = removeSunshine ? RemovalSelection.FindOwnedSunshineMsi() : null;
        var ownedDevices = removeDriver
            ? RemovalSelection.ReadState()!.VddInstances
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
        if (removeSunshine && sunshineMsi is null) return Failed;
        string logPath=Path.Combine(Path.GetTempPath(),"OpenTabletDisplay-uninstall-"+Guid.NewGuid().ToString("N")+".log");
        void Log(string msg)=>File.AppendAllText(logPath,DateTime.Now.ToString("O")+" "+msg+"\r\n",Encoding.UTF8);
        try
        {
            Log("Uninstall selection: application=yes, Sunshine=" + removeSunshine +
                ", owned virtual display device=" + removeDriver +
                ". Components not selected remain installed.");
            foreach(string task in new[]{SetupConstants.TaskName,"FSDL USB Bridge Agent"})
            {
                try {await SafeCommands.ExecuteAsync(SetupProcesses.WindowsExe("schtasks.exe"),
                    new[]{"/End","/TN",task},TimeSpan.FromSeconds(10));}catch{}
                try {await SafeCommands.ExecuteAsync(SetupProcesses.WindowsExe("schtasks.exe"),
                    new[]{"/Delete","/TN",task,"/F"},TimeSpan.FromSeconds(12));}catch{}
            }
            if(File.Exists(AppPaths.RuntimeExe))
            {
                try {await SafeCommands.ExecuteAsync(AppPaths.RuntimeExe,
                    new[]{"cleanup"},TimeSpan.FromSeconds(30));}
                catch(Exception ex){Log("USB transport cleanup skipped: "+ex.Message);}
            }
            InstalledRecord? state=null;
            try {
                if(File.Exists(AppPaths.InstallState))
                    state=JsonSerializer.Deserialize<InstalledRecord>(File.ReadAllText(AppPaths.InstallState));
            }catch(Exception ex)when(ex is IOException or JsonException){Log("State read: "+ex.Message);}
            if(state?.SunshineConfigPath is string path &&
               path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                   StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                var currentLines=File.ReadAllLines(path);
                var restored=SunshineConfiguration.RestoreChangedOnly(currentLines,state.SunshineChanges);
                SunshineConfiguration.WriteAtomic(path,restored);
                if (!removeSunshine && !removeDriver)
                {
                    // Preserve the deliberate stopped state. Uninstalling the
                    // app should NOT unexpectedly start Sunshine.
                    try
                    {
                        using var service = new ServiceController("SunshineService");
                        if (service.Status == ServiceControllerStatus.Running)
                            await SunshineSetup.RestartAsync(new SetupLog(
                                Path.Combine(Path.GetTempPath(),"otd-uninstall-service-"+
                                    Guid.NewGuid().ToString("N")+".log")));
                    }
                    catch(Exception ex){Log("Sunshine refresh skipped: "+ex.Message);}
                }
                Log("Restored app-owned Sunshine entries, without replacing user modifications.");
            }
            if (removeDriver)
            {
                // Only exact device instance IDs recorded when our installer
                // created them are eligible. Other VDD devices, other driver's
                // packages and root-enumerated monitors must never be touched.
                if (ownedDevices.Length == 0 ||
                    ownedDevices.Any(x => !RemovalSelection.IsOwnedDisplayInstance(x)))
                    throw new InvalidOperationException("Owned virtual display identity changed. Removal cancelled.");
                try
                {
                    using var sunshine = new ServiceController("SunshineService");
                    if (sunshine.Status == ServiceControllerStatus.Running)
                    {
                        if (!sunshine.CanStop)
                            throw new InvalidOperationException(
                                "Sunshine is using the display and cannot be stopped safely.");
                        sunshine.Stop();
                        sunshine.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        Log("Stopped Sunshine before removing the selected virtual display device.");
                    }
                }
                catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception win &&
                    win.NativeErrorCode == 1060)
                {
                    // Sunshine was already removed or absent.
                }
                foreach (string id in ownedDevices)
                {
                    Log("Removing verified app-owned virtual-display instance " + id);
                    var result = await SafeCommands.ExecuteAsync(
                        SetupProcesses.WindowsExe("pnputil.exe"),
                        new[] { "/remove-device", id }, TimeSpan.FromSeconds(90));
                    if (!result.Success)
                        throw new InvalidOperationException(
                            "Windows refused to remove the selected virtual display device (exit " +
                            result.ExitCode + "). The driver package and other devices were left intact.");
                }
                Log("Selected virtual display device removed. Driver-store package retained.");
            }

            if (removeSunshine)
            {
                // Never run registry-supplied uninstaller command lines.
                // Execute Windows Installer ONLY with the exact, pinned,
                // ownership-verified MSI product GUID.
                string code = sunshineMsi ??
                    throw new InvalidOperationException("Owned Sunshine MSI identification was lost.");
                if (RemovalSelection.FindOwnedSunshineMsi() != code)
                    throw new InvalidOperationException("Sunshine MSI identity changed during uninstall.");
                Log("Removing the selected app-installed Sunshine MSI.");
                var result = await SafeCommands.ExecuteAsync(
                    SetupProcesses.WindowsExe("msiexec.exe"),
                    new[] { "/x", code, "/qn", "/norestart" }, TimeSpan.FromMinutes(4));
                // MSI exit 3010 means the removal succeeded but Windows
                // requires a restart to finish deleting in-use files.
                if (!result.Success && result.ExitCode != 3010)
                    throw new InvalidOperationException(
                        "Windows Installer could not remove Sunshine (exit " +
                        result.ExitCode + "). OpenTabletDisplay remains installed so removal can be retried.");
                Log(result.ExitCode == 3010
                    ? "Sunshine uninstalled; Windows needs a restart to finish cleanup."
                    : "Sunshine uninstall completed.");
            }
            try {Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FSDL",throwOnMissingSubKey:false);}
            catch(Exception ex){Log("Windows uninstall registry entry: "+ex.Message);}
            try
            {
                string link=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"OpenTabletDisplay.lnk");
                if(File.Exists(link))
                {
                    Type? t=Type.GetTypeFromProgID("WScript.Shell");
                    if(t is not null)
                    {
                        dynamic sh=Activator.CreateInstance(t)!;
                        dynamic shortcut=sh.CreateShortcut(link);
                        string target=(string)shortcut.TargetPath;
                        if(string.Equals(Path.GetFullPath(target),Path.Combine(root,"OpenTabletDisplay.exe"),
                            StringComparison.OrdinalIgnoreCase))File.Delete(link);
                    }
                }
            }
            catch(Exception ex){Log("Start Menu link cleanup: "+ex.Message);}

            // This elevated helper is executing from the installed .exe. An
            // in-use EXE cannot be deleted. Delete other app-owned files now,
            // then schedule ONLY this exact EXE and empty parent for deletion
            // by Windows on the next restart. Do not spawn cmd.exe scripts.
            string liveExe=Path.GetFullPath(Environment.ProcessPath??"");
            var directories = new List<string>();
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            // Inspect each directory before entering it: SearchOption.AllDirectories
            // would follow some junction types before a caller could reject them.
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Refusing to delete a redirected application directory.");
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Refusing to delete a redirected application file.");
                    files.Add(file);
                }
                foreach (var subdir in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(subdir) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Refusing to follow an application folder junction.");
                    directories.Add(subdir);
                    pending.Push(subdir);
                }
            }
            foreach(string file in files)
            {
                if(string.Equals(Path.GetFullPath(file),liveExe,StringComparison.OrdinalIgnoreCase))
                {
                    if(!MoveFileEx(file,null,DelayUntilReboot))
                        throw new IOException("Windows couldn't schedule deletion of the running app executable.");
                    continue;
                }
                try {File.Delete(file);}
                catch(IOException)
                {
                    if(!MoveFileEx(file,null,DelayUntilReboot))
                        throw new IOException("Failed to remove an app-owned file: "+Path.GetFileName(file));
                }
            }
            foreach(string dir in directories.OrderByDescending(x=>x.Length))
            {
                try {Directory.Delete(dir);}
                catch(IOException)
                {
                    if(!MoveFileEx(dir,null,DelayUntilReboot))
                        throw new IOException("Windows couldn't schedule app folder cleanup.");
                }
            }
            try {Directory.Delete(root);}
            catch(IOException) {if(!MoveFileEx(root,null,DelayUntilReboot))throw;}
            Log("Uninstall finished. The in-use executable and any pending files will be deleted at the next Windows restart.");
            return 0;
        }
        catch(Exception ex) {try {Log("Uninstall incomplete: "+ex);}catch{}return Failed;}
    }
}
