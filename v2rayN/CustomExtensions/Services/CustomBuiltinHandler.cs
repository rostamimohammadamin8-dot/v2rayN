namespace v2rayN.CustomExtensions.Services;

public sealed record SubscriptionEnsureResult(SubscriptionEntry Subscription, bool Added);

public static class CustomBuiltinHandler
{
    public const string SubscriptionUrl =
        "https://raw.githubusercontent.com/rostamimohammadamin8-dot/Free-VPN-Config-Pack-2026/main/sub.txt";

    public const string SubscriptionRemarks = "\u26A1 Free Config Pack";

    public static async Task<SubscriptionEnsureResult> EnsureSubscriptionAsync(
        IConfigStorageProvider storage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var subscriptions = await storage.GetSubscriptionsAsync(cancellationToken);
        var matches = subscriptions
            .Where(item =>
                string.Equals(item.Url.Trim(), SubscriptionUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Remarks.Trim(), SubscriptionRemarks, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => string.Equals(item.Url, SubscriptionUrl, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var added = matches.Length == 0;
        var canonical = added
            ? new SubscriptionEntry(Guid.NewGuid().ToString("N"), SubscriptionRemarks, SubscriptionUrl)
            : matches[0] with
            {
                Remarks = SubscriptionRemarks,
                Url = SubscriptionUrl
            };

        if (added || canonical != matches[0])
        {
            await storage.UpsertSubscriptionAsync(canonical, cancellationToken);
        }

        foreach (var duplicate in matches.Skip(1))
        {
            await storage.DeleteSubscriptionAsync(duplicate.Id, cancellationToken);
        }

        return new SubscriptionEnsureResult(canonical, added);
    }
}
