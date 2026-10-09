using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;

namespace v2rayN.CustomExtensions.Services;

public sealed class JsonConfigStorageProvider : IConfigStorageProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _configPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonConfigStorageProvider(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "guiNConfig.json");
    }

    public async Task<IReadOnlyList<SubscriptionEntry>> GetSubscriptionsAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var root = await ReadConfigAsync(cancellationToken);
            return GetSubscriptionArray(root)
                .OfType<JsonObject>()
                .Select((item, index) => ReadEntry(item, index))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertSubscriptionAsync(
        SubscriptionEntry subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var root = await ReadConfigAsync(cancellationToken);
            var subscriptions = GetSubscriptionArray(root);
            var matching = subscriptions
                .OfType<JsonObject>()
                .Where(item => string.Equals(
                    item["id"]?.GetValue<string>(),
                    subscription.Id,
                    StringComparison.Ordinal))
                .ToArray();

            var node = matching.FirstOrDefault();
            if (node is null)
            {
                node = new JsonObject();
                subscriptions.Add(node);
            }

            WriteEntry(node, subscription);
            foreach (var duplicate in matching.Skip(1))
            {
                subscriptions.Remove(duplicate);
            }

            await WriteConfigAtomicallyAsync(root, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteSubscriptionAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var root = await ReadConfigAsync(cancellationToken);
            var subscriptions = GetSubscriptionArray(root);
            var matches = subscriptions
                .OfType<JsonObject>()
                .Where(item => string.Equals(item["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length == 0)
            {
                return;
            }

            foreach (var match in matches)
            {
                subscriptions.Remove(match);
            }

            await WriteConfigAtomicallyAsync(root, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<JsonObject> ReadConfigAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_configPath))
        {
            return new JsonObject();
        }

        await using var input = File.OpenRead(_configPath);
        return await JsonNode.ParseAsync(input, cancellationToken: cancellationToken) as JsonObject
            ?? throw new InvalidDataException($"The configuration root must be a JSON object: {_configPath}");
    }

    private async Task WriteConfigAtomicallyAsync(JsonObject root, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_configPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(output, root, SerializerOptions, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _configPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static JsonArray GetSubscriptionArray(JsonObject root)
    {
        if (root["subscriptions"] is JsonArray subscriptions)
        {
            return subscriptions;
        }

        subscriptions = new JsonArray();
        root["subscriptions"] = subscriptions;
        return subscriptions;
    }

    private static SubscriptionEntry ReadEntry(JsonObject node, int index)
    {
        var url = node["url"]?.GetValue<string>() ?? string.Empty;
        var id = node["id"]?.GetValue<string>();
        return new SubscriptionEntry(
            string.IsNullOrWhiteSpace(id) ? $"legacy-{index}-{url}" : id,
            node["remarks"]?.GetValue<string>() ?? string.Empty,
            url,
            node["enabled"]?.GetValue<bool>() ?? true);
    }

    private static void WriteEntry(JsonObject node, SubscriptionEntry subscription)
    {
        node["id"] = subscription.Id;
        node["remarks"] = subscription.Remarks;
        node["url"] = subscription.Url;
        node["enabled"] = subscription.Enabled;
    }
}
