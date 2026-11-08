using System.IO.Compression;
using System.Text;
using System.Text.Json;
using v2rayN.WebAPI.Contracts;
using v2rayN.WebAPI.Services;

namespace v2rayN.WebAPI.Tests;

public class DiagnosticsServiceTests
{
    [Test]
    public async Task ArchiveRedactsCredentialsBearerTokensAndSubscriptionUrls()
    {
        const string password = "mock-password-value";
        const string secret = "mock-api-key-value";
        const string bearer = "mock-bearer-token";
        const string subscriptionUrl = "https://example.invalid/subscription?token=subscription-secret";
        var logs = new[]
        {
            new LogView(DateTimeOffset.UtcNow, "core", $"password={password}; api_key={secret}; Authorization: Bearer {bearer}"),
            new LogView(DateTimeOffset.UtcNow, "subscription", $"Updated feed {subscriptionUrl}"),
        };
        var archiveBytes = new DiagnosticsService().CreateArchive(CreateStatus(), ["core", "subscription-update:private-id"], logs);
        var report = ReadReport(archiveBytes);

        await report.Should().Contain("operatingSystem");
        await report.Should().Contain("capabilities");
        await report.Should().Contain("subscription-update");
        await report.Should().Contain("[URL REDACTED]");
        await report.Should().NotContain(password);
        await report.Should().NotContain(secret);
        await report.Should().NotContain(bearer);
        await report.Should().NotContain(subscriptionUrl);
        await report.Should().NotContain("private-id");
    }

    [Test]
    public async Task ArchiveUsesOnlyAllowlistedRuntimeFieldsAndBoundsLogOutput()
    {
        var logs = Enumerable.Range(0, 600)
            .Select(index => new LogView(DateTimeOffset.UtcNow, "core", $"entry-{index}-{new string('x', 3000)}"))
            .ToArray();
        var archiveBytes = new DiagnosticsService().CreateArchive(CreateStatus(), ["unrecognized:private"], logs);
        var report = ReadReport(archiveBytes);

        await report.Should().Contain("entry-599");
        await report.Should().NotContain("entry-0");
        await report.Should().NotContain("CurrentProfileName");
        await report.Should().NotContain("private");
        await archiveBytes.Length.Should().BeLessThan(2 * 1024 * 1024);
    }

    private static StatusView CreateStatus() => new(
        CoreRunning: true,
        CoreType: "xray",
        CurrentProfileId: "private-profile-id",
        CurrentProfileName: "private-profile-name",
        CoreStartedAt: DateTimeOffset.UtcNow,
        Listeners: [],
        XrayAvailable: true,
        Runtime: ".NET",
        ProfileCount: 1,
        SubscriptionCount: 1,
        CheckedAt: DateTimeOffset.UtcNow,
        StatisticsEnabled: true,
        Traffic: null,
        RuntimeState: "running",
        WebVersion: "1.0.4",
        GitCommit: "0123456789abcdef0123456789abcdef01234567",
        RuntimeIdentifier: "win-x64",
        Capabilities: ["events.sse", "editor.options"]);

    private static string ReadReport(byte[] archiveBytes)
    {
        using var stream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entryStream = archive.GetEntry("diagnostics.json")!.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
