using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using v2rayN.CustomExtensions.Services;

namespace v2rayN.CustomExtensions.Tests;

public class CustomExtensionTests
{
    [Test]
    public async Task EnsureSubscription_IsIdempotentAndDeduplicatesUrlAndRemarkMatches()
    {
        var storage = new MemoryStorage(
        [
            new SubscriptionEntry("url-match", "Legacy name", CustomBuiltinHandler.SubscriptionUrl),
            new SubscriptionEntry("remark-match", CustomBuiltinHandler.SubscriptionRemarks, "https://old.example/sub")
        ]);

        var first = await CustomBuiltinHandler.EnsureSubscriptionAsync(storage);
        var second = await CustomBuiltinHandler.EnsureSubscriptionAsync(storage);

        if (first.Added || second.Added || storage.Subscriptions.Count != 1 ||
            storage.Subscriptions[0].Url != CustomBuiltinHandler.SubscriptionUrl ||
            storage.Subscriptions[0].Remarks != CustomBuiltinHandler.SubscriptionRemarks)
        {
            throw new InvalidOperationException("Subscription injection was not idempotent or did not deduplicate both identity forms.");
        }
    }

    [Test]
    public async Task JsonStorage_PreservesOtherConfigurationAndWritesSubscription()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"v2rayn-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "guiNConfig.json");
        await File.WriteAllTextAsync(path, """{"theme":"dark","subscriptions":[]}""");

        try
        {
            var storage = new JsonConfigStorageProvider(path);
            await CustomBuiltinHandler.EnsureSubscriptionAsync(storage);
            var entries = await storage.GetSubscriptionsAsync();
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(path));

            if (entries.Count != 1 ||
                config.RootElement.GetProperty("theme").GetString() != "dark" ||
                entries[0].Url != CustomBuiltinHandler.SubscriptionUrl)
            {
                throw new InvalidOperationException("JSON storage did not preserve the existing config or write the subscription.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public Task FlagConverter_ReturnsUnicodeRegionalIndicatorFlags()
    {
        if (GeoIpService.ToFlagEmoji("US") != "🇺🇸" ||
            GeoIpService.ToFlagEmoji("ir") != "🇮🇷")
        {
            throw new InvalidOperationException("Country code conversion returned an incorrect flag.");
        }

        return Task.CompletedTask;
    }

    [Test]
    public async Task PingService_OrdersLiveNodesAndMarksFailedNodesOffline()
    {
        using var client = new HttpClient(new StaticResponseHandler());
        using var service = new PingService(client);
        var results = await service.MeasureAsync(
            new[] { "slow", "offline", "fast" },
            node => new Uri($"https://probe.invalid/{node}"));

        if (results.Count != 3 ||
            results[0].Node != "fast" ||
            results[1].Node != "slow" ||
            results[2].Node != "offline" ||
            results[2].IsAlive)
        {
            throw new InvalidOperationException("Ping measurements were not sorted or offline nodes were not marked.");
        }
    }

    private sealed class MemoryStorage(IEnumerable<SubscriptionEntry> entries) : IConfigStorageProvider
    {
        public List<SubscriptionEntry> Subscriptions { get; } = entries.ToList();

        public Task<IReadOnlyList<SubscriptionEntry>> GetSubscriptionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionEntry>>(Subscriptions.ToArray());

        public Task UpsertSubscriptionAsync(
            SubscriptionEntry subscription,
            CancellationToken cancellationToken = default)
        {
            Subscriptions.RemoveAll(entry => entry.Id == subscription.Id);
            Subscriptions.Add(subscription);
            return Task.CompletedTask;
        }

        public Task DeleteSubscriptionAsync(string id, CancellationToken cancellationToken = default)
        {
            Subscriptions.RemoveAll(entry => entry.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/slow")
            {
                await Task.Delay(30, cancellationToken);
            }

            var response = path switch
            {
                "/fast" => HttpStatusCode.NoContent,
                "/slow" => HttpStatusCode.OK,
                _ => HttpStatusCode.ServiceUnavailable
            };
            return new HttpResponseMessage(response);
        }
    }
}
