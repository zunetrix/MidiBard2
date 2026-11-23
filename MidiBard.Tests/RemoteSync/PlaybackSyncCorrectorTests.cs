using MidiBard.RemoteSync.Client;

namespace MidiBard.Tests.RemoteSync;

public class PlaybackSyncCorrectorTests
{
    private const int Soft = 30;
    private const int Hard = 500;
    private const double MaxNudge = 0.05;

    private static SyncCorrection Evaluate(
        PlaybackSyncCorrector corrector,
        long drift,
        long expected = 10_000,
        long duration = 60_000)
        => corrector.Evaluate(drift, expected, duration, Soft, Hard, MaxNudge);

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(-29)]
    public void DriftInsideDeadZoneDoesNothing(long drift)
    {
        var corrector = new PlaybackSyncCorrector();

        Evaluate(corrector, drift).Kind.ShouldBe(SyncCorrectionKind.None);
    }

    [Fact]
    public void BehindTheConductorSpeedsUp()
    {
        var corrector = new PlaybackSyncCorrector();

        // drift = local − expected; negative = behind.
        var correction = Evaluate(corrector, drift: -250);

        correction.Kind.ShouldBe(SyncCorrectionKind.NudgeSpeed);
        correction.SpeedFactor.ShouldBeGreaterThan(1.0);
        // Proportional: 250/500 × 0.05 = +2.5%.
        correction.SpeedFactor.ShouldBe(1.025, 0.0001);
    }

    [Fact]
    public void AheadOfTheConductorSlowsDown()
    {
        var corrector = new PlaybackSyncCorrector();

        var correction = Evaluate(corrector, drift: 250);

        correction.Kind.ShouldBe(SyncCorrectionKind.NudgeSpeed);
        correction.SpeedFactor.ShouldBe(0.975, 0.0001);
    }

    [Fact]
    public void NudgeIsCappedAtTheConfiguredMaximum()
    {
        var corrector = new PlaybackSyncCorrector();

        var correction = Evaluate(corrector, drift: -499);

        correction.Kind.ShouldBe(SyncCorrectionKind.NudgeSpeed);
        correction.SpeedFactor.ShouldBeLessThanOrEqualTo(1.0 + MaxNudge + 0.0001);
    }

    [Fact]
    public void LargeDriftJumpsToTheExpectedPosition()
    {
        var corrector = new PlaybackSyncCorrector();

        var correction = Evaluate(corrector, drift: 800, expected: 12_345);

        correction.Kind.ShouldBe(SyncCorrectionKind.Jump);
        correction.TargetPositionMs.ShouldBe(12_345);
    }

    [Fact]
    public void JumpTargetIsClampedToSongDuration()
    {
        var corrector = new PlaybackSyncCorrector();

        var correction = Evaluate(corrector, drift: -800, expected: 70_000, duration: 60_000);

        correction.Kind.ShouldBe(SyncCorrectionKind.Jump);
        correction.TargetPositionMs.ShouldBe(60_000);
    }

    [Fact]
    public void ReenteringTheDeadZoneRestoresBaseSpeedExactlyOnce()
    {
        var corrector = new PlaybackSyncCorrector();

        Evaluate(corrector, drift: -200).Kind.ShouldBe(SyncCorrectionKind.NudgeSpeed);

        // Drift recovered: the active nudge must be undone...
        Evaluate(corrector, drift: 5).Kind.ShouldBe(SyncCorrectionKind.ResetSpeed);

        // ...and subsequent in-zone ticks stay silent.
        Evaluate(corrector, drift: 5).Kind.ShouldBe(SyncCorrectionKind.None);
    }

    [Fact]
    public void ActiveNudgeKeepsCorrectingInsideTheSoftZoneUntilNearZero()
    {
        var corrector = new PlaybackSyncCorrector();

        Evaluate(corrector, drift: -200).Kind.ShouldBe(SyncCorrectionKind.NudgeSpeed);

        // Drift fell below the soft threshold (30) but not below the exit
        // threshold (10): without hysteresis the follower would settle here,
        // permanently ~soft-threshold behind the conductor.
        var stillCorrecting = Evaluate(corrector, drift: -20);
        stillCorrecting.Kind.ShouldBe(SyncCorrectionKind.NudgeSpeed);
        // Floored at the minimum active nudge so small drift converges quickly.
        stillCorrecting.SpeedFactor.ShouldBe(1.0 + PlaybackSyncCorrector.MinActiveNudge, 0.0001);

        // Below the exit threshold: done, base speed restored, and the full soft
        // dead zone applies again for subsequent small drift.
        Evaluate(corrector, drift: -5).Kind.ShouldBe(SyncCorrectionKind.ResetSpeed);
        Evaluate(corrector, drift: -20).Kind.ShouldBe(SyncCorrectionKind.None);
    }

    [Fact]
    public void FreshDriftInsideTheSoftZoneIsStillIgnored()
    {
        var corrector = new PlaybackSyncCorrector();

        // No nudge active: the full soft dead zone applies.
        Evaluate(corrector, drift: -20).Kind.ShouldBe(SyncCorrectionKind.None);
    }

    [Fact]
    public void ConvergenceModeCorrectsStartResidualBelowTheSoftThreshold()
    {
        var corrector = new PlaybackSyncCorrector();

        // A start residual of 20ms (below soft=30) would normally never be
        // corrected, leaving the follower audibly behind the conductor forever.
        corrector.BeginConvergence();

        var correction = Evaluate(corrector, drift: -20);
        correction.Kind.ShouldBe(SyncCorrectionKind.NudgeSpeed);
        correction.SpeedFactor.ShouldBeGreaterThan(1.0);

        // Converged: base speed restored, wide dead zone applies again.
        Evaluate(corrector, drift: -5).Kind.ShouldBe(SyncCorrectionKind.ResetSpeed);
        Evaluate(corrector, drift: -20).Kind.ShouldBe(SyncCorrectionKind.None);
    }

    [Fact]
    public void ConvergenceModeWithNoResidualResetsOnce()
    {
        var corrector = new PlaybackSyncCorrector();
        corrector.BeginConvergence();

        // Already tight at start: one ResetSpeed (harmless base-speed reapply), then silent.
        Evaluate(corrector, drift: 3).Kind.ShouldBe(SyncCorrectionKind.ResetSpeed);
        Evaluate(corrector, drift: 3).Kind.ShouldBe(SyncCorrectionKind.None);
    }

    [Fact]
    public void JumpClearsTheActiveNudgeState()
    {
        var corrector = new PlaybackSyncCorrector();

        Evaluate(corrector, drift: -200).Kind.ShouldBe(SyncCorrectionKind.NudgeSpeed);
        Evaluate(corrector, drift: 900).Kind.ShouldBe(SyncCorrectionKind.Jump);

        // After the jump the corrector no longer owes a speed reset.
        Evaluate(corrector, drift: 5).Kind.ShouldBe(SyncCorrectionKind.None);
    }

    [Fact]
    public void ResetForgetsAnActiveNudge()
    {
        var corrector = new PlaybackSyncCorrector();
        Evaluate(corrector, drift: -200);

        corrector.Reset();

        Evaluate(corrector, drift: 5).Kind.ShouldBe(SyncCorrectionKind.None);
    }
}
