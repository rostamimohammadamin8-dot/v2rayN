using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using v2rayN.CustomExtensions.Services;
#if NET10_0_WINDOWS10_0_19041_0
using ServiceLib.ViewModels;
#endif

namespace v2rayN.CustomExtensions.Extensions;

public static class MainWindowExtension
{
    public static void MinimizeToTrayOnClose(Window window, Action hideToTray)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(hideToTray);
        window.Closing += (_, args) =>
        {
            args.Cancel = true;
            hideToTray();
        };
    }

    public static async Task FilterCountryAsync<TItem>(
        DataGrid grid,
        IEnumerable<TItem> source,
        Func<TItem, string?> getCountryCode,
        string? countryCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(getCountryCode);

        await grid.Dispatcher.InvokeAsync(() =>
        {
            var items = source.ToArray();
            var collectionView = new ListCollectionView(items);
            if (!string.IsNullOrWhiteSpace(countryCode))
            {
                collectionView.Filter = item =>
                    item is TItem typedItem &&
                    string.Equals(
                        getCountryCode(typedItem),
                        countryCode,
                        StringComparison.OrdinalIgnoreCase);
            }

            grid.ItemsSource = collectionView;
        }, DispatcherPriority.DataBind, cancellationToken);
    }

    public static async Task<bool> ConnectToLowestLatencyAsync<TNode>(
        Dispatcher dispatcher,
        IEnumerable<PingResult<TNode>> results,
        Func<TNode, Task> connectAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(connectAsync);

        var best = await dispatcher.InvokeAsync(
            () => results
                .Where(result => result.IsAlive && result.LatencyMilliseconds is >= 0 and <= 5000)
                .MinBy(result => result.LatencyMilliseconds),
            DispatcherPriority.Normal,
            cancellationToken);
        if (best is null)
        {
            return false;
        }

        var connection = await dispatcher.InvokeAsync(
            () => connectAsync(best.Node),
            DispatcherPriority.Normal,
            cancellationToken);
        await connection;
        return true;
    }

#if NET10_0_WINDOWS10_0_19041_0
    public static Task<bool> ConnectToLowestLatencyProfileAsync<TNode>(
        Window window,
        IEnumerable<PingResult<TNode>> results,
        Func<TNode, string?> getProfileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(getProfileId);
        if (window.DataContext is not MainWindowViewModel viewModel)
        {
            throw new InvalidOperationException(
                "The window DataContext must be the native v2rayN MainWindowViewModel.");
        }

        return ConnectToLowestLatencyAsync(
            window.Dispatcher,
            results,
            node =>
            {
                var profileId = getProfileId(node);
                if (string.IsNullOrWhiteSpace(profileId))
                {
                    throw new InvalidOperationException("The selected profile does not have an index ID.");
                }

                return viewModel.ProfilesViewModel.SetDefaultServer(profileId);
            },
            cancellationToken);
    }
#endif
}
