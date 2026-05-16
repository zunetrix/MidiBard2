using MidiBard.Control.MidiControl.Preview;

namespace MidiBard.Tests.Control.MidiControl.Preview;

public class MidiEditorPreviewSchedulerStateTests
{
    [Fact]
    public void Schedule_TracksPendingGroupAndRemovesAfterCallback()
    {
        var scheduler = new ManualScheduler();
        using var state = new MidiEditorPreviewSchedulerState(scheduler, ownsScheduler: false);
        var callbacks = 0;

        state.Schedule(
            MidiEditorPreviewScheduleGroup.CompensatedEvent,
            TimeSpan.Zero,
            () => callbacks++);

        state.PendingCount.ShouldBe(1);
        state.GetPendingCount(MidiEditorPreviewScheduleGroup.CompensatedEvent).ShouldBe(1);

        scheduler.RunNext();

        callbacks.ShouldBe(1);
        state.PendingCount.ShouldBe(0);
        state.GetPendingCount(MidiEditorPreviewScheduleGroup.CompensatedEvent).ShouldBe(0);
    }

    [Fact]
    public void CancelGroup_CancelsOnlyMatchingGroupAndInvalidatesThatGroupVersion()
    {
        var scheduler = new ManualScheduler();
        using var state = new MidiEditorPreviewSchedulerState(scheduler, ownsScheduler: false);
        var compensatedVersion = state.GetVersion(MidiEditorPreviewScheduleGroup.CompensatedEvent);
        var cleanupVersion = state.GetVersion(MidiEditorPreviewScheduleGroup.RetainedSoundCleanup);
        var callbacks = new List<string>();

        state.Schedule(
            MidiEditorPreviewScheduleGroup.CompensatedEvent,
            TimeSpan.Zero,
            () => callbacks.Add("compensated"));
        state.Schedule(
            MidiEditorPreviewScheduleGroup.RetainedSoundCleanup,
            TimeSpan.Zero,
            () => callbacks.Add("cleanup"));

        var nextVersion = state.CancelGroup(MidiEditorPreviewScheduleGroup.CompensatedEvent);

        state.IsCurrent(MidiEditorPreviewScheduleGroup.CompensatedEvent, compensatedVersion).ShouldBeFalse();
        state.GetVersion(MidiEditorPreviewScheduleGroup.CompensatedEvent).ShouldBe(nextVersion);
        state.IsCurrent(MidiEditorPreviewScheduleGroup.RetainedSoundCleanup, cleanupVersion).ShouldBeTrue();
        state.GetPendingCount(MidiEditorPreviewScheduleGroup.CompensatedEvent).ShouldBe(0);
        state.GetPendingCount(MidiEditorPreviewScheduleGroup.RetainedSoundCleanup).ShouldBe(1);

        scheduler.RunAll();

        callbacks.ShouldBe(new[] { "cleanup" });
        state.PendingCount.ShouldBe(0);
    }

    [Fact]
    public void DisposingReturnedHandle_RemovesPendingSchedule()
    {
        var scheduler = new ManualScheduler();
        using var state = new MidiEditorPreviewSchedulerState(scheduler, ownsScheduler: false);
        var callbacks = 0;

        var scheduled = state.Schedule(
            MidiEditorPreviewScheduleGroup.SameOnsetRoll,
            TimeSpan.Zero,
            () => callbacks++);

        scheduled.Dispose();
        scheduler.RunAll();

        callbacks.ShouldBe(0);
        state.PendingCount.ShouldBe(0);
    }

    [Fact]
    public void Dispose_CancelsPendingSchedulesAndDisposesOwnedScheduler()
    {
        var scheduler = new ManualScheduler();
        var state = new MidiEditorPreviewSchedulerState(scheduler, ownsScheduler: true);
        var callbacks = 0;

        state.Schedule(
            MidiEditorPreviewScheduleGroup.CompensatedEvent,
            TimeSpan.Zero,
            () => callbacks++);

        state.Dispose();
        scheduler.RunAll();

        callbacks.ShouldBe(0);
        state.PendingCount.ShouldBe(0);
        scheduler.Disposed.ShouldBeTrue();
    }

    private sealed class ManualScheduler : IMidiEditorPreviewScheduler, IDisposable
    {
        private readonly List<ScheduledAction> actions = new();
        private long nextSequence;

        public bool Disposed { get; private set; }

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var action = new ScheduledAction(nextSequence++, callback);
            actions.Add(action);
            return action;
        }

        public void RunNext()
        {
            var action = actions
                .Where(item => !item.Cancelled)
                .OrderBy(item => item.Sequence)
                .FirstOrDefault();
            if (action == null)
                return;

            action.Cancelled = true;
            action.Callback();
        }

        public void RunAll()
        {
            while (actions.Any(action => !action.Cancelled))
                RunNext();
        }

        public void Dispose()
        {
            Disposed = true;
            foreach (var action in actions)
                action.Cancelled = true;
        }

        private sealed class ScheduledAction(long sequence, Action callback) : IDisposable
        {
            public long Sequence { get; } = sequence;
            public Action Callback { get; } = callback;
            public bool Cancelled { get; set; }

            public void Dispose()
                => Cancelled = true;
        }
    }
}
