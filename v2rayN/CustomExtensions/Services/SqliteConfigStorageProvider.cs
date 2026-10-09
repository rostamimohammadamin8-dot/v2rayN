namespace v2rayN.CustomExtensions.Services;

public sealed class SqliteConfigStorageProvider : IConfigStorageProvider
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<SubscriptionEntry>>> _loadAsync;
    private readonly Func<SubscriptionEntry, CancellationToken, Task> _upsertAsync;
    private readonly Func<string, CancellationToken, Task> _deleteAsync;

    public SqliteConfigStorageProvider(
        Func<CancellationToken, Task<IReadOnlyList<SubscriptionEntry>>> loadAsync,
        Func<SubscriptionEntry, CancellationToken, Task> upsertAsync,
        Func<string, CancellationToken, Task> deleteAsync)
    {
        _loadAsync = loadAsync ?? throw new ArgumentNullException(nameof(loadAsync));
        _upsertAsync = upsertAsync ?? throw new ArgumentNullException(nameof(upsertAsync));
        _deleteAsync = deleteAsync ?? throw new ArgumentNullException(nameof(deleteAsync));
    }

    public Task<IReadOnlyList<SubscriptionEntry>> GetSubscriptionsAsync(
        CancellationToken cancellationToken = default) =>
        _loadAsync(cancellationToken);

    public Task UpsertSubscriptionAsync(
        SubscriptionEntry subscription,
        CancellationToken cancellationToken = default) =>
        _upsertAsync(subscription, cancellationToken);

    public Task DeleteSubscriptionAsync(string id, CancellationToken cancellationToken = default) =>
        _deleteAsync(id, cancellationToken);
}
