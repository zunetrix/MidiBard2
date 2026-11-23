using MidiBard.RemoteSync;

namespace MidiBard.Tests.RemoteSync;

public class SoloGridTrackerTests
{
    [Fact]
    public void NoSendsMeansNoSession()
    {
        var tracker = new SoloGridTracker();

        tracker.GetBoundaryMs(1000).ShouldBeNull();
    }

    [Fact]
    public void SteadySendsGiveTheirOwnPhase()
    {
        var tracker = new SoloGridTracker();
        for (var i = 0; i < 4; i++)
            tracker.ObserveSend(10_488 + i * 500);

        var boundary = tracker.GetBoundaryMs(12_000).ShouldNotBeNull();
        (boundary % 500).ShouldBe(488);
    }

    [Fact]
    public void FrameLateSendsUseUpperEnvelope()
    {
        // Real log: sends alternate 56/72 (one 60 fps frame apart); boundary is the later one.
        var tracker = new SoloGridTracker();
        long[] sends = [349_072, 349_556, 350_056, 350_572, 351_072, 351_556];
        foreach (var send in sends)
            tracker.ObserveSend(send);

        var boundary = tracker.GetBoundaryMs(351_600).ShouldNotBeNull();
        (boundary % 500).ShouldBe(72);
    }

    [Fact]
    public void EnvelopeHandlesWrapAroundThePacketPeriod()
    {
        // Phases 488 then 4 (= 504): the later boundary wraps past 500.
        var tracker = new SoloGridTracker();
        tracker.ObserveSend(10_488);
        tracker.ObserveSend(11_004);
        tracker.ObserveSend(11_488);

        var boundary = tracker.GetBoundaryMs(11_500).ShouldNotBeNull();
        (boundary % 500).ShouldBe(4);
    }

    [Fact]
    public void GapStartsNewSessionWithFreshPhase()
    {
        var tracker = new SoloGridTracker();
        tracker.ObserveSend(10_488);
        tracker.ObserveSend(10_988);
        var first = tracker.SessionId;

        tracker.ObserveSend(14_004); // > 750 ms of silence: new session
        tracker.ObserveSend(14_504);

        tracker.SessionId.ShouldBe(first + 1);
        (tracker.GetBoundaryMs(14_600).ShouldNotBeNull() % 500).ShouldBe(4);
        tracker.SendsInSession.ShouldBe(2);
    }

    [Fact]
    public void SessionExpiresAfterSilence()
    {
        var tracker = new SoloGridTracker();
        tracker.ObserveSend(10_000);

        tracker.GetBoundaryMs(10_700).ShouldNotBeNull();
        tracker.GetBoundaryMs(10_800).ShouldBeNull();
    }

    [Theory]
    [InlineData(10_488, 0, 38)]
    [InlineData(10_488, 20, 8)]
    [InlineData(10_488, -500, 38)]
    [InlineData(10_003, -10, 43)]
    public void SlotPhaseConvertsAndWraps(long boundary, long offset, int expected)
        => SoloGridTracker.SlotPhase(boundary, offset).ShouldBe(expected);

    [Theory]
    [InlineData(38, 4, -16)]
    [InlineData(4, 38, 16)]
    [InlineData(10, 10, 0)]
    [InlineData(35, 10, 25)]
    [InlineData(10, 36, 24)]
    public void SlotPhaseDeltaFoldsToHalfSlot(int phase, int reference, int expected)
        => SoloGridTracker.SlotPhaseDelta(phase, reference).ShouldBe(expected);
}
