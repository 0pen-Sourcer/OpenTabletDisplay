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

    internal static async Task<int> RunAsync(string expectedSid)
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
        string logPath=Path.Combine(Path.GetTempPath(),"OpenTabletDisplay-uninstall-"+Guid.NewGuid().ToString("N")+".log");
        void Log(string msg)=>File.AppendAllText(logPath,DateTime.Now.ToString("O")+" "+msg+"\r\n",Encoding.UTF8);
        try
        {
            Log("Removing application integration. Keeping Sunshine and installed display drivers.");
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
                try {await SunshineSetup.RestartAsync(new SetupLog(
                    Path.Combine(Path.GetTempPath(),"otd-uninstall-service-"+Guid.NewGuid().ToString("N")+".log")));}
                catch(Exception ex){Log("Sunshine restart failed: "+ex.Message);}
                Log("Restored app-owned Sunshine entries, without replacing user modifications.");
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
            foreach(string file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))
            {
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                {Log("Refusing reparse-point file: "+file);return Failed;}
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
                        throw new IOException("Failed to remove or schedule an application-owned file: "+Path.GetFileName(file));
                }
            }
            foreach(string dir in Directory.EnumerateDirectories(root,"*",SearchOption.AllDirectories)
                .OrderByDescending(x=>x.Length))
            {
                if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Cannot remove a junction inside application files.");
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
