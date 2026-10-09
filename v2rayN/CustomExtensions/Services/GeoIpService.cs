using System.Collections.Concurrent;
using System.IO;
using System.Net;
using MaxMind.GeoIP2;

namespace v2rayN.CustomExtensions.Services;

public sealed record GeoIpResult(string CountryCode, string CountryName, string FlagEmoji);

public sealed class GeoIpService : IDisposable
{
    private readonly DatabaseReader _reader;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public GeoIpService(string? databasePath = null)
    {
        databasePath ??= Path.Combine(AppContext.BaseDirectory, "dat", "GeoLite2-Country.mmdb");
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("The GeoLite2 Country database was not found.", databasePath);
        }

        _reader = new DatabaseReader(databasePath);
    }

    public async Task<GeoIpResult?> LookupAsync(
        string hostOrAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostOrAddress);
        var key = hostOrAddress.Trim();
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached.Result;
        }

        var result = await LookupCoreAsync(key, cancellationToken);
        _cache.TryAdd(key, new CacheEntry(result));
        return result;
    }

    private async Task<GeoIpResult?> LookupCoreAsync(string key, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = IPAddress.TryParse(key, out var address)
            ? [address]
            : await Dns.GetHostAddressesAsync(key, cancellationToken);

        foreach (var candidate in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_reader.TryCountry(candidate, out var response))
            {
                continue;
            }

            var country = response?.Country;
            var code = country?.IsoCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var countryName = country?.Name ?? code;
            return new GeoIpResult(code, countryName, ToFlagEmoji(code));
        }

        return null;
    }

    public static string ToFlagEmoji(string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        if (countryCode.Length != 2 || !char.IsAsciiLetter(countryCode[0]) || !char.IsAsciiLetter(countryCode[1]))
        {
            throw new ArgumentException("Country code must be a two-letter ISO 3166-1 alpha-2 code.", nameof(countryCode));
        }

        const int regionalIndicatorOffset = 0x1F1E6 - 'A';
        return string.Concat(
            char.ConvertFromUtf32(char.ToUpperInvariant(countryCode[0]) + regionalIndicatorOffset),
            char.ConvertFromUtf32(char.ToUpperInvariant(countryCode[1]) + regionalIndicatorOffset));
    }

    public void Dispose() => _reader.Dispose();

    private sealed record CacheEntry(GeoIpResult? Result);
}
