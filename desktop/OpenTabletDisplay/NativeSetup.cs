using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OpenTabletDisplay;

internal sealed class SunshineChange
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("original_present")] public bool OriginalPresent { get; set; }
    [JsonPropertyName("original_value")] public string? OriginalValue { get; set; }
    [JsonPropertyName("applied_present")] public bool AppliedPresent { get; set; }
    [JsonPropertyName("applied_value")] public string? AppliedValue { get; set; }
}

internal sealed class InstalledRecord
{
    [JsonPropertyName("version")] public string Version { get; set; } = SetupConstants.Version;
    [JsonPropertyName("installed_at")] public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.Now;
    [JsonPropertyName("sunshine_installed_by_fsdl")] public bool SunshineOwned { get; set; }
    [JsonPropertyName("vdd_installed_by_fsdl")] public bool DriverOwned { get; set; }
    [JsonPropertyName("sunshine_config_path")] public string? SunshineConfigPath { get; set; }
    [JsonPropertyName("sunshine_backup")] public string? SunshineBackup { get; set; }
    [JsonPropertyName("sunshine_changes")] public List<SunshineChange> SunshineChanges { get; set; } = new();
    [JsonPropertyName("vdd_instance_ids")] public List<string> VddInstances { get; set; } = new();
    [JsonPropertyName("vdd_driver_names")] public List<string> VddDrivers { get; set; } = new();
}

internal static class SunshineConfiguration
{
    internal static readonly Dictionary<string,string> Values=new(StringComparer.OrdinalIgnoreCase)
    {
        ["dd_configuration_option"]="ensure_primary",
        ["dd_resolution_option"]="auto",
        ["dd_refresh_rate_option"]="auto",
        ["dd_config_revert_on_disconnect"]="enabled",
        ["dd_config_revert_delay"]="1000"
    };
    private static readonly Dictionary<string,string> Legacy=new(StringComparer.OrdinalIgnoreCase)
    {
        ["dd_resolution_option"]="manual",
        ["dd_manual_resolution"]="1280x800",
        ["dd_refresh_rate_option"]="manual",
        ["dd_manual_refresh_rate"]="60",
        ["max_bitrate"]="40000",
        ["minimum_fps_target"]="60",
        ["nvenc_preset"]="1",
        ["controller"]="disabled",
        ["gamepad_driver"]="none"
    };
    private static readonly string[] LegacyRemove={"dd_manual_resolution","dd_manual_refresh_rate",
        "max_bitrate","minimum_fps_target","encoder","nvenc_preset","controller","gamepad_driver"};

    public static (bool present,string? value) Get(IEnumerable<string> lines,string key)
    {
        var rx=new Regex(@"^\s*"+Regex.Escape(key)+@"\s*=\s*(.*)$",RegexOptions.IgnoreCase);
        foreach(string line in lines) {var m=rx.Match(line);if(m.Success)return(true,m.Groups[1].Value.Trim());}
        return(false,null);
    }
    public static string[] Set(IEnumerable<string> lines,string key,string? value)
    {
        var result=new List<string>();bool written=false;
        var rx=new Regex(@"^\s*"+Regex.Escape(key)+@"\s*=",RegexOptions.IgnoreCase);
        foreach(var line in lines)
        {
            if(rx.IsMatch(line))
            {
                if(value is not null && !written){result.Add(key+" = "+value);written=true;}
            }
            else result.Add(line);
        }
        if(value is not null && !written)result.Add(key+" = "+value);
        return result.ToArray();
    }
    public static (string[] lines,List<SunshineChange> changes) Apply(string[] original,string id)
    {
        if(!Regex.IsMatch(id,@"^\{[0-9a-fA-F-]{36}\}$"))
            throw new InvalidDataException("Invalid Sunshine display identifier.");
        string[] lines=original.ToArray();
        var changes=new List<SunshineChange>();
        var desired=new Dictionary<string,string>(Values,StringComparer.OrdinalIgnoreCase)
            {["output_name"]=id};
        foreach(var pair in desired)
        {
            var before=Get(lines,pair.Key);
            changes.Add(new SunshineChange{
                Key=pair.Key,OriginalPresent=before.present,OriginalValue=before.value,
                AppliedPresent=true,AppliedValue=pair.Value});
            lines=Set(lines,pair.Key,pair.Value);
        }
        int legacyScore=Legacy.Count(p=>Get(original,p.Key) is var q && q.present && q.value==p.Value);
        if(legacyScore>=6)
            foreach(string key in LegacyRemove)
            {
                var old=Get(lines,key);
                if(!old.present)continue;
                changes.Add(new SunshineChange{Key=key,OriginalPresent=true,OriginalValue=old.value,
                    AppliedPresent=false,AppliedValue=null});
                lines=Set(lines,key,null);
            }
        return(lines,changes);
    }
    public static string[] RestoreChangedOnly(string[] current,IEnumerable<SunshineChange> changes)
    {
        var lines=current.ToArray();
        foreach(var change in changes)
        {
            var actual=Get(lines,change.Key);
            if(actual.present!=change.AppliedPresent ||
                (actual.present && !StringComparer.Ordinal.Equals(actual.value,change.AppliedValue)))
                continue; // user has changed it since installation
            lines=Set(lines,change.Key,change.OriginalPresent?change.OriginalValue:null);
        }
        return lines;
    }
    public static void WriteAtomic(string file,string[] lines)
    {
        string pending=file+".otd-pending";
        File.WriteAllLines(pending,lines,new UTF8Encoding(false));
        File.Move(pending,file,true);
    }
}

internal static class VirtualDriverSetup
{
    public static List<string> DeviceIds()
    {
        var ids=new List<string>();
        using var search=new ManagementObjectSearcher(
            "SELECT DeviceID,HardwareID,Status FROM Win32_PnPEntity WHERE PNPClass='Display' OR DeviceID LIKE 'ROOT%'");
        using var result=search.Get();
        foreach(ManagementBaseObject entry in result)
        {
            string id=entry["DeviceID"]?.ToString()??"";
            string[] hw=entry["HardwareID"] as string[] ?? Array.Empty<string>();
            if(hw.Any(x=>x.Equals(@"ROOT\MttVDD",StringComparison.OrdinalIgnoreCase)) ||
                id.StartsWith(@"ROOT\MTTVDD\",StringComparison.OrdinalIgnoreCase))
                ids.Add(id);
        }
        return ids;
    }
    public static bool IsReady() => SystemInspector.Read().Driver=="Ready";
    public static async Task<List<string>> EnsureAsync(SetupLog log)
    {
        var previous=DeviceIds();
        if(IsReady()) {log.Write("Healthy Virtual Display Driver detected; existing driver preserved.");return new();}
        if(previous.Count>0) throw new InvalidOperationException(
            "The virtual-display driver is installed but unhealthy. Correct it through Windows Device Manager before setup; no driver trust settings were changed.");
        log.Write("Virtual Display Driver not installed. Retrieving pinned driver and device tool.");
        string zip=await PinnedDownload.FetchAsync(SetupConstants.VddUrl,SetupConstants.VddSha,
            Path.Combine(AppPaths.Root,"cache","VirtualDisplayDriver-"+SetupConstants.VddVersion+".zip"),log);
        string nefZip=await PinnedDownload.FetchAsync(SetupConstants.NefconUrl,SetupConstants.NefconSha,
            Path.Combine(AppPaths.Root,"cache","nefcon-1.14.0.zip"),log);
        string temp=Path.Combine(Path.GetTempPath(),"otd-driver-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string vdd=Path.Combine(temp,"vdd"),nef=Path.Combine(temp,"nefcon");
            ZipFile.ExtractToDirectory(zip,vdd);
            ZipFile.ExtractToDirectory(nefZip,nef);
            string inf=Directory.EnumerateFiles(vdd,"MttVDD.inf",SearchOption.AllDirectories).FirstOrDefault()??
                throw new InvalidDataException("Pinned driver package is missing MttVDD.inf.");
            string cat=Directory.EnumerateFiles(vdd,"mttvdd.cat",SearchOption.AllDirectories).FirstOrDefault()??
                throw new InvalidDataException("Pinned driver package is missing its catalog signature.");
            string nefexe=Directory.EnumerateFiles(nef,"nefconw.exe",SearchOption.AllDirectories)
                .FirstOrDefault(x=>x.Contains(@"\x64\",StringComparison.OrdinalIgnoreCase))??
                throw new InvalidDataException("Pinned driver tool has no x64 executable.");
            WindowsTrust.RequireSigned(cat);
            log.Write("Verified trusted Windows driver catalog; installing exact ROOT\\MttVDD device.");
            await SetupProcesses.RequireAsync(nefexe,new[] {"install",inf,@"Root\MttVDD"},
                TimeSpan.FromMinutes(2),"Install virtual display driver",log);
            await SetupProcesses.RequireAsync(SetupProcesses.WindowsExe("pnputil.exe"),
                new[]{"/scan-devices"},TimeSpan.FromSeconds(35),"Refresh Windows device list",log);
            for(int i=0;i<15;i++)
            {
                if(IsReady())
                {
                    var created=DeviceIds().Except(previous,StringComparer.OrdinalIgnoreCase).ToList();
                    log.Write("Virtual Display Driver detected and healthy.");
                    return created;
                }
                await Task.Delay(2000);
            }
            throw new InvalidOperationException("The signed driver command finished but Windows did not report a healthy MttVDD device.");
        }
        finally {try {Directory.Delete(temp,true);}catch(IOException){}}
    }
}

internal static class NativeSetup
{
    public const int IdentityMismatch=73;
    public const int Failed=75;
    public static async Task<int> InstallAsync(string expectedSid)
    {
        // No downloads, logs, file writes or drivers before same-account UAC verification.
        if(!IsAuthorized(expectedSid))return IdentityMismatch;
        SetupLog? log=null;
        FileStream? exclusive=null;
        var changes=new List<SunshineChange>();
        string? cfg=null;
        bool applied=false;
        List<string> createdDriver=new();
        try
        {
            if(!Environment.Is64BitOperatingSystem)throw new PlatformNotSupportedException("64-bit Windows is required.");
            string root=NativePackage.Discover()??throw new FileNotFoundException(
                "The complete extracted release is missing. Use the packaged ZIP, not GitHub's Source ZIP.");
            var package=new NativePackage(root);
            foreach(string entry in package.Entries)package.RequireVerified(entry);
            Directory.CreateDirectory(AppPaths.Root);
            exclusive=new FileStream(Path.Combine(AppPaths.Root,"install.lock"),FileMode.OpenOrCreate,
                FileAccess.ReadWrite,FileShare.None);
            log=new SetupLog(AppPaths.InstallLog);
            log.Write("Starting native Windows installer. No PowerShell scripts will be executed.");
            log.Write("Full packaged release verified. Same-account administrator identity verified.");
            var old=LoadState();
            // Keep user enrollment and runtime directories; never reset them on upgrade.
            await StopPriorAgentAsync(log);
            CopyRuntime(package,log);
            bool sunAlready=SunshineSetup.HasService();
            if(!sunAlready)
            {
                string msi=await PinnedDownload.FetchAsync(SetupConstants.SunshineUrl,
                    SetupConstants.SunshineSha,Path.Combine(AppPaths.Root,"cache","Sunshine-"+
                        SetupConstants.SunshineVersion+".msi"),log);
                WindowsTrust.RequireSigned(msi);
                log.Write("Windows verified the pinned Sunshine MSI signature.");
                await SetupProcesses.RequireAsync(SetupProcesses.WindowsExe("msiexec.exe"),
                    new[] {"/i",msi,"/qn","/norestart"},TimeSpan.FromMinutes(4),"Install Sunshine",log);
            }
            if(!SunshineSetup.HasService())throw new InvalidOperationException(
                "Sunshine installation ended but SunshineService was not registered.");
            using(var sun=new ServiceController("SunshineService"))
            {
                sun.Refresh();
                if(sun.Status!=ServiceControllerStatus.Running)
                {
                    sun.Start();sun.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(40));
                }
            }
            await SunshineSetup.WaitHealthyAsync(log);
            if(!sunAlready)
            {
                string executable=SunshineSetup.Executable()??
                    throw new FileNotFoundException("Sunshine executable missing after installation.");
                string password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
                    .TrimEnd('=').Replace('+','A').Replace('/','B');
                await SetupProcesses.RequireAsync(executable,new[]{"--creds","fsdl",password},
                    TimeSpan.FromSeconds(30),"Initialize Sunshine credentials",log);
                // This file remains per-user; never write credentials into diagnostics.
                string credentials=Path.Combine(AppPaths.Root,"sunshine-webui.txt");
                File.WriteAllText(credentials,"Sunshine local settings: https://localhost:47990\r\n"+
                    "Username: fsdl\r\nPassword: "+password+"\r\n",Encoding.UTF8);
                try {
                    var me=WindowsIdentity.GetCurrent().User!;
                    var security=new System.Security.AccessControl.FileSecurity();
                    security.SetOwner(me);
                    security.SetAccessRuleProtection(true,false);
                    security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        me,System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));
                    new FileInfo(credentials).SetAccessControl(security);
                } catch(Exception e) { log.Write("Could not restrict credentials ACL: "+e.Message);
                    throw new InvalidOperationException("Credentials ACL could not be secured; installation halted."); }
                log.Write("Created Sunshine local credentials (values omitted).");
            }
            createdDriver=await VirtualDriverSetup.EnsureAsync(log);
            await SunshineSetup.RestartAsync(log);
            string? displayId=null;
            for(int i=0;i<5 && displayId is null;i++)
            {
                displayId=SunshineSetup.FindDeviceId();
                if(displayId is null)
                {
                    log.Write("Waiting for Sunshine to enumerate the virtual display ("+(i+1)+"/5).");
                    await Task.Delay(2500);
                }
            }
            if(displayId is null)throw new InvalidOperationException(
                "Sunshine did not identify the virtual display. No monitor will be guessed or selected by display index.");
            cfg=SunshineSetup.ConfigFile()??throw new FileNotFoundException("Sunshine configuration file was not found.");
            string[] initial=File.ReadAllLines(cfg);
            string backupDir=Path.Combine(AppPaths.Root,"backups");
            Directory.CreateDirectory(backupDir);
            string backup=Path.Combine(backupDir,"sunshine.conf."+
                DateTime.Now.ToString("yyyyMMdd-HHmmss")+".bak");
            File.Copy(cfg,backup,true);
            var result=SunshineConfiguration.Apply(initial,displayId);
            changes=result.changes;
            SunshineConfiguration.WriteAtomic(cfg,result.lines);
            applied=true;
            log.Write("Configured Sunshine to capture verified virtual-display GUID "+displayId+".");
            await SunshineSetup.RestartAsync(log);

            log.Write("Preparing Android Platform Tools and verified gnirehtet runtime.");
            string outPath=Path.Combine(AppPaths.LogsDirectory,"prepare-stdout.txt"),
                errPath=Path.Combine(AppPaths.LogsDirectory,"prepare-stderr.txt");
            try {
                // The untrusted/blocked executable must fail visibly, not disappear.
                Task<CommandOutcome> preparation=SafeCommands.ExecuteAsync(AppPaths.RuntimeExe,
                    new[]{"prepare"},TimeSpan.FromMinutes(7));
                while(!preparation.IsCompleted)
                {
                    await Task.WhenAny(preparation,Task.Delay(TimeSpan.FromSeconds(20)));
                    if(!preparation.IsCompleted)log.Write("Waiting for verified Android dependencies / security review.");
                }
                var resultPrep=await preparation;
                File.WriteAllText(outPath,resultPrep.StandardOutput);
                File.WriteAllText(errPath,resultPrep.StandardError);
                if(!resultPrep.Success)throw new InvalidOperationException(
                    "USB dependency preparation failed (exit "+resultPrep.ExitCode+"): "+
                    (resultPrep.Summary.Length>400?resultPrep.Summary[^400..]:resultPrep.Summary));
            }
            catch(Exception ex)
            {
                File.WriteAllText(errPath,ex.ToString());
                throw new InvalidOperationException(
                    "USB dependency preparation could not complete. Check AVG/Defender verdict and prepare-stderr.txt. "+
                    ex.Message,ex);
            }
            // Save an offline repair package only after dependencies are ready.
            string saved=Path.Combine(AppPaths.Root,"package");
            if(!Path.GetFullPath(saved).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase))
            {
                string pending=Path.Combine(AppPaths.Root,"package.pending");
                string previous=Path.Combine(AppPaths.Root,"package.previous");
                if(Directory.Exists(pending))Directory.Delete(pending,true);
                package.CopyFilesTo(pending);
                if(Directory.Exists(previous))Directory.Delete(previous,true);
                if(Directory.Exists(saved))Directory.Move(saved,previous);
                try {Directory.Move(pending,saved);}
                catch {if(Directory.Exists(previous)&&!Directory.Exists(saved))Directory.Move(previous,saved);throw;}
                if(Directory.Exists(previous))Directory.Delete(previous,true);
            }
            CopyDesktop(package,log);
            string user=WindowsIdentity.GetCurrent().Name;
            string taskExe=Path.Combine(AppPaths.Root,"FSDL-Agent.exe");
            // User-specific login task, no PowerShell, no command shell, limited run-level.
            await SetupProcesses.RequireAsync(SetupProcesses.WindowsExe("schtasks.exe"),
                new[]{"/Create","/F","/SC","ONLOGON","/TN",SetupConstants.TaskName,
                    "/TR","\""+taskExe+"\" daemon","/RU",user,"/RL","LIMITED"},
                TimeSpan.FromSeconds(40),"Register background connection agent",log);
            await SetupProcesses.RequireAsync(SetupProcesses.WindowsExe("schtasks.exe"),
                new[]{"/Run","/TN",SetupConstants.TaskName},TimeSpan.FromSeconds(15),"Start background agent",log);
            var state=new InstalledRecord{
                Version=SetupConstants.Version,InstalledAt=DateTimeOffset.Now,
                SunshineOwned=!sunAlready || (old?.SunshineOwned??false),
                DriverOwned=createdDriver.Count>0 || (old?.DriverOwned??false),
                SunshineConfigPath=cfg,SunshineBackup=backup,SunshineChanges=changes,
                VddInstances=createdDriver.Count>0?createdDriver:(old?.VddInstances??new()),
                VddDrivers=old?.VddDrivers??new()
            };
            WriteState(state);
            RegisterPrograms(log);
            log.Write("Installation completed successfully. Android authorization and Moonlight pairing can begin.");
            return 0;
        }
        catch(Exception ex)
        {
            try {log?.Write("INSTALL FAILED: "+ex.Message);}catch{}
            // Only undo our own config keys, and only if still at our values.
            if(applied && cfg is not null && File.Exists(cfg))
            {
                try {
                    var restored=SunshineConfiguration.RestoreChangedOnly(File.ReadAllLines(cfg),changes);
                    SunshineConfiguration.WriteAtomic(cfg,restored);
                    if(log is not null)await SunshineSetup.RestartAsync(log);
                    log?.Write("Restored app-owned Sunshine configuration changes.");
                } catch(Exception e) {log?.Write("Sunshine restore requires manual review: "+e.Message);}
            }
            // Leave trusted drivers/Sunshine installed if setup fails; do not
            // delete user installations or attempt a forced driver rollback.
            try {log?.Write("Existing Sunshine and display drivers were preserved. No antivirus or trust settings were changed.");}catch{}
            return Failed;
        }
        finally {exclusive?.Dispose();}
    }

    private static bool IsAuthorized(string expected)
    {
        try {
            string actual=WindowsIdentity.GetCurrent().User?.Value??"";
            using var id=WindowsIdentity.GetCurrent();
            var p=new WindowsPrincipal(id);
            return expected.Length>5 && actual==expected &&
                p.IsInRole(WindowsBuiltInRole.Administrator);
        } catch {return false;}
    }

    private static InstalledRecord? LoadState()
    {
        try {return JsonSerializer.Deserialize<InstalledRecord>(File.ReadAllText(AppPaths.InstallState));}
        catch(IOException){return null;}
        catch(JsonException){return null;}
    }
    private static void WriteState(InstalledRecord state)
    {
        string path=AppPaths.InstallState;
        string tmp=path+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(tmp,path,true);
    }
    private static async Task StopPriorAgentAsync(SetupLog log)
    {
        foreach(string name in new[]{SetupConstants.TaskName,"FSDL USB Bridge Agent"})
        {
            // The tool returns a nonzero code when the task does not exist.
            try {await SafeCommands.ExecuteAsync(SetupProcesses.WindowsExe("schtasks.exe"),
                new[]{"/End","/TN",name},TimeSpan.FromSeconds(12));}catch{}
            try {await SafeCommands.ExecuteAsync(SetupProcesses.WindowsExe("schtasks.exe"),
                new[]{"/Delete","/TN",name,"/F"},TimeSpan.FromSeconds(12));}catch{}
        }
        if(File.Exists(AppPaths.RuntimeExe))
            try {await SafeCommands.ExecuteAsync(AppPaths.RuntimeExe,new[]{"stop"},TimeSpan.FromSeconds(15));}
            catch(Exception ex){log.Write("Existing USB stop skipped: "+ex.Message);}
    }
    private static void CopyRuntime(NativePackage package,SetupLog log)
    {
        foreach(string name in new[]{"FSDL.exe","FSDL-Agent.exe"})
        {
            string source=package.PathOf("payload/"+name),dest=Path.Combine(AppPaths.Root,name);
            if(!File.Exists(dest)||PinnedDownload.Hash(dest)!=PinnedDownload.Hash(source))
                File.Copy(source,dest,true);
        }
        string probes=Path.Combine(AppPaths.Root,"probes");
        Directory.CreateDirectory(probes);
        File.Copy(package.PathOf("payload/probes/fsdl-probe-android-arm64"),
            Path.Combine(probes,"fsdl-probe-android-arm64"),true);
        log.Write("Verified USB runtime files installed to the application directory.");
    }
    private static void CopyDesktop(NativePackage package,SetupLog log)
    {
        string src=package.PathOf("OpenTabletDisplay.exe"),dst=Path.Combine(AppPaths.Root,"OpenTabletDisplay.exe");
        if(!File.Exists(dst)||PinnedDownload.Hash(src)!=PinnedDownload.Hash(dst))
        {
            try {File.Copy(src,dst,true);}
            catch(IOException) {throw new InvalidOperationException("Close an existing installed OpenTabletDisplay window before updating.");}
        }
        string assets=Path.Combine(AppPaths.Root,"assets");
        Directory.CreateDirectory(assets);
        foreach(string ext in new[]{"ico","png"})
            File.Copy(package.PathOf("assets/OpenTabletDisplay."+ext),Path.Combine(assets,"OpenTabletDisplay."+ext),true);
        log.Write("Native Windows app and assets installed.");
    }
    private static void RegisterPrograms(SetupLog log)
    {
        try {
            using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FSDL");
            key?.SetValue("DisplayName","OpenTabletDisplay");
            key?.SetValue("DisplayVersion",SetupConstants.Version);
            key?.SetValue("Publisher","OpenTabletDisplay contributors");
            key?.SetValue("UninstallString","\""+Path.Combine(AppPaths.Root,"OpenTabletDisplay.exe")+"\" --uninstall");
            key?.SetValue("NoModify",1,RegistryValueKind.DWord);
        } catch(Exception ex) {log.Write("Windows Apps list registration skipped: "+ex.Message);}
        try
        {
            string menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"OpenTabletDisplay.lnk");
            Type type=Type.GetTypeFromProgID("WScript.Shell")??throw new InvalidOperationException("Windows shortcut COM interface unavailable.");
            dynamic shell=Activator.CreateInstance(type)!;
            dynamic shortcut=shell.CreateShortcut(menu);
            shortcut.TargetPath=Path.Combine(AppPaths.Root,"OpenTabletDisplay.exe");
            shortcut.WorkingDirectory=AppPaths.Root;
            shortcut.Description="Connect an Android device as an extended display";
            shortcut.Save();
            log.Write("Created Start Menu shortcut.");
        }
        catch(Exception ex) {log.Write("Start Menu shortcut creation skipped: "+ex.Message);}
    }

    public static void SelfTest()
    {
        var original=new[]{"encoder = nvenc","custom_option = keep","dd_resolution_option = manual"};
        var applied=SunshineConfiguration.Apply(original,"{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}");
        if(!SunshineConfiguration.Get(applied.lines,"output_name").present)
            throw new InvalidOperationException("Native Sunshine config test failed: output device absent.");
        var changed=SunshineConfiguration.Set(applied.lines,"custom_option","customized");
        var restored=SunshineConfiguration.RestoreChangedOnly(changed,applied.changes);
        if(SunshineConfiguration.Get(restored,"custom_option").value!="customized")
            throw new InvalidOperationException("Sunshine config restore modified unrelated user settings.");
        if(SunshineConfiguration.Get(restored,"output_name").present)
            throw new InvalidOperationException("Sunshine config restore failed to remove app-owned key.");
    }
}
