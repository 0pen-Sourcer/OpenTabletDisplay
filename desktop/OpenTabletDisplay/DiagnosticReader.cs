using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTabletDisplay;

internal static class DiagnosticReader
{
    public static string BuildReport()
    {
        var report = new StringBuilder();
        report.AppendLine("OpenTabletDisplay | Local diagnostics");
        report.AppendLine($"UTC: {DateTime.UtcNow:O}");
        report.AppendLine("No diagnostic information has been uploaded.");
        report.AppendLine(new string('─', 57));
        foreach (var (name, file) in new[] {
            ("Elevated setup startup", ElevatedBootstrap.LogPath),
            ("Installation", AppPaths.InstallLog),
            ("USB dependency stdout", Path.Combine(AppPaths.LogsDirectory, "prepare-stdout.txt")),
            ("USB dependency stderr", Path.Combine(AppPaths.LogsDirectory, "prepare-stderr.txt")),
            ("USB connection stdout", Path.Combine(AppPaths.LogsDirectory, "manual-start.txt")),
            ("USB connection stderr", Path.Combine(AppPaths.LogsDirectory, "manual-start-error.txt")),
            ("Background agent", AppPaths.AgentLog)
        })
        {
            report.AppendLine().AppendLine("[" + name + "]");
            report.AppendLine(Tail(file, 65, 56 * 1024));
        }
        return report.ToString();
    }

    private static string Tail(string path, int lines, int maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return "No log file found.";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > maxBytes) stream.Seek(-maxBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string content = reader.ReadToEnd();
            return string.Join(Environment.NewLine, content.Split('\n').TakeLast(lines).Select(s => s.TrimEnd('\r')));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Unable to read log: " + ex.GetType().Name;
        }
    }

    public static string Sanitize(string report)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
            report = report.Replace(profile, "<USER_PROFILE>", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(Environment.UserName))
            report = report.Replace(Environment.UserName, "<USER>", StringComparison.OrdinalIgnoreCase);
        report = Regex.Replace(report, @"(?im)\b(password|token|secret|authorization|auth[_ -]?key)\s*[:=]\s*[^\r\n]+",
            "$1: <REDACTED>");
        report = Regex.Replace(report, @"(?im)(android enrollment:\s*)[^\r\n]+", "$1<REDACTED>");
        report = Regex.Replace(report, @"(?im)\b(serial(?:number)?\s*[:=]\s*)[^\s\r\n]+", "$1<REDACTED>");
        report = Regex.Replace(report, @"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)", "<IP_REDACTED>");
        report = Regex.Replace(report, @"(?im)\b([A-F0-9]{2}:){5}[A-F0-9]{2}\b", "<MAC_REDACTED>");
        return report;
    }

    public static void Export(string path, string raw)
    {
        var fullPath = Path.GetFullPath(path);
        File.WriteAllText(fullPath, Sanitize(raw), new UTF8Encoding(false));
    }
}
