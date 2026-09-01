using MidiBard.RemoteControl;

namespace MidiBard.Tests.RemoteControl;

public class RemotePlaybackLifecycleTests
{
    [Fact]
    public void LoadCreatesReadyPlaybackWithOpaqueHandle()
    {
        var lifecycle = new RemotePlaybackLifecycle();

        var loaded = lifecycle.OnPlaybackLoaded("/music/Frog's Theme.mid", 123456);

        loaded.PlaybackId.ShouldNotBeNull();
        loaded.FileName.ShouldBe("Frog's Theme.mid");
        loaded.DurationMs.ShouldBe(123456);
        loaded.State.ShouldBe(RemotePlaybackState.Ready);
        lifecycle.IsCurrent(loaded.PlaybackId!.Value).ShouldBeTrue();
    }

    [Fact]
    public void ReplacingActivePlaybackStopsOldHandleAndCreatesNewHandle()
    {
        var lifecycle = new RemotePlaybackLifecycle();
        var first = lifecycle.OnPlaybackLoaded("/music/First.mid", 1000);
        lifecycle.OnPlaybackStarted();

        var second = lifecycle.OnPlaybackLoaded("/music/Second.mid", 2000);

        second.PlaybackId.ShouldNotBe(first.PlaybackId);
        var events = lifecycle.Events.GetAfter(0);
        events.Select(item => item.Type).ShouldBe(new[]
        {
            RemotePlaybackEventType.PlaybackStarted,
            RemotePlaybackEventType.PlaybackStopped,
        });
        events[1].PlaybackId.ShouldBe(first.PlaybackId!.Value);
    }

    [Fact]
    public void NaturalCompletionIsNotTurnedIntoPauseByEnsembleCleanup()
    {
        var lifecycle = new RemotePlaybackLifecycle();
        var loaded = lifecycle.OnPlaybackLoaded("/music/Test.mid", 1000);
        lifecycle.OnPlaybackStarted();
        lifecycle.OnPlaybackCompleted(loaded.PlaybackId!.Value);
        lifecycle.OnEnsembleStopped();

        lifecycle.OnPlaybackPaused();

        lifecycle.GetSnapshot().State.ShouldBe(RemotePlaybackState.Completed);
        lifecycle.Events.GetAfter(0).Select(item => item.Type).ShouldBe(new[]
        {
            RemotePlaybackEventType.PlaybackStarted,
            RemotePlaybackEventType.PlaybackCompleted,
            RemotePlaybackEventType.EnsembleStopped,
        });
        lifecycle.IsCurrent(loaded.PlaybackId!.Value).ShouldBeTrue();
    }

    [Fact]
    public void ExplicitStopClearsHandleAndNeverReportsNaturalCompletion()
    {
        var lifecycle = new RemotePlaybackLifecycle();
        var loaded = lifecycle.OnPlaybackLoaded("/music/Test.mid", 1000);
        lifecycle.OnPlaybackStarted();

        lifecycle.OnPlaybackStopped();

        lifecycle.GetSnapshot().State.ShouldBe(RemotePlaybackState.Idle);
        lifecycle.GetSnapshot().PlaybackId.ShouldBeNull();
        lifecycle.Events.GetAfter(0).Select(item => item.Type).ShouldBe(new[]
        {
            RemotePlaybackEventType.PlaybackStarted,
            RemotePlaybackEventType.PlaybackStopped,
        });
    }

    [Fact]
    public void EventSequenceIsStrictlyIncreasingAndCanBeReadFromCursor()
    {
        var lifecycle = new RemotePlaybackLifecycle();
        var loaded = lifecycle.OnPlaybackLoaded("/music/Test.mid", 1000);
        lifecycle.OnPlaybackStarted();
        lifecycle.OnEnsembleStarted();
        lifecycle.OnPlaybackCompleted(loaded.PlaybackId!.Value);

        var all = lifecycle.Events.GetAfter(0);
        all.Select(item => item.Sequence).ShouldBe(new long[] { 1, 2, 3 });

        var later = lifecycle.Events.GetAfter(1);
        later.Select(item => item.Type).ShouldBe(new[]
        {
            RemotePlaybackEventType.EnsembleStarted,
            RemotePlaybackEventType.PlaybackCompleted,
        });
    }

    [Fact]
    public void EventJournalReportsWhenRequestedHistoryWasDiscarded()
    {
        var journal = new RemoteEventJournal(capacity: 2);
        var playbackId = Guid.NewGuid();

        journal.Publish(playbackId, RemotePlaybackEventType.PlaybackStarted);
        journal.Publish(playbackId, RemotePlaybackEventType.PlaybackPaused);
        journal.Publish(playbackId, RemotePlaybackEventType.PlaybackStarted);

        Should.Throw<RemoteEventHistoryLostException>(() => journal.GetAfter(0));
        journal.GetAfter(1).Count.ShouldBe(2);
    }
}


    [Fact]
    public void LateCompletionFromReplacedPlaybackCannotCompleteCurrentPlayback()
    {
        var lifecycle = new RemotePlaybackLifecycle();
        var first = lifecycle.OnPlaybackLoaded("/music/First.mid", 1000);
        lifecycle.OnPlaybackStarted();
        var second = lifecycle.OnPlaybackLoaded("/music/Second.mid", 2000);

        var accepted = lifecycle.OnPlaybackCompleted(first.PlaybackId!.Value);

        accepted.ShouldBeFalse();
        lifecycle.GetSnapshot().PlaybackId.ShouldBe(second.PlaybackId);
        lifecycle.GetSnapshot().State.ShouldBe(RemotePlaybackState.Ready);
        lifecycle.Events.GetAfter(0)
            .Select(item => item.Type)
            .ShouldNotContain(RemotePlaybackEventType.PlaybackCompleted);
    }

    [Fact]
    public void EnsembleStopRemainsAssociatedWithPlaybackThatStartedEnsemble()
    {
        var lifecycle = new RemotePlaybackLifecycle();
        var first = lifecycle.OnPlaybackLoaded("/music/First.mid", 1000);
        lifecycle.OnEnsembleStarted();
        var second = lifecycle.OnPlaybackLoaded("/music/Second.mid", 2000);

        lifecycle.OnEnsembleStopped();

        var stop = lifecycle.Events.GetAfter(0)
            .Single(item => item.Type == RemotePlaybackEventType.EnsembleStopped);
        stop.PlaybackId.ShouldBe(first.PlaybackId!.Value);
        stop.PlaybackId.ShouldNotBe(second.PlaybackId!.Value);
    }
