using ServiceLib.Models.Configs;
using ServiceLib.Services;

namespace ServiceLib.Tests.Services;

public class ProfileFailoverCoordinatorTests
{
    [Test]
    public async Task DisabledByDefaultNeverProbesOrSwitches()
    {
        var coordinator = new ProfileFailoverCoordinator();
        var settings = new ProfileFailoverSettings();
        var probeCalled = false;
        var switched = false;

        var result = await coordinator.ObserveAsync(
            settings,
            "active",
            currentProfileHealthy: false,
            ["candidate"],
            (_, _) =>
            {
                probeCalled = true;
                return Task.FromResult<IReadOnlyList<ProfileProbeResult>>([new("candidate", 10)]);
            },
            (_, _) =>
            {
                switched = true;
                return Task.CompletedTask;
            },
            DateTimeOffset.UtcNow);

        await result.Should().BeNull();
        await probeCalled.Should().BeFalse();
        await switched.Should().BeFalse();
    }

    [Test]
    public async Task RequiresConsecutiveFailuresAndSelectsFastestHealthyCandidate()
    {
        var coordinator = new ProfileFailoverCoordinator();
        var settings = new ProfileFailoverSettings
        {
            Enabled = true,
            ConsecutiveFailureThreshold = 2,
            FailureHysteresisSeconds = 30,
            CooldownSeconds = 60,
        };
        var now = DateTimeOffset.UtcNow;
        string? selectedProfile = null;
        ProfileFailoverEvent? emitted = null;
        coordinator.RouteSwitched += value => emitted = value;

        Task<IReadOnlyList<ProfileProbeResult>> Probe(IReadOnlyList<string> _, CancellationToken __) =>
            Task.FromResult<IReadOnlyList<ProfileProbeResult>>([
                new("slow", 250),
                new("fast", 45),
                new("dead", 5001),
            ]);
        Task Switch(string id, CancellationToken cancellationToken) { selectedProfile = id; return Task.CompletedTask; }

        var first = await coordinator.ObserveAsync(settings, "active", false, ["slow", "fast", "dead"], Probe, Switch, now);
        var second = await coordinator.ObserveAsync(settings, "active", false, ["slow", "fast", "dead"], Probe, Switch, now.AddSeconds(1));

        await first.Should().BeNull();
        await second.Should().NotBeNull();
        await selectedProfile.Should().BeEqualTo("fast");
        await emitted!.SelectedProfileId.Should().BeEqualTo("fast");
    }

    [Test]
    public async Task ManualOverrideAndCooldownPreventRepeatedAutomaticSwitching()
    {
        var coordinator = new ProfileFailoverCoordinator();
        var settings = new ProfileFailoverSettings
        {
            Enabled = true,
            ConsecutiveFailureThreshold = 1,
            FailureHysteresisSeconds = 30,
            CooldownSeconds = 60,
        };
        var now = DateTimeOffset.UtcNow;
        var switchCount = 0;
        Task<IReadOnlyList<ProfileProbeResult>> Probe(IReadOnlyList<string> ids, CancellationToken _) =>
            Task.FromResult<IReadOnlyList<ProfileProbeResult>>([new(ids[0], 25)]);
        Task Switch(string profileId, CancellationToken cancellationToken) { switchCount++; return Task.CompletedTask; }

        coordinator.SetManualOverride(now.AddSeconds(120));
        var overridden = await coordinator.ObserveAsync(settings, "active", false, ["next"], Probe, Switch, now);
        coordinator.ClearManualOverride();
        var switched = await coordinator.ObserveAsync(settings, "active", false, ["next"], Probe, Switch, now.AddSeconds(1));
        var duringCooldown = await coordinator.ObserveAsync(settings, "next", false, ["other"], Probe, Switch, now.AddSeconds(2));

        await overridden.Should().BeNull();
        await switched.Should().NotBeNull();
        await duringCooldown.Should().BeNull();
        await switchCount.Should().BeEqualTo(1);
    }

    [Test]
    public async Task HealthyProbeResetsConsecutiveFailureCount()
    {
        var coordinator = new ProfileFailoverCoordinator();
        var settings = new ProfileFailoverSettings
        {
            Enabled = true,
            ConsecutiveFailureThreshold = 2,
            FailureHysteresisSeconds = 30,
            CooldownSeconds = 60,
        };
        var now = DateTimeOffset.UtcNow;
        var switchCount = 0;
        Task<IReadOnlyList<ProfileProbeResult>> Probe(IReadOnlyList<string> ids, CancellationToken _) =>
            Task.FromResult<IReadOnlyList<ProfileProbeResult>>([new(ids[0], 20)]);
        Task Switch(string profileId, CancellationToken cancellationToken) { switchCount++; return Task.CompletedTask; }

        await coordinator.ObserveAsync(settings, "active", false, ["candidate"], Probe, Switch, now);
        await coordinator.ObserveAsync(settings, "active", true, ["candidate"], Probe, Switch, now.AddSeconds(1));
        var result = await coordinator.ObserveAsync(settings, "active", false, ["candidate"], Probe, Switch, now.AddSeconds(2));

        await result.Should().BeNull();
        await switchCount.Should().BeEqualTo(0);
    }
}
