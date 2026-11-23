using System;

namespace MidiBard.RemoteSync.Client;

/// <summary>
/// NTP-style clock offset estimator fed by four-timestamp ping/pong samples:
/// <c>rtt = (t3 - t0) - (t2 - t1)</c>, <c>offset = ((t1 - t0) + (t2 - t3)) / 2</c>
/// (offset = conductor clock − follower clock). Subtracting the conductor's
/// hold time <c>t2 - t1</c> keeps its send-queue delay out of both values.
///
/// The estimate is the median offset of the low-RTT samples in a sliding window
/// (those within <see cref="LowRttToleranceMs"/> of the best RTT). Low-RTT probes
/// saw little queuing in either direction, so their midpoint assumption holds
/// best; taking the median over several of them rejects a single asymmetric
/// outlier that a pure minimum-RTT pick would trust blindly.
/// </summary>
internal sealed class ClockSyncEstimator
{
    private const int WindowSize = 16;

    /// <summary>Samples whose RTT exceeds the best one by more than this are ignored for the offset.</summary>
    public const long LowRttToleranceMs = 20;

    private readonly (long Rtt, long Offset)[] _samples = new (long, long)[WindowSize];
    private readonly long[] _scratch = new long[WindowSize];
    private int _count;
    private int _next;
    private long _offsetMs;
    private long _rttMs = -1;
    private readonly object _gate = new();

    public bool HasEstimate
    {
        get
        {
            lock (_gate)
                return _count > 0;
        }
    }

    /// <summary>Best current offset estimate (conductor − follower), ms. 0 when no samples yet.</summary>
    public long OffsetMs
    {
        get
        {
            lock (_gate)
                return _offsetMs;
        }
    }

    /// <summary>Lowest RTT in the window (network round trip without conductor hold time), ms. -1 when no samples yet.</summary>
    public long RttMs
    {
        get
        {
            lock (_gate)
                return _rttMs;
        }
    }

    /// <param name="t0">Follower clock when the ping was written to the socket.</param>
    /// <param name="t1">Conductor clock when the ping was received.</param>
    /// <param name="t2">Conductor clock when the pong was written to the socket.</param>
    /// <param name="t3">Follower clock when the pong was received.</param>
    public void AddSample(long t0, long t1, long t2, long t3)
    {
        var hold = t2 - t1;
        var rtt = (t3 - t0) - hold;
        if (hold < 0 || rtt < 0)
            return;

        // Sum before halving so odd totals don't lose a whole millisecond per side.
        var offset = ((t1 - t0) + (t2 - t3)) / 2;
        lock (_gate)
        {
            _samples[_next] = (rtt, offset);
            _next = (_next + 1) % WindowSize;
            _count = Math.Min(_count + 1, WindowSize);
            RecomputeLocked();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _count = 0;
            _next = 0;
            _offsetMs = 0;
            _rttMs = -1;
        }
    }

    private void RecomputeLocked()
    {
        var bestRtt = long.MaxValue;
        for (var i = 0; i < _count; i++)
            bestRtt = Math.Min(bestRtt, _samples[i].Rtt);

        var n = 0;
        for (var i = 0; i < _count; i++)
        {
            if (_samples[i].Rtt <= bestRtt + LowRttToleranceMs)
                _scratch[n++] = _samples[i].Offset;
        }

        Array.Sort(_scratch, 0, n);
        _offsetMs = n % 2 == 1
            ? _scratch[n / 2]
            : (_scratch[n / 2 - 1] + _scratch[n / 2]) / 2;
        _rttMs = bestRtt;
    }
}
