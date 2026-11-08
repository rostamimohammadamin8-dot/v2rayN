using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using v2rayN.WebAPI.Contracts;

namespace v2rayN.WebAPI.Services;

public sealed class DiagnosticsService
{
    private const int MaximumLogEntries = 500;
    private const int MaximumLogMessageCharacters = 2048;
    private const int MaximumArchiveBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private static readonly Regex SecretAssignment = new(
        @"(?i)\b(password|passwd|pass|token|secret|api[-_]?key|authorization)\s*[:=]\s*(?:Bearer\s+)?(?:""[^""]*""|'[^']*'|[^\s,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BearerCredential = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/\-=]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Url = new(
        @"(?i)\b(?:https?|vmess|vless|trojan|ss|ssr|socks5|tuic|hysteria2|wireguard)://\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AbsolutePath = new(
        @"(?i)(?:[a-z]:\\(?:[^\\\s""']+\\)*[^\\\s""']+|/(?:home|Users|var|tmp|etc)/[^\s""']+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public byte[] CreateArchive(
        StatusView status,
        IReadOnlyList<string> activeOperations,
        IReadOnlyList<LogView> recentLogs)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(activeOperations);
        ArgumentNullException.ThrowIfNull(recentLogs);

        var report = new DiagnosticsReport(
            DateTimeOffset.UtcNow,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            status.RuntimeIdentifier,
            status.WebVersion,
            status.GitCommit,
            status.CoreRunning,
            NormalizeRuntimeState(status.RuntimeState),
            NormalizeCapabilities(status.Capabilities),
            NormalizeOperations(activeOperations),
            recentLogs.TakeLast(MaximumLogEntries).Select(SanitizeLog).ToArray());

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            JsonSerializer.Serialize(entryStream, report, JsonOptions);
        }

        if (output.Length > MaximumArchiveBytes)
        {
            throw new InvalidOperationException("The generated diagnostics archive exceeded its size limit.");
        }
        return output.ToArray();
    }

    private static string NormalizeRuntimeState(string? state) =>
        state is "stopped" or "starting" or "running" or "stopping" or "failed"
            ? state
            : "unknown";

    private static string[] NormalizeCapabilities(IEnumerable<string>? capabilities) =>
        capabilities?
            .Where(value => Regex.IsMatch(value, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray()
        ?? [];

    private static string[] NormalizeOperations(IEnumerable<string> operations) =>
        operations.Select(operation => operation switch
            {
                "core" => "core",
                "speedtest" => "speedtest",
                _ when operation.StartsWith("core-update", StringComparison.Ordinal) => "core-update",
                _ when operation.StartsWith("subscription", StringComparison.Ordinal) => "subscription-update",
                _ => null,
            })
            .Where(operation => operation is not null)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(operation => operation!)
            .ToArray();

    private static DiagnosticsLogEntry SanitizeLog(LogView log)
    {
        var source = Regex.IsMatch(log.Source, "^[a-zA-Z][a-zA-Z0-9-]{0,31}$", RegexOptions.CultureInvariant)
            ? log.Source
            : "other";
        var message = log.Message;
        message = Url.Replace(message, "[URL REDACTED]");
        message = SecretAssignment.Replace(message, "$1=[REDACTED]");
        message = BearerCredential.Replace(message, "Bearer [REDACTED]");
        message = AbsolutePath.Replace(message, "[PATH REDACTED]");
        if (message.Length > MaximumLogMessageCharacters)
        {
            message = message[..MaximumLogMessageCharacters] + "… [truncated]";
        }
        return new DiagnosticsLogEntry(log.Timestamp, source, message);
    }

    private sealed record DiagnosticsReport(
        DateTimeOffset GeneratedAt,
        string OperatingSystem,
        string Runtime,
        string? RuntimeIdentifier,
        string? ApplicationVersion,
        string? SourceCommit,
        bool CoreRunning,
        string RuntimeState,
        string[] Capabilities,
        string[] ActiveOperations,
        DiagnosticsLogEntry[] RecentLogs);

    private sealed record DiagnosticsLogEntry(DateTimeOffset Timestamp, string Source, string Message);
}
