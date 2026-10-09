namespace v2rayN.CustomExtensions.Services;

public sealed record SubscriptionEntry(
    string Id,
    string Remarks,
    string Url,
    bool Enabled = true);
