using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.ServiceProcess;

namespace OpenTabletDisplay;

internal static class SetupConstants
{
    internal const string Version = "3.4.0-rc.1";
    internal const string TaskName = "FSDL USB Display Agent";
    internal const string SunshineVersion = "2026.914.233613";
    internal const string SunshineUrl = "https://github.com/LizardByte/Sunshine/releases/download/v2026.914.233613/Sunshine-Windows-AMD64-installer.msi";
    internal const string SunshineSha = "1d7fed8beecd5889dc7ff14cf9f42d6d38f37c3066c13c6c2a5f4e91847e0ccf";
    internal const string VddVersion = "25.7.23";
    internal const string VddUrl = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip";
    internal const string VddSha = "e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a";
    internal const string NefconUrl = "https://github.com/nefarius/nefcon/releases/download/v1.14.0/nefcon_v1.14.0.zip";
    internal const string NefconSha = "a15557da24a9efca203158de3b43b0eaf982db231f0194031f1ed428bc13e669";
}

internal sealed class SetupLog
{
    private readonly string _file;
    public SetupLog(string file)
    {
        _file = file;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "OpenTabletDisplay " + SetupConstants.Version + " install " +
            DateTimeOffset.Now.ToString("O") + Environment.NewLine, Encoding.UTF8);
    }
    public void Write(string value)
    {
        string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + value;
        File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8);
    }
}

internal sealed class NativePackage
{
    public string Root { get; }
    private readonly Dictionary<string,string> _hashes = new(StringComparer.OrdinalIgnoreCase);
    public static string? Discover()
    {
        string direct = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (File.Exists(Path.Combine(direct, "SHA256SUMS.txt"))) return direct;
        string cache = Path.Combine(AppPaths.Root, "package");
        if (File.Exists(Path.Combine(cache,"SHA256SUMS.txt"))) return cache;
        return null;
    }
    public NativePackage(string root)
    {
        Root = Path.GetFullPath(root);
        string manifest = Path.Combine(Root, "SHA256SUMS.txt");
        if (!File.Exists(manifest)) throw new FileNotFoundException("The extracted package is incomplete: SHA256SUMS.txt is missing.");
        foreach (string line in File.ReadAllLines(manifest))
        {
            var m = Regex.Match(line, @"\A([a-f0-9]{64})  ([^\r\n]+)\z", RegexOptions.IgnoreCase);
            if (!m.Success || !_hashes.TryAdd(m.Groups[2].Value.Replace('\\','/'), m.Groups[1].Value.ToLowerInvariant()))
                throw new InvalidDataException("Package checksum list contains an invalid or duplicate entry.");
        }
        foreach (var required in new[] { "OpenTabletDisplay.exe", "payload/FSDL.exe",
                 "payload/FSDL-Agent.exe", "payload/probes/fsdl-probe-android-arm64",
                 "assets/OpenTabletDisplay.ico", "assets/OpenTabletDisplay.png" })
            RequireVerified(required);
    }
    public string PathOf(string rel)
    {
        if (!_hashes.ContainsKey(rel)) throw new InvalidDataException("The required file isn't listed in the package: " + rel);
        string combined = Path.GetFullPath(Path.Combine(Root, rel.Replace('/',Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsafe relative package path.");
        return combined;
    }
    public void RequireVerified(string rel)
    {
        string path = PathOf(rel);
        if (!File.Exists(path)) throw new FileNotFoundException("Missing release file: " + rel);
        string got = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(got), Encoding.ASCII.GetBytes(_hashes[rel])))
            throw new InvalidDataException("File integrity check failed: " + rel);
    }
    public IEnumerable<string> Entries => _hashes.Keys;
    public void CopyFilesTo(string target)
    {
        Directory.CreateDirectory(target);
        foreach (string rel in _hashes.Keys)
        {
            RequireVerified(rel);
            string dest = Path.GetFullPath(Path.Combine(target, rel.Replace('/',Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(Path.GetFullPath(target).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsafe package destination.");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(PathOf(rel), dest, overwrite:true);
        }
        File.Copy(Path.Combine(Root,"SHA256SUMS.txt"),Path.Combine(target,"SHA256SUMS.txt"),true);
    }
}

internal static class PinnedDownload
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = true })
        { Timeout = TimeSpan.FromMinutes(6) };

    public static async Task<string> FetchAsync(string url,string sha,string dest,SetupLog log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (File.Exists(dest) && Hash(dest).Equals(sha,StringComparison.OrdinalIgnoreCase))
        {
            log.Write("Verified cached download: "+Path.GetFileName(dest));
            return dest;
        }
        var part = dest + ".part";
        if(File.Exists(part)) File.Delete(part);
        log.Write("Downloading verified upstream component: " + Path.GetFileName(dest));
        try
        {
            using var result = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            result.EnsureSuccessStatusCode();
            if (result.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Download was redirected to an insecure address.");
            await using(var target=File.Create(part)) await result.Content.CopyToAsync(target);
            if(!Hash(part).Equals(sha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pinned component SHA-256 mismatch: "+Path.GetFileName(dest));
            File.Move(part,dest,true);
            return dest;
        }
        catch
        {
            if(File.Exists(part))File.Delete(part);
            throw;
        }
    }
    public static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
}

internal static class WindowsTrust
{
    private static readonly Guid ActionVerify = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct FileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct TrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
    [DllImport("wintrust.dll", CharSet=CharSet.Unicode, PreserveSig=true)]
    private static extern int WinVerifyTrust(IntPtr hwnd,ref Guid action,ref TrustData data);
    public static void RequireSigned(string file)
    {
        var info=new FileInfo { cbStruct=(uint)Marshal.SizeOf<FileInfo>(), pcwszFilePath=file };
        IntPtr ptr=Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(info,ptr,false);
            var data=new TrustData {
                cbStruct=(uint)Marshal.SizeOf<TrustData>(),
                dwUIChoice=2, // no UI
                fdwRevocationChecks=1, // check the entire certificate chain
                dwUnionChoice=1, // file
                pFile=ptr,
                dwProvFlags=0x40 // WTD_REVOCATION_CHECK_CHAIN; allow fresh online revocation data
            };
            Guid g=ActionVerify;
            int status=WinVerifyTrust(IntPtr.Zero,ref g,ref data);
            if(status!=0) throw new InvalidDataException(
                "Windows cannot verify the publisher signature of "+Path.GetFileName(file)+
                " (0x"+status.ToString("X8")+"). No trust exceptions were added.");
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}

internal static class SetupProcesses
{
    public static async Task<CommandOutcome> RequireAsync(string exe,IEnumerable<string> args,
        TimeSpan timeout,string operation,SetupLog log)
    {
        log.Write("Starting: "+operation);
        Task<CommandOutcome> task=SafeCommands.ExecuteAsync(exe,args,timeout);
        while(!task.IsCompleted)
        {
            await Task.WhenAny(task,Task.Delay(TimeSpan.FromSeconds(18)));
            if(!task.IsCompleted) log.Write("In progress: "+operation);
        }
        var result=await task;
        string details=(result.StandardError+"\n"+result.StandardOutput).Trim();
        if(!string.IsNullOrEmpty(details)) log.Write(operation+" result: "+(details.Length>1600?details[^1600..]:details).Replace("\n"," | ").Replace("\r",""));
        if(!result.Success) throw new InvalidOperationException(operation+" failed (exit "+result.ExitCode+"). See diagnostics.");
        return result;
    }
    public static string WindowsExe(string name)=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),name);
}

internal static class SunshineSetup
{
    public static bool HasService()
    {
        try {using var s = new ServiceController("SunshineService");var x=s.Status;return true;}
        catch(InvalidOperationException){return false;}
    }
    public static string? Executable()
    {
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Sunshine");
        if(!Directory.Exists(root))return null;
        var direct=Path.Combine(root,"sunshine.exe");
        if(File.Exists(direct))return direct;
        return Directory.EnumerateFiles(root,"sunshine.exe",SearchOption.AllDirectories).FirstOrDefault();
    }
    public static string? ConfigFile()
    {
        string d=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Sunshine","config");
        if(!Directory.Exists(d))return null;
        var direct=Path.Combine(d,"sunshine.conf");
        return File.Exists(direct)?direct:Directory.EnumerateFiles(d,"*.conf",SearchOption.TopDirectoryOnly).FirstOrDefault();
    }
    public static async Task WaitHealthyAsync(SetupLog log)
    {
        using var http=new HttpClient { Timeout=TimeSpan.FromSeconds(2) };
        for(int i=0;i<20;i++)
        {
            try {using var res=await http.GetAsync("http://127.0.0.1:47989/serverinfo");
                if(res.IsSuccessStatusCode)return;}
            catch(HttpRequestException){}
            catch(TaskCanceledException){}
            await Task.Delay(2000);
        }
        throw new InvalidOperationException("Sunshine service did not become responsive at /serverinfo.");
    }
    public static async Task RestartAsync(SetupLog log)
    {
        using var s=new ServiceController("SunshineService");
        s.Refresh();
        if(s.Status!=ServiceControllerStatus.Stopped)
        {
            if(s.CanStop) { s.Stop(); s.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30)); }
            else throw new InvalidOperationException("Sunshine service cannot be stopped safely.");
        }
        s.Start();
        s.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));
        await WaitHealthyAsync(log);
    }
    public static string? FindDeviceId()
    {
        var roots=new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Sunshine"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Sunshine")
        };
        var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regex=new Regex("\"device_id\"\\s*:\\s*\"(?<id>\\{[a-f0-9-]{36}\\})\".{0,700}?\"friendly_name\"\\s*:\\s*\"(?<name>[^\"]*)\"",
            RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromSeconds(1));
        foreach(string root in roots)
        {
            if(!Directory.Exists(root))continue;
            IEnumerable<string> logs;
            try {logs=Directory.EnumerateFiles(root,"*sunshine*.log",SearchOption.AllDirectories).Take(20).ToArray();}
            catch(UnauthorizedAccessException){continue;}
            foreach(string path in logs)
            {
                try {
                    var info=new FileInfo(path);
                    using var fs=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                    if(fs.Length>256*1024)fs.Seek(-256*1024,SeekOrigin.End);
                    using var reader=new StreamReader(fs);
                    var text=reader.ReadToEnd();
                    foreach(Match m in regex.Matches(text))
                        if(Regex.IsMatch(m.Groups["name"].Value,@"virtual\s*display|virtual.*driver|idd\s*hdr|mtt",RegexOptions.IgnoreCase))
                            ids.Add(m.Groups["id"].Value);
                }
                catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
        }
        if(ids.Count>1) throw new InvalidOperationException("Multiple virtual displays were found in Sunshine logs. Choose the correct device explicitly before continuing.");
        return ids.Count==1?ids.First():null;
    }
}
