using MidiBard.RemoteControl;

namespace MidiBard.Tests.RemoteControl;

public class RemoteControlStatusMonitorTests
{
    private static RemoteControlStatusFingerprint Baseline()
        => new(
            true,
            23,
            true,
            true,
            true,
            true,
            0,
            4,
            "FFXIV",
            false);

    [Fact]
    public void InitialAndRepeatedStatusAreSilent()
    {
        var journal = new RemoteEventJournal();
        var monitor = new RemoteControlStatusMonitor(journal);
        var status = Baseline();

        monitor.Observe(status).ShouldBeFalse();
        monitor.Observe(status).ShouldBeFalse();

        journal.LatestSequence.ShouldBe(0);
        journal.GetAfter(0).ShouldBeEmpty();
    }

    [Fact]
    public void RelevantChangesPublishOneStatusInvalidationEach()
    {
        var journal = new RemoteEventJournal();
        var monitor = new RemoteControlStatusMonitor(journal);
        var current = Baseline();
        monitor.Observe(current);

        var changes = new[]
        {
            current with { PlayerLoaded = false },
            current with { ClassJobId = 24 },
            current with { InParty = false },
            current with { IsPartyLeader = false },
            current with { MonitorOnEnsemble = false },
            current with { SyncClients = false },
            current with { PlayMode = 1 },
            current with { CurrentPlaylistId = 5 },
            current with { CurrentPlaylistName = "Other" },
            current with { CurrentPlaylistIsTemporary = true },
        };

        foreach (var changed in changes)
        {
            monitor.Observe(changed).ShouldBeTrue();
            monitor.Observe(changed).ShouldBeFalse();
            current = changed;
        }

        var events = journal.GetAfter(0);
        events.Count.ShouldBe(changes.Length);
        events.All(item => item.Type == RemotePlaybackEventType.StatusChanged).ShouldBeTrue();
        events.All(item => item.PlaybackId == null).ShouldBeTrue();
        events.Select(item => item.Sequence)
            .ShouldBe(Enumerable.Range(1, changes.Length).Select(value => (long)value));
    }
}
