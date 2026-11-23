using System;

namespace MidiBard.RemoteSync.Client;

internal enum SyncCorrectionKind
{
    /// <summary>Drift inside the dead zone and no nudge active - do nothing.</summary>
    None,

    /// <summary>Restore the user's base speed (drift re-entered the dead zone after a nudge).</summary>
    ResetSpeed,

    /// <summary>Apply <see cref="SyncCorrection.SpeedFactor"/> on top of the base speed.</summary>
    NudgeSpeed,

    /// <summary>Drift too large - seek straight to <see cref="SyncCorrection.TargetPositionMs"/>.</summary>
    Jump,
}

internal readonly record struct SyncCorrection(
    SyncCorrectionKind Kind,
    double SpeedFactor = 1.0,
    long TargetPositionMs = 0);

/// <summary>
/// Pure drift-correction policy (follower side):
/// dead zone below the soft threshold, proportional speed nudge in between,
/// hard seek at or above the hard threshold. Unlike a naive implementation,
/// it remembers when a nudge is active so re-entering the dead zone restores
/// the user's base speed instead of leaving a permanent speed offset.
///
/// The dead zone is asymmetric (hysteresis): correction starts when drift
/// exceeds the soft threshold but, once nudging, continues until drift falls
/// below <see cref="NudgeExitThresholdMs"/>. Without this, correction would
/// stop right at the soft boundary and the follower would settle permanently
/// ~softThreshold behind (or ahead of) the conductor.
/// </summary>
internal sealed class PlaybackSyncCorrector
{
    /// <summary>Once nudging, keep correcting until drift falls below this (ms).</summary>
    public const int NudgeExitThresholdMs = 10;

    /// <summary>Minimum active-nudge speed fraction, so small residual drift still converges quickly.</summary>
    public const double MinActiveNudge = 0.01;

    private bool _nudgeActive;

    /// <param name="driftMs">local position − expected position (positive = follower ahead).</param>
    /// <param name="expectedPositionMs">Where the follower should be right now.</param>
    /// <param name="durationMs">Song duration, for clamping jump targets.</param>
    /// <param name="softThresholdMs">Dead zone (ms).</param>
    /// <param name="hardThresholdMs">Jump threshold (ms).</param>
    /// <param name="maxSpeedNudge">Max fractional speed adjustment (e.g. 0.05 = ±5%).</param>
    public SyncCorrection Evaluate(
        long driftMs,
        long expectedPositionMs,
        long durationMs,
        int softThresholdMs,
        int hardThresholdMs,
        double maxSpeedNudge)
    {
        var magnitude = Math.Abs(driftMs);
        var deadZoneMs = _nudgeActive ? Math.Min(NudgeExitThresholdMs, softThresholdMs) : softThresholdMs;

        if (magnitude < deadZoneMs)
        {
            if (!_nudgeActive)
                return new SyncCorrection(SyncCorrectionKind.None);

            _nudgeActive = false;
            return new SyncCorrection(SyncCorrectionKind.ResetSpeed);
        }

        if (magnitude >= hardThresholdMs)
        {
            _nudgeActive = false;
            var target = Math.Clamp(expectedPositionMs, 0, Math.Max(0, durationMs));
            return new SyncCorrection(SyncCorrectionKind.Jump, TargetPositionMs: target);
        }

        // Proportional nudge: ahead (positive drift) slows down, behind speeds up.
        // Floored so residual drift below the soft threshold still converges to ~zero
        // instead of crawling (inaudible: ≤1% speed for a few seconds).
        var nudgeMagnitude = Math.Clamp(
            magnitude / (double)hardThresholdMs * maxSpeedNudge,
            Math.Min(MinActiveNudge, maxSpeedNudge),
            maxSpeedNudge);
        var fraction = driftMs > 0 ? -nudgeMagnitude : nudgeMagnitude;

        _nudgeActive = true;
        return new SyncCorrection(SyncCorrectionKind.NudgeSpeed, SpeedFactor: 1.0 + fraction);
    }

    /// <summary>Forget any active nudge (e.g. after stop/load, when base speed is reapplied anyway).</summary>
    public void Reset() => _nudgeActive = false;

    /// <summary>
    /// Arm the narrow exit dead zone for the next evaluations. Called right after
    /// playback starts: the start residual (DoPlay dispatch + game frame phase,
    /// typically 15-45ms) sits below the soft threshold, so without this it would
    /// never be corrected and every follower would sit audibly behind the conductor.
    /// </summary>
    public void BeginConvergence() => _nudgeActive = true;
}
