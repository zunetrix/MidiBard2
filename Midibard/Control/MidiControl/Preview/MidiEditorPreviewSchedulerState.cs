using System;
using System.Collections.Generic;
using System.Linq;

namespace MidiBard.Control.MidiControl.Preview;

internal enum MidiEditorPreviewScheduleGroup
{
    CompensatedEvent,
    SameOnsetRoll,
    RetainedSoundCleanup,
}

internal sealed class MidiEditorPreviewSchedulerState : IDisposable
{
    private readonly object sync = new();
    private readonly IMidiEditorPreviewScheduler scheduler;
    private readonly bool ownsScheduler;
    private readonly List<TrackedSchedule> schedules = new();
    private readonly long[] versions = new long[Enum.GetValues<MidiEditorPreviewScheduleGroup>().Length];
    private bool disposed;

    public MidiEditorPreviewSchedulerState(
        IMidiEditorPreviewScheduler scheduler,
        bool ownsScheduler)
    {
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.ownsScheduler = ownsScheduler;
    }

    public int PendingCount
    {
        get
        {
            lock (sync)
                return schedules.Count;
        }
    }

    public int GetPendingCount(MidiEditorPreviewScheduleGroup group)
    {
        lock (sync)
            return schedules.Count(schedule => schedule.Group == group);
    }

    public long GetVersion(MidiEditorPreviewScheduleGroup group)
    {
        lock (sync)
            return versions[(int)group];
    }

    public bool IsCurrent(MidiEditorPreviewScheduleGroup group, long version)
        => GetVersion(group) == version;

    public IDisposable Schedule(
        MidiEditorPreviewScheduleGroup group,
        TimeSpan delay,
        Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        TrackedSchedule tracked;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            tracked = new TrackedSchedule(this, group, callback);
            schedules.Add(tracked);
        }

        try
        {
            tracked.SetInnerSchedule(scheduler.Schedule(delay, tracked.Invoke));
        }
        catch
        {
            tracked.Dispose();
            throw;
        }

        return tracked;
    }

    public long CancelGroup(MidiEditorPreviewScheduleGroup group)
    {
        TrackedSchedule[] toCancel;
        long version;
        lock (sync)
        {
            version = ++versions[(int)group];
            toCancel = schedules
                .Where(schedule => schedule.Group == group)
                .ToArray();
        }

        foreach (var schedule in toCancel)
            schedule.Dispose();

        return version;
    }

    public void CancelAll()
    {
        TrackedSchedule[] toCancel;
        lock (sync)
        {
            for (var i = 0; i < versions.Length; i++)
                versions[i]++;

            toCancel = schedules.ToArray();
        }

        foreach (var schedule in toCancel)
            schedule.Dispose();
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;

            disposed = true;
        }

        CancelAll();
        if (ownsScheduler && scheduler is IDisposable disposableScheduler)
            disposableScheduler.Dispose();
    }

    private void Remove(TrackedSchedule schedule)
    {
        lock (sync)
            schedules.Remove(schedule);
    }

    private sealed class TrackedSchedule : IDisposable
    {
        private readonly object sync = new();
        private readonly MidiEditorPreviewSchedulerState owner;
        private readonly Action callback;
        private IDisposable innerSchedule;
        private bool disposed;

        public TrackedSchedule(
            MidiEditorPreviewSchedulerState owner,
            MidiEditorPreviewScheduleGroup group,
            Action callback)
        {
            this.owner = owner;
            Group = group;
            this.callback = callback;
        }

        public MidiEditorPreviewScheduleGroup Group { get; }

        public void SetInnerSchedule(IDisposable schedule)
        {
            var disposeNow = false;
            lock (sync)
            {
                if (disposed)
                    disposeNow = true;
                else
                    innerSchedule = schedule;
            }

            if (disposeNow)
                schedule.Dispose();
        }

        public void Invoke()
        {
            lock (sync)
            {
                if (disposed)
                    return;

                disposed = true;
            }

            owner.Remove(this);
            callback();
        }

        public void Dispose()
        {
            IDisposable scheduleToDispose;
            lock (sync)
            {
                if (disposed)
                    return;

                disposed = true;
                scheduleToDispose = innerSchedule;
            }

            owner.Remove(this);
            scheduleToDispose?.Dispose();
        }
    }
}
