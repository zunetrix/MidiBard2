using System;

using MidiBard.RemoteSync.Client;

namespace MidiBard.Tests.RemoteSync;

public class ClockSyncEstimatorTests
{
    [Fact]
    public void NoSamplesMeansNoEstimate()
    {
        var estimator = new ClockSyncEstimator();

        estimator.HasEstimate.ShouldBeFalse();
        estimator.OffsetMs.ShouldBe(0);
        estimator.RttMs.ShouldBe(-1);
    }

    [Fact]
    public void SymmetricSampleYieldsExactOffset()
    {
        var estimator = new ClockSyncEstimator();

        // Conductor clock is +500ms ahead; 20ms each way, no conductor hold time.
        // t0=1000 (local send), conductor receives at local 1020 => t1 = t2 = 1520, t3=1040.
        estimator.AddSample(1000, 1520, 1520, 1040);

        estimator.HasEstimate.ShouldBeTrue();
        estimator.RttMs.ShouldBe(40);
        estimator.OffsetMs.ShouldBe(500);
    }

    [Fact]
    public void ConductorHoldTimeIsExcludedFromRttAndOffset()
    {
        var estimator = new ClockSyncEstimator();

        // Offset +500, 20ms each way, pong sat 60ms in the conductor's send queue.
        // A single-timestamp pong (t1 == t2 at receive) would report rtt 100 and offset 470.
        AddSample(estimator, t0: 1000, offset: 500, upMs: 20, holdMs: 60, downMs: 20);

        estimator.RttMs.ShouldBe(40);
        estimator.OffsetMs.ShouldBe(500);
    }

    [Fact]
    public void HighRttJitteredSamplesAreIgnored()
    {
        var estimator = new ClockSyncEstimator();
        const long trueOffset = -250;

        // One clean sample: rtt 20, perfect symmetry.
        AddSample(estimator, t0: 5000, offset: trueOffset, upMs: 10, holdMs: 0, downMs: 10);

        // Many jittered samples: large asymmetric delays corrupt their offsets.
        var random = new Random(42);
        for (var i = 0; i < 12; i++)
        {
            AddSample(estimator, t0: 6000 + i * 100, offset: trueOffset,
                upMs: 50 + random.Next(100), holdMs: random.Next(5), downMs: 5 + random.Next(20));
        }

        estimator.RttMs.ShouldBe(20);
        estimator.OffsetMs.ShouldBe(trueOffset);
    }

    [Fact]
    public void MedianRejectsAsymmetricOutlierAmongLowRttSamples()
    {
        var estimator = new ClockSyncEstimator();
        const long trueOffset = 1000;

        // Lowest-RTT sample is asymmetric (2ms up, 16ms down): its own offset reads 993.
        AddSample(estimator, t0: 0, offset: trueOffset, upMs: 2, holdMs: 0, downMs: 16);

        // Slightly slower but symmetric samples, all within the low-RTT tolerance.
        for (var i = 1; i <= 4; i++)
            AddSample(estimator, t0: i * 1000, offset: trueOffset, upMs: 10, holdMs: 0, downMs: 10);

        estimator.RttMs.ShouldBe(18);
        estimator.OffsetMs.ShouldBe(trueOffset);
    }

    [Fact]
    public void EvenCountUsesMeanOfMiddleOffsets()
    {
        var estimator = new ClockSyncEstimator();

        AddSample(estimator, t0: 0, offset: 100, upMs: 5, holdMs: 0, downMs: 5);
        AddSample(estimator, t0: 1000, offset: 110, upMs: 5, holdMs: 0, downMs: 5);

        estimator.OffsetMs.ShouldBe(105);
    }

    [Fact]
    public void WindowSlidesOutOldSamples()
    {
        var estimator = new ClockSyncEstimator();

        // Best sample first (rtt 10, offset 100)...
        AddSample(estimator, t0: 0, offset: 100, upMs: 5, holdMs: 0, downMs: 5);

        // ...then 16 worse samples (rtt 60, offset 300) that push it out of the window.
        for (var i = 1; i <= 16; i++)
            AddSample(estimator, t0: i * 1000, offset: 300, upMs: 30, holdMs: 0, downMs: 30);

        estimator.OffsetMs.ShouldBe(300);
        estimator.RttMs.ShouldBe(60);
    }

    [Fact]
    public void NegativeRttSamplesAreDiscarded()
    {
        var estimator = new ClockSyncEstimator();

        estimator.AddSample(1000, 1500, 1500, 900); // t3 < t0

        estimator.HasEstimate.ShouldBeFalse();
    }

    [Fact]
    public void NegativeHoldSamplesAreDiscarded()
    {
        var estimator = new ClockSyncEstimator();

        estimator.AddSample(1000, 1500, 1490, 1040); // t2 < t1

        estimator.HasEstimate.ShouldBeFalse();
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var estimator = new ClockSyncEstimator();
        AddSample(estimator, t0: 1000, offset: 50, upMs: 10, holdMs: 0, downMs: 10);

        estimator.Reset();

        estimator.HasEstimate.ShouldBeFalse();
        estimator.OffsetMs.ShouldBe(0);
        estimator.RttMs.ShouldBe(-1);
    }

    /// <summary>Builds a sample from ground truth: conductor clock = follower clock + offset.</summary>
    private static void AddSample(ClockSyncEstimator estimator, long t0, long offset, long upMs, long holdMs, long downMs)
    {
        var t1 = t0 + upMs + offset;
        var t2 = t1 + holdMs;
        var t3 = t0 + upMs + holdMs + downMs;
        estimator.AddSample(t0, t1, t2, t3);
    }
}
