using ServiceLib.Models.Configs;

namespace ServiceLib.Services;

public sealed record ProfileProbeResult(string ProfileId, double? LatencyMilliseconds);

public sealed record ProfileFailoverEvent(
    string PreviousProfileId,
    string SelectedProfileId,
    double LatencyMilliseconds,
    DateTimeOffset SwitchedAt);

public sealed class ProfileFailoverCoordinator
{
    private const double MaximumAcceptedLatencyMilliseconds = 5000;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _overrideGate = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _lastFailureAt;
    private DateTimeOffset? _lastSwitchAt;
    private DateTimeOffset? _manualOverrideUntil;

    public event Action<ProfileFailoverEvent>? RouteSwitched;

    public void SetManualOverride(DateTimeOffset until)
    {
        lock (_overrideGate)
        {
            _manualOverrideUntil = until;
        }
    }

    public void ClearManualOverride()
    {
        lock (_overrideGate)
        {
            _manualOverrideUntil = null;
        }
    }

    public async Task<ProfileFailoverEvent?> ObserveAsync(
        ProfileFailoverSettings settings,
        string currentProfileId,
        bool currentProfileHealthy,
        IReadOnlyList<string> candidateProfileIds,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ProfileProbeResult>>> probeCandidatesAsync,
        Func<string, CancellationToken, Task> switchProfileAsync,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentProfileId);
        ArgumentNullException.ThrowIfNull(candidateProfileIds);
        ArgumentNullException.ThrowIfNull(probeCandidatesAsync);
        ArgumentNullException.ThrowIfNull(switchProfileAsync);
        ValidateSettings(settings);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (currentProfileHealthy)
            {
                _consecutiveFailures = 0;
                _lastFailureAt = null;
                return null;
            }

            if (!settings.Enabled)
            {
                return null;
            }

            if (_lastFailureAt is null || now - _lastFailureAt > TimeSpan.FromSeconds(settings.FailureHysteresisSeconds))
            {
                _consecutiveFailures = 0;
            }
            _consecutiveFailures++;
            _lastFailureAt = now;
            if (_consecutiveFailures < settings.ConsecutiveFailureThreshold
                || _lastSwitchAt is { } lastSwitch && now - lastSwitch < TimeSpan.FromSeconds(settings.CooldownSeconds)
                || IsManualOverrideActive(now))
            {
                return null;
            }

            var candidates = candidateProfileIds
                .Where(id => !string.IsNullOrWhiteSpace(id) && !string.Equals(id, currentProfileId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                return null;
            }

            var results = await probeCandidatesAsync(candidates, cancellationToken);
            var selected = results
                .Where(result => candidates.Contains(result.ProfileId, StringComparer.Ordinal)
                    && result.LatencyMilliseconds is >= 0 and <= MaximumAcceptedLatencyMilliseconds)
                .OrderBy(result => result.LatencyMilliseconds)
                .FirstOrDefault();
            if (selected?.LatencyMilliseconds is not double latency)
            {
                return null;
            }

            await switchProfileAsync(selected.ProfileId, cancellationToken);
            var switched = new ProfileFailoverEvent(currentProfileId, selected.ProfileId, latency, now);
            _lastSwitchAt = now;
            _consecutiveFailures = 0;
            _lastFailureAt = null;
            RouteSwitched?.Invoke(switched);
            return switched;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsManualOverrideActive(DateTimeOffset now)
    {
        lock (_overrideGate)
        {
            if (_manualOverrideUntil is not { } until)
            {
                return false;
            }
            if (until <= now)
            {
                _manualOverrideUntil = null;
                return false;
            }
            return true;
        }
    }

    private static void ValidateSettings(ProfileFailoverSettings settings)
    {
        if (settings.ConsecutiveFailureThreshold is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Failure threshold must be between 1 and 20.");
        }
        if (settings.FailureHysteresisSeconds is < 1 or > 3600)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Failure hysteresis must be between 1 and 3600 seconds.");
        }
        if (settings.CooldownSeconds is < 1 or > 86400)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Failover cooldown must be between 1 and 86400 seconds.");
        }
    }
}
