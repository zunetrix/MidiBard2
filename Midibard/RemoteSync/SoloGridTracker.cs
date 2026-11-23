using System;

namespace MidiBard.RemoteSync;

/// <summary>
/// Tracks the 50 ms slot grid of this client's solo performance packets.
///
/// Observed game behaviour (validated with the solo send hook):
/// - a send session opens on the first key after silence and closes after
///   ~5 empty packets; each session picks a new, unpredictable grid phase;
/// - inside a session packets go out every 500 ms on the first frame past the
///   window boundary, so send times are the boundary or ~1 frame (16 ms) later;
///   when they alternate between two values, the real boundary sits at the later one.
///
/// The boundary estimate is therefore the upper envelope of the session's send
/// phases. All times are <see cref="SyncClock"/> milliseconds of this client.
/// </summary>
internal sealed class SoloGridTracker
{
    public const int PacketPeriodMs = 500;
    public const int SlotMs = 50;

    /// <summary>A gap longer than this between sends means the previous session closed.</summary>
    public const int SessionGapMs = 750;

    private readonly object _gate = new();
    private long _sessionFirstSendMs = long.MinValue;
    private long _lastSendMs = long.MinValue;
    private long _maxPhaseOffsetMs;
    private int _sendsInSession;

    /// <summary>Incremented every time a new send session is detected.</summary>
    public int SessionId { get; private set; }

    public void ObserveSend(long sendMs)
    {
        lock (_gate)
        {
            if (_lastSendMs == long.MinValue || sendMs - _lastSendMs > SessionGapMs)
            {
                SessionId++;
                _sessionFirstSendMs = sendMs;
                _maxPhaseOffsetMs = 0;
                _sendsInSession = 0;
            }

            // Offset of this send from the session's first send, folded into (-250, 250].
            var offset = PositiveMod(sendMs - _sessionFirstSendMs, PacketPeriodMs);
            if (offset > PacketPeriodMs / 2)
                offset -= PacketPeriodMs;

            if (_sendsInSession == 0 || offset > _maxPhaseOffsetMs)
                _maxPhaseOffsetMs = offset;

            _sendsInSession++;
            _lastSendMs = sendMs;
        }
    }

    /// <summary>
    /// Absolute time of one window boundary of the current session (any boundary;
    /// only its value modulo <see cref="SlotMs"/> / <see cref="PacketPeriodMs"/> matters),
    /// or null when no session is active at <paramref name="nowMs"/>.
    /// </summary>
    public long? GetBoundaryMs(long nowMs)
    {
        lock (_gate)
        {
            if (_lastSendMs == long.MinValue || nowMs - _lastSendMs > SessionGapMs)
                return null;

            return _sessionFirstSendMs + _maxPhaseOffsetMs;
        }
    }

    /// <summary>Sends seen in the current session (estimate gets tighter as this grows).</summary>
    public int SendsInSession
    {
        get
        {
            lock (_gate)
                return _sendsInSession;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _sessionFirstSendMs = long.MinValue;
            _lastSendMs = long.MinValue;
            _maxPhaseOffsetMs = 0;
            _sendsInSession = 0;
        }
    }

    /// <summary>
    /// Slot-grid phase in [0, 50) on another clock: <paramref name="boundaryMs"/> is local,
    /// <paramref name="offsetMs"/> converts local to that clock (conductor − local).
    /// </summary>
    public static int SlotPhase(long boundaryMs, long offsetMs)
        => (int)PositiveMod(boundaryMs + offsetMs, SlotMs);

    /// <summary>Signed difference between two slot phases folded into (-25, 25].</summary>
    public static int SlotPhaseDelta(int phase, int referencePhase)
    {
        var delta = PositiveMod(phase - referencePhase, SlotMs);
        return (int)(delta > SlotMs / 2 ? delta - SlotMs : delta);
    }

    private static long PositiveMod(long value, long modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
