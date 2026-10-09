namespace v2rayN.CustomExtensions.Services;

public interface IConfigStorageProvider
{
    Task<IReadOnlyList<SubscriptionEntry>> GetSubscriptionsAsync(
        CancellationToken cancellationToken = default);

    Task UpsertSubscriptionAsync(
        SubscriptionEntry subscription,
        CancellationToken cancellationToken = default);

    Task DeleteSubscriptionAsync(
        string id,
        CancellationToken cancellationToken = default);
}
