using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Dalamud.Hooking;

using MidiBard.RemoteSync;

namespace MidiBard.Managers;

/// <summary>
/// Diagnostic capture of the game's solo performance packets (10 note slots per packet,
/// ~50 ms each). Used to validate the recovered solo send/receive signatures and to
/// measure each client's slot-grid timing. Hooks are created up front but only enabled
/// while <see cref="SendMonitorEnabled"/> / <see cref="ReceiveMonitorEnabled"/> are on,
/// so an unverified signature can never affect normal play.
/// </summary>
internal sealed class SoloPerformanceMonitor : IDisposable
{
    public const int SlotCount = 10;
    private const int MaxEntries = 200;

    // Send packet layout (from the old NetworkManager.LogNotes): notes at +0x10, tones right after.
    private const int SendNotesOffset = 0x10;
    private const int SendRawBytes = SendNotesOffset + SlotCount * 2;

    // Receive payload layout (SoloPerformanceIpc): count byte, 10 notes, 10 tones.
    private const int RecvNotesOffset = 1;
    private const int RecvRawBytes = 1 + SlotCount * 2 + 3;

    private delegate void SoloSendDelegate(nint packet);
    private delegate nint SoloReceiveDelegate(uint sourceId, nint data);

    private readonly Hook<SoloSendDelegate>? _sendHook;
    private readonly Hook<SoloReceiveDelegate>? _receiveHook;

    private readonly List<SoloPacketSnapshot> _log = new();
    private readonly Dictionary<uint, long> _lastReceiveBySource = new();
    private long _lastSendMs = long.MinValue;

    internal enum EntryKind
    {
        Send,
        Receive,
        /// <summary>This client pressed a performance key (Notes[0] = key 0-36).</summary>
        KeyDown,
        /// <summary>This client released a performance key (Notes[0] = key 0-36).</summary>
        KeyUp,
    }

    internal readonly record struct SoloPacketSnapshot(
        DateTime Timestamp,
        long SyncMs,
        EntryKind Kind,
        uint SourceId,
        long DeltaMs,
        byte[] Notes,
        byte[] Tones,
        byte[] Raw)
    {
        public bool IsSend => Kind == EntryKind.Send;
        public bool IsKey => Kind is EntryKind.KeyDown or EntryKind.KeyUp;

        /// <summary>Slots holding a played note (0xFF = empty, 0xFE = key release).</summary>
        public int ActiveNoteCount => Notes.Count(n => n is not (0xFF or 0xFE));
    }

    public bool SendSignatureFound => Offsets.SoloSendHandler != 0;
    public bool ReceiveSignatureFound => Offsets.SoloReceivedHandler != 0;

    private volatile bool _sendLogEnabled;
    private volatile bool _gridTrackingEnabled;

    /// <summary>Debug capture of sent packets and key events into the log.</summary>
    public bool SendMonitorEnabled
    {
        get => _sendLogEnabled;
        set
        {
            _sendLogEnabled = value;
            UpdateSendHook();
        }
    }

    /// <summary>
    /// Feeds <see cref="Grid"/> from sent packets without logging. Turned on by remote
    /// sync so every client can report its slot-grid phase.
    /// </summary>
    public bool GridTrackingEnabled
    {
        get => _gridTrackingEnabled;
        set
        {
            _gridTrackingEnabled = value;
            if (!value)
                Grid.Reset();
            UpdateSendHook();
        }
    }

    /// <summary>Slot grid of this client's current solo send session.</summary>
    public SoloGridTracker Grid { get; } = new();

    private void UpdateSendHook() => Toggle(_sendHook, _sendLogEnabled || _gridTrackingEnabled);

    public bool ReceiveMonitorEnabled
    {
        get => _receiveHook?.IsEnabled == true;
        set => Toggle(_receiveHook, value);
    }

    private static volatile SoloPerformanceMonitor? _instance;

    private long _keysRecorded;
    private long _keysSkipped;

    /// <summary>Key events logged since the last clear.</summary>
    public long KeysRecorded => System.Threading.Interlocked.Read(ref _keysRecorded);

    /// <summary>Key events seen while the send monitor was off (diagnostic for "no KEY rows").</summary>
    public long KeysSkipped => System.Threading.Interlocked.Read(ref _keysSkipped);

    /// <summary>
    /// Logs a performance key press/release from <see cref="Control.BardPlayDevice"/> while the
    /// send monitor runs, so key timing can be matched against the slots of sent packets.
    /// </summary>
    public static void RecordKey(int key, bool down)
    {
        var monitor = _instance;
        if (monitor == null)
            return;

        if (!monitor.SendMonitorEnabled)
        {
            System.Threading.Interlocked.Increment(ref monitor._keysSkipped);
            return;
        }

        System.Threading.Interlocked.Increment(ref monitor._keysRecorded);
        monitor.Add(new SoloPacketSnapshot(
            DateTime.Now, SyncClock.NowMs, down ? EntryKind.KeyDown : EntryKind.KeyUp, 0, -1,
            new[] { (byte)key }, Array.Empty<byte>(), Array.Empty<byte>()));
    }

    public SoloPerformanceMonitor()
    {
        _instance = this;

        try
        {
            if (Offsets.SoloSendHandler != 0)
                _sendHook = DalamudApi.GameInteropProvider.HookFromAddress<SoloSendDelegate>(Offsets.SoloSendHandler, OnSend);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, "[SoloMonitor] Could not create solo send hook.");
        }

        try
        {
            if (Offsets.SoloReceivedHandler != 0)
                _receiveHook = DalamudApi.GameInteropProvider.HookFromAddress<SoloReceiveDelegate>(Offsets.SoloReceivedHandler, OnReceive);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, "[SoloMonitor] Could not create solo receive hook.");
        }
    }

    /// <summary>Snapshot of the capture log, oldest first.</summary>
    public List<SoloPacketSnapshot> GetLog()
    {
        lock (_log)
            return _log.ToList();
    }

    public void Clear()
    {
        lock (_log)
        {
            _log.Clear();
            _lastReceiveBySource.Clear();
            _lastSendMs = long.MinValue;
            System.Threading.Interlocked.Exchange(ref _keysRecorded, 0);
            System.Threading.Interlocked.Exchange(ref _keysSkipped, 0);
        }
    }

    private static void Toggle<T>(Hook<T>? hook, bool enable) where T : Delegate
    {
        if (hook == null)
            return;

        if (enable)
            hook.Enable();
        else
            hook.Disable();
    }

    private void OnSend(nint packet)
    {
        try
        {
            var now = SyncClock.NowMs;
            if (_gridTrackingEnabled)
                Grid.ObserveSend(now);

            var raw = _sendLogEnabled ? ReadBytes(packet, SendRawBytes) : null;
            if (raw != null)
            {
                long delta;
                lock (_log)
                {
                    delta = _lastSendMs == long.MinValue ? -1 : now - _lastSendMs;
                    _lastSendMs = now;
                }

                Add(new SoloPacketSnapshot(
                    DateTime.Now, now, EntryKind.Send, SourceId: 0, delta,
                    raw.AsSpan(SendNotesOffset, SlotCount).ToArray(),
                    raw.AsSpan(SendNotesOffset + SlotCount, SlotCount).ToArray(),
                    raw));
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[SoloMonitor] error in solo send hook");
        }

        _sendHook!.Original(packet);
    }

    private nint OnReceive(uint sourceId, nint data)
    {
        try
        {
            var raw = ReadBytes(data, RecvRawBytes);
            if (raw != null)
            {
                var now = SyncClock.NowMs;
                long delta;
                lock (_log)
                {
                    delta = _lastReceiveBySource.TryGetValue(sourceId, out var last) ? now - last : -1;
                    _lastReceiveBySource[sourceId] = now;
                }

                Add(new SoloPacketSnapshot(
                    DateTime.Now, now, EntryKind.Receive, sourceId, delta,
                    raw.AsSpan(RecvNotesOffset, SlotCount).ToArray(),
                    raw.AsSpan(RecvNotesOffset + SlotCount, SlotCount).ToArray(),
                    raw));
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[SoloMonitor] error in solo receive hook");
        }

        return _receiveHook!.Original(sourceId, data);
    }

    // Rejects obviously invalid pointers (a mis-resolved signature may pass a non-pointer).
    private static byte[]? ReadBytes(nint address, int count)
    {
        if (address < 0x10000)
            return null;

        var buffer = new byte[count];
        Marshal.Copy(address, buffer, 0, count);
        return buffer;
    }

    private void Add(SoloPacketSnapshot snapshot)
    {
        lock (_log)
        {
            _log.Add(snapshot);
            if (_log.Count > MaxEntries)
                _log.RemoveRange(0, _log.Count - MaxEntries);
        }
    }

    public void Dispose()
    {
        if (_instance == this)
            _instance = null;

        _sendHook?.Dispose();
        _receiveHook?.Dispose();
    }
}
