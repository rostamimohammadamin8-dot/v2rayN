using v2rayN.CustomExtensions.Services;
using ServiceLib.Common;
using ServiceLib.Handler;
using ServiceLib.Helper;
using ServiceLib.Manager;
using ServiceLib.Models.Entities;

namespace v2rayN.CustomExtensions;

public static class CustomExtensionBootstrap
{
    public static async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var storage = new SqliteConfigStorageProvider(
            LoadSubscriptionsAsync,
            UpsertSubscriptionAsync,
            DeleteSubscriptionAsync);
        var result = await CustomBuiltinHandler.EnsureSubscriptionAsync(storage, cancellationToken);

        if (result.Added)
        {
            _ = UpdateNewSubscriptionInBackgroundAsync(result.Subscription.Id);
        }
    }

    private static async Task<IReadOnlyList<SubscriptionEntry>> LoadSubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = await AppManager.Instance.SubItems();
        return items?
            .Select(item => new SubscriptionEntry(item.Id, item.Remarks, item.Url, item.Enabled))
            .ToArray() ?? [];
    }

    private static async Task UpsertSubscriptionAsync(
        SubscriptionEntry subscription,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = await AppManager.Instance.SubItems();
        var item = items?.FirstOrDefault(existing => existing.Id == subscription.Id);
        if (item is null)
        {
            item = new SubItem
            {
                Id = subscription.Id,
                Remarks = subscription.Remarks,
                Url = subscription.Url,
                MoreUrl = string.Empty,
                Enabled = subscription.Enabled,
                UserAgent = string.Empty,
                Sort = (items?.Count ?? 0) + 1
            };
        }
        else
        {
            item.Remarks = subscription.Remarks;
            item.Url = subscription.Url;
            item.Enabled = subscription.Enabled;
        }

        await SQLiteHelper.Instance.ReplaceAsync(item);
    }

    private static async Task DeleteSubscriptionAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = await AppManager.Instance.GetSubItem(id);
        if (item is not null)
        {
            await SQLiteHelper.Instance.DeleteAsync(item);
        }
    }

    private static async Task UpdateNewSubscriptionInBackgroundAsync(string subscriptionId)
    {
        try
        {
            await Task.Run(() => SubscriptionHandler.UpdateProcess(
                AppManager.Instance.Config,
                subscriptionId,
                false,
                static (_, _) => Task.CompletedTask));
        }
        catch (Exception exception)
        {
            Logging.SaveLog("CustomExtension.SubscriptionUpdate", exception);
        }
    }
}
