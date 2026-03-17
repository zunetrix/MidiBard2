using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Hooking;
using Dalamud.Interface.ImGuiNotification;

using MidiBard.Util;

namespace MidiBard.Managers;

internal class EnsembleManager : IDisposable
{
    private Plugin Plugin { get; }
    public event Action EnsembleStart;
    public event Action EnsemblePrepare;
    public event Action EnsembleStopped;
    public readonly Stopwatch EnsembleTimer = new Stopwatch();
    internal List<TimeSpan> EnsembleRecvTime { get; } = new();
    public bool EnsembleRunning => EnsembleTimer.IsRunning;
    private delegate long NetworkEnsembleDelegate(IntPtr a1, IntPtr a2);
    private readonly Hook<NetworkEnsembleDelegate> NetworkEnsembleHook;

    private long HandleNetworkEnsemble(IntPtr a1, IntPtr a2)
    {
        if (Plugin.Config.MonitorOnEnsemble)
            StartEnsemble();

        return NetworkEnsembleHook.Original(a1, a2);
    }

    internal EnsembleManager(Plugin plugin)
    {
        Plugin = plugin;
        //UpdateMetronomeHook = new Hook<sub_140C87B40>(Offsets.UpdateMetronome, HandleUpdateMetronome);
        //UpdateMetronomeHook.Enable();

        // NetworkEnsembleHook = DalamudApi.GameInteropProvider.HookFromAddress<sub_1410F4EC0>(Offsets.NetworkEnsembleStart, (a1, a2) =>
        // {
        //     if (Plugin.Config.MonitorOnEnsemble) StartEnsemble();
        //     return NetworkEnsembleHook.Original(a1, a2);
        // });
        // NetworkEnsembleHook.Enable();

        NetworkEnsembleHook = DalamudApi.GameInteropProvider.HookFromAddress<NetworkEnsembleDelegate>(Offsets.NetworkEnsembleStart, HandleNetworkEnsemble);
        NetworkEnsembleHook.Enable();

        EnsembleStopped += () => EnsembleTimer.Reset();
    }

    // Tracks whether ensemble was running on the previous frame to detect the running→stopped transition.
    private bool _wasEnsembleModeRunning = false;

    // Called each framework update when Config.MonitorOnEnsemble is true.
    // Detects when ensemble mode ends and triggers cleanup (stop playback, unequip instrument).
    public void MonitorEnsembleState()
    {
        if (_wasEnsembleModeRunning)
        {
            if (!AgentManager.AgentMetronome.EnsembleModeRunning || !AgentManager.AgentPerformance.InPerformanceMode)
            {
                InvokeEnsembleStop();
                if (Plugin.Config.StopPlayingWhenEnsembleEnds)
                    Plugin.MidiPlayerControl.Pause();
                // Fallback unequip: catches clients that missed the IPC unequip broadcast
                Plugin.InstrumentSwitcher.SwitchToContinue(0);

                if (Plugin.Config.EnableEnsemblePlayMode)
                {
                    Plugin.FilePlayback.TryEnsembleAutoAdvance();
                }
            }
        }
        _wasEnsembleModeRunning = AgentManager.AgentMetronome.EnsembleModeRunning && AgentManager.AgentPerformance.InPerformanceMode;
    }

    //public SyncHelper(out List<(byte[] notes, byte[] tones)> sendNotes, out List<(byte[] notes, byte[] tones)> recvNotes)
    //{
    //    sendNotes = new List<(byte[] notes, byte[] tones)>();
    //    recvNotes = new List<(byte[] notes, byte[] tones)>();
    //}

    //private delegate IntPtr sub_140C87B40(IntPtr agentMetronome, byte beat);
    //   private Hook<sub_140C87B40> UpdateMetronomeHook;

    public void BeginEnsembleReadyCheck(int delayMs = 0)
    {
        if (delayMs > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(delayMs);
                await DalamudApi.Framework.RunOnFrameworkThread(DoBeginEnsembleReadyCheck);
            });
            return;
        }

        DoBeginEnsembleReadyCheck();
    }

    private unsafe void DoBeginEnsembleReadyCheck()
    {
        if (AgentManager.AgentMetronome.EnsembleModeRunning) return;

        if (AgentManager.AgentPerformance.InPerformanceMode && !AgentManager.AgentMetronome.Struct->AgentInterface.IsAgentActive())
            AgentManager.AgentMetronome.Struct->AgentInterface.Show();

        Playlib.BeginReadyCheck();
        Playlib.ConfirmBeginReadyCheck();
    }

    internal void StopEnsemble()
    {
        Playlib.BeginReadyCheck();
        Playlib.SendAction("SelectYesno", 3, 0);
    }

    internal void BroadcastEquipInstruments()
    {
        if (Plugin.CurrentBardPlayback?.MidiFileConfig is { } config)
            Plugin.IpcProvider.UpdateMidiFileConfig(config);

        if (!Plugin.Config.playOnMultipleDevices)
            Plugin.IpcProvider.UpdateInstrument(true);
        else
            Plugin.ChatWatcher.SendUpdateInstrument();
    }

    internal void BroadcastUnequipInstruments()
    {
        if (Plugin.Config.playOnMultipleDevices)
        {
            Plugin.ChatWatcher.SendClose();
            return;
        }

        Plugin.IpcProvider.UpdateInstrument(false); // broadcast to other clients
        Plugin.InstrumentSwitcher.SwitchToContinue(0); // always self-unequip directly
    }

    //private unsafe IntPtr HandleUpdateMetronome(IntPtr agentMetronome, byte currentBeat)
    //{
    //    var original = UpdateMetronomeHook.Original(agentMetronome, currentBeat);
    //    try
    //    {
    //        if (MidiBard.Plugin.Config.MonitorOnEnsemble)
    //        {
    //            var metronome = ((AgentMetronome.AgentMetronomeStruct*)agentMetronome);
    //            var beatsPerBar = metronome->MetronomeBeatsPerBar;
    //            var barElapsed = metronome->MetronomeBeatsElapsed;
    //            var ensembleRunning = metronome->EnsembleModeRunning;
    //               DalamudApi.PluginLog.Verbose($"[Metronome] {barElapsed} {currentBeat}/{beatsPerBar}");

    //               if (barElapsed == -2 && currentBeat == 0 && ensembleRunning != 0)
    //               {
    //                   DalamudApi.PluginLog.Warning($"Prepare: ensemble: {ensembleRunning}");
    //                   StartEnsemble();
    //               }
    //           }
    //    }
    //    catch (Exception e)
    //    {
    //        DalamudApi.PluginLog.Error(e, $"error in {nameof(UpdateMetronomeHook)}");
    //    }

    //    return original;
    //}

    private void StartEnsemble()
    {
        EnsembleRecvTime.Clear();
        EnsemblePrepare?.Invoke();

        // if playback is null, cancel ensemble mode.
        if (!Plugin.CurrentBardPlayback.IsLoaded)
        {
            if (Plugin.Config.SyncClients)
            {
                StopEnsemble();
                ImGuiUtil.AddNotification(NotificationType.Error, "Please load a song before starting ensemble!");
                Plugin.IpcProvider.ErrPlaybackNull(DalamudApi.PlayerState.CharacterName);
            }
        }
        else
        {
            EnsembleTimer.Restart();
            Plugin.CurrentBardPlayback.Stop();
            Plugin.CurrentBardPlayback.MoveToStart();

            try
            {
                Plugin.MidiPlayerControl.DoPlay(true);
                DalamudApi.PluginLog.Warning($"Start ensemble: sw: {EnsembleTimer.Elapsed.TotalMilliseconds}ms");
                EnsembleStart?.Invoke();
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error EnsembleStart");
            }
        }
    }

    // Per-song compensation override loaded from the song's JSON sidecar file.
    // Null when no override is active; set by SetPerSongCompensation when a song carries
    // JSON compensation data, and cleared by ClearPerSongCompensation when it does not.
    // PerSongCompensationMode forces ByInstrument for the duration of the song without
    // permanently changing the user's global CompensationMode setting.
    public Dictionary<string, int>? PerSongCompensation { get; private set; } = null;
    public CompensationModes? PerSongCompensationMode { get; private set; } = null;

    // Version-based cache for the resolved int[] compensation array (instrument rowId → ms delay).
    // _compensationVersion is incremented whenever source data changes; the array is only
    // rebuilt in GetEffectiveCompensationArray() when _cachedVersion is stale.
    private int[]? _effectiveCompensationCache = null;
    private int _compensationVersion = 0;
    private int _cachedVersion = -1;

    // Activates a per-song compensation override from the song's JSON file.
    // Forces ByInstrument mode for this song without touching the global CompensationMode.
    public void SetPerSongCompensation(Dictionary<string, int> dict)
    {
        PerSongCompensation = dict;
        PerSongCompensationMode = CompensationModes.ByInstrument;
        _compensationVersion++;
    }

    // Removes the active per-song override, reverting to global InstrumentCompensationOverrides
    // and the user's configured CompensationMode.
    public void ClearPerSongCompensation()
    {
        PerSongCompensation = null;
        PerSongCompensationMode = null;
        _compensationVersion++;
    }

    // Call this whenever InstrumentCompensationOverrides is mutated externally (e.g. from the UI).
    public void InvalidateCompensationCache() => _compensationVersion++;

    // Resolves the final per-instrument compensation int[] for ByInstrument mode.
    // Priority: per-song JSON override > global InstrumentCompensationOverrides > computed averages.
    // Only keys present in the source dict override the default; missing keys keep their average value.
    // Result is cached until _compensationVersion changes.
    private int[] GetEffectiveCompensationArray()
    {
        if (_effectiveCompensationCache != null && _cachedVersion == _compensationVersion)
            return _effectiveCompensationCache;

        var source = PerSongCompensation ?? Plugin.Config.InstrumentCompensationOverrides;
        var arr = (int[])GetCompensationAver().Clone();
        foreach (var (rowId, name) in InstrumentHelper.RowIdToName)
            if (source.TryGetValue(name, out var ms))
                arr[rowId] = ms;

        _cachedVersion = _compensationVersion;
        return _effectiveCompensationCache = arr;
    }

    // Returns the number of milliseconds this bard must wait before sending its event,
    // so that all bards in the ensemble sound in sync despite varying instrument attack times.
    public int GetCompensationNew(int instrument, int note)
    {
        try
        {
            // Per-song mode takes priority over the global config mode.
            var mode = PerSongCompensationMode ?? Plugin.Config.CompensationMode;
            switch (mode)
            {
                case CompensationModes.None:
                    return 0;

                case CompensationModes.ByInstrument:
                    {
                        // Each instrument has one user-configurable average delay value.
                        // The slowest instrument gets 0 extra delay; faster instruments wait
                        // longer so they all sound at the same real-world time.
                        var compensation = GetEffectiveCompensationArray();
                        var max = compensation.Max(i => i);
                        return max - compensation[instrument];
                    }

                case CompensationModes.ByInstrumentNote:
                    {
                        // Uses hardcoded per-note attack times measured from the game's audio samples
                        // (time until the sample exceeds 10% of its peak volume).
                        // The globally slowest note defines the deadline; every other note is delayed
                        // by (max − its own attack time) so all note onsets align across the ensemble.
                        // Non-note events (note < 0) use the instrument's fastest note as reference,
                        // ensuring they are scheduled ahead of any note event for that instrument.
                        return note < 0 ? CompensationMax - CompensationMin[instrument] : CompensationMax - Compensation10pct[instrument][note];
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, $"error when getting Compensation value. instrument: {instrument}, note: {note}");
            return 0;
        }
    }

    /// <summary>
    /// Per-note attack times measured from FFXIV's audio samples: time (ms) until each note's
    /// waveform first exceeds 10% of its peak volume. Indexed as [instrument rowId][note 0-36].
    /// Used by ByInstrumentNote mode; not user-editable.
    /// </summary>
    private static readonly byte[][] Compensation10pct =
    {
        Enumerable.Repeat((byte)0, 37).ToArray(),
        new byte[] // 1 / Harp / 047harp.scd
        {
            66, 66, 66, 66, 66, 66, 66, 66, 66, 66, 66, // scd3
            65, 65, 65, 65, 65, 65, 65, 65, 65,         // scd2
            66, 66, 66, 66, 66, 66, 66, 66, 66,         // scd0
            65, 65, 65, 65, 65, 65, 65, 65              // scd1
        },
        new byte[] // 2 / Piano / 001grandpiano.scd
        {
            70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, // scd4
            70, 70, 70, 70, 70,                                             // scd3
            70, 70, 70, 70, 70,                                             // scd1
            69, 69, 69, 69,                                                 // scd2
            70, 70, 70, 70, 70, 70, 70                                      // scd0
        },
        new byte[] // 3 / Lute / 026steelguitar.scd
        {
            81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, // scd3
            78, 78, 78, 78, 78, 78, 78,                                 // scd2
            79, 79, 79, 79, 79, 79, 79,                                 // scd1
            79, 79, 79, 79, 79, 79, 79, 79                              // scd0
        },
        new byte[] // 4 / Fiddle / 046pizzicato.scd
        {
            78, 78, 78,                        // scd1
            68, 68, 68, 68,                    // scd0
            79, 79, 79, 79,                    // scd2
            68, 68, 68,                        // scd3
            67, 67, 67, 67, 67,                // scd4
            72, 72, 72,                        // scd5
            69, 69, 69, 69, 69, 69,            // scd6
            71, 71, 71, 71, 71, 71, 71, 71, 71 // scd7
        },
        new byte[] // 5 / Flute / 074flute.scd
        {
            70, 70, 70, 70, 70, 70, 70, 70, 70, 70, // scd5
            79, 79, 79, 79, 79,                     // scd4
            72, 72, 72, 72,                         // scd3
            77, 77, 77, 77,                         // scd2
            80, 80, 80, 80, 80,                     // scd1
            82, 82, 82, 82, 82, 82, 82, 82, 82      // scd0
        },
        new byte[] // 6 / Oboe / 069oboe.scd
        {
            75, 75, 75, 75, 75, 75, 75, 75, 75, 75,                    // scd4
            74, 74, 74, 74, 74,                                        // scd2
            73, 73, 73,                                                // scd3
            70, 70, 70, 70,                                            // scd1
            68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68 // scd0
        },
        new byte[] // 7 / Clarinet / 072clarinet.scd
        {
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,                           // scd4
            4, 4, 4, 4, 4, 4,                                          // scd3
            7, 7, 7, 7, 7,                                             // scd2
            13, 13, 13, 13, 13, 13, 13,                                // scd1
            7, 7, 7, 7, 7, 7, 7, 7,                                    // scd0
        },
        new byte[] // 8 / Fife / 073piccolo.scd
        {
            70, 70, 70, 70, 70, 70, 70, 70,                    // scd4
            73, 73, 73, 73, 73, 73,                            // scd1
            83, 83, 83,                                        // scd0
            88, 88, 88, 88, 88, 88, 88,                        // scd2
            85, 85, 85, 85, 85, 85, 85, 85, 85, 85, 85, 85, 85 // scd3
        },
        new byte[] // 9 / Panpipes / 076panflute.scd
        {
            69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, // scd2
            71, 71, 71, 71,                                                 // scd1
            71, 71, 71, 71,                                                 // scd3
            70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70              // scd0
        },
        new byte[] // 10 / Timpani / 048timpani.scd
        {
            67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, // scd2
            65, 65, 65, 65, 65, 65, 65, 65,                                 // scd1
            67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67              // scd0
        },
        new byte[] // 11 / Bongo / 097bongo.scd
        {
            69, 69, 69, 69, 69, 69, 69, 69,                            // scd2
            65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65,    // scd1
            54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54 // scd0
        },
        new byte[] // 12 / Bass Drum / 098bd.scd
        {
            71, 71, 71, 71, 71, 71, 71,                        // scd3
            65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65,        // scd2
            55, 55, 55, 55, 55, 55,                            // scd0
            46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46 // scd1
        },
        new byte[] // 13 / Snare Drum / 099snare.scd
        {
            71, 71, 71, 71, 71, 71, 71, 71, 71, 71, 71, 71,    // scd0
            63, 63, 63, 63, 63, 63, 63, 63,                    // scd1
            62, 62, 62, 62,                                    // scd2
            55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55 // scd3
        },
        new byte[] // 14 / Cymbal / 100cymbal.scd
        {
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 // scd0
        },
        new byte[] // 15 / Trumpet / 057trumpet.scd
        {
            17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, // scd2
            23, 23, 23, 23,                                             // scd1
            8, 8, 8, 8, 8,                                              // scd4
            6, 6, 6,                                                    // scd5
            4, 4, 4, 4,                                                 // scd3
            20, 20, 20, 20, 20, 20                                      // scd0
        },
        new byte[] // 16 / Trombone / 058trombone.scd
        {
            9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, // scd0
            8, 8, 8, 8, 8,                               // scd1
            9, 9, 9, 9,                                  // scd2
            5, 5, 5,                                     // scd3
            9, 9, 9, 9, 9, 9, 9, 9, 9, 9                 // scd4
        },
        new byte[] // 17 / Tuba / 059tuba.scd
        {
            15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, // scd0
            7, 7, 7,                                    // scd5
            13, 13, 13, 13, 13,                         // scd1
            4, 4, 4, 4, 4,                              // scd3
            7, 7, 7, 7, 7,                              // scd2
            27, 27, 27, 27, 27, 27, 27, 27              // scd4
        },
        new byte[] // 18 / Horn / 061frenchhorn.scd
        {
            5, 5, 5, 5, 5, 5, 5, 5, 5,                  // scd0
            10, 10, 10, 10, 10, 10,                     // scd3
            10, 10, 10,                                 // scd4
            6, 6, 6, 6,                                 // scd2
            6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 // scd1
        },
        new byte[] // 19 / Saxophone / 066altosax.scd
        {
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, // scd3
            6, 6, 6, 6, 6, 6,                   // scd2
            7, 7, 7, 7, 7, 7, 7, 7,             // scd1
            8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8     // scd0
        },
        new byte[] // 20 / Violin / 041violin.scd
        {
            23, 23, 23, 23, 23, 23, 23, 23, 23, 23, // scd0
            18, 18, 18, 18, 18, 18, 18, 18, 18,     // scd1
            18, 18, 18, 18, 18, 18, 18,             // scd4
            14, 14, 14, 14,                         // scd3
            11, 11, 11, 11, 11, 11, 11              // scd2
        },
        new byte[] // 21 / Viola / 042viola.scd
        {
            16, 16, 16, 16, 16, 16, 16, 16, 16, // scd0
            15, 15, 15, 15, 15, 15,             // scd1
            15, 15, 15, 15, 15, 15, 15, 15,     // scd2
            13, 13, 13, 13, 13, 13, 13, 13,     // scd3
            13, 13, 13, 13, 13, 13              // scd4
        },
        new byte[] // 22 / Cello / 043cello.scd
        {
            21, 21, 21, 21, 21, 21,            // scd0
            9, 9, 9, 9, 9, 9, 9, 9,            // scd1
            17, 17, 17, 17, 17, 17, 17, 17,    // scd2
            13, 13, 13, 13, 13, 13,            // scd3
            17, 17, 17, 17, 17, 17, 17, 17, 17 // scd4
        },
        new byte[] // 23 / Double Bass / 044contrabass.scd
        {
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5,      // scd0
            10, 10, 10, 10, 10,                // scd1
            11, 11, 11, 11, 11, 11, 11,        // scd2
            8, 8, 8, 8, 8, 8,                  // scd3
            12, 12, 12, 12, 12, 12, 12, 12, 12 // scd4
        },
        new byte[] // 24 / Electric Guitar: Overdriven / 030driveguitar.scd
        {
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, // scd3
            1, 1, 1, 1, 1, 1,                // scd0
            3, 3, 3, 3, 3,                   // scd4
            3, 3, 3, 3, 3, 3, 3, 3, 3,       // scd1
            3, 3, 3, 3, 3, 3                 // scd2
        },
        new byte[] // 25 / Electric Guitar: Clean / 028cleanguitar.scd
        {
            6, 6, 6, 6, 6, 6, 6, 6, 6, 6, // scd4
            4, 4, 4, 4, 4, 4, 4,          // scd0
            4, 4, 4, 4, 4, 4, 4,          // scd3
            6, 6, 6, 6, 6, 6, 6,          // scd1
            4, 4, 4, 4, 4, 4              // scd2
        },
        new byte[] // 26 / Electric Guitar: Muted / 029muteguitar.scd
        {
            65, 65, 65,     // scd3
            60, 60, 60,     // scd9
            68, 68, 68, 68, // scd6
            63, 63, 63, 63, // scd2
            74, 74, 74, 74, // scd8
            74, 74, 74, 74, // scd5
            68, 68, 68, 68, // scd1
            69, 69, 69, 69, // scd7
            70, 70, 70, 70, // scd4
            68, 68, 68      // scd0
        },
        new byte[] // 27 / Electric Guitar: Power Chords / 031powerguitar.scd
        {
            8, 8, 8, 8, 8, 8, 8, 8, 8, // scd0
            1, 1, 1, 1, 1, 1, 1, 1,    // scd1
            6, 6, 6, 6, 6,             // scd2
            0, 0, 0, 0, 0, 0, 0,       // scd3
            6, 6, 6, 6, 6, 6, 6, 6     // scd4
        },
        new byte[] // 28 / Electric Guitar: Special / 032fxguitar.scd
        {
            11, 11, 11, 11, 11, 11,    // scd0
            0, 0, 0, 0, 0, 0, 0,       // scd1
            0, 0, 0, 0, 0, 0, 0,       // scd2
            5, 5, 5, 5, 5, 5, 5, 5, 5, // scd3
            0, 0, 0, 0, 0, 0, 0, 0     // scd4
        }
    };

    /// <summary>
    ///     Takes the time ms when first sample volume > 0% * max volume
    /// </summary>
    private static readonly byte[][] Compensation0pct =
    {
        Enumerable.Repeat((byte)0, 37).ToArray(),
        new byte[] // 1 / Harp / 047harp.scd
        {
            65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, // scd3
            64, 64, 64, 64, 64, 64, 64, 64, 64,         // scd2
            65, 65, 65, 65, 65, 65, 65, 65, 65,         // scd0
            64, 64, 64, 64, 64, 64, 64, 64              // scd1
        },
        new byte[] // 2 / Piano / 001grandpiano.scd
        {
            69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, // scd4
            68, 68, 68, 68, 68,                                             // scd3
            68, 68, 68, 68, 68,                                             // scd1
            68, 68, 68, 68,                                                 // scd2
            68, 68, 68, 68, 68, 68, 68                                      // scd0
        },
        new byte[] // 3 / Lute / 026steelguitar.scd
        {
            72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, // scd3
            72, 72, 72, 72, 72, 72, 72,                                 // scd2
            73, 73, 73, 73, 73, 73, 73,                                 // scd1
            72, 72, 72, 72, 72, 72, 72, 72                              // scd0
        },
        new byte[] // 4 / Fiddle / 046pizzicato.scd
        {
            65, 65, 65,                        // scd1
            66, 66, 66, 66,                    // scd0
            66, 66, 66, 66,                    // scd2
            65, 65, 65,                        // scd3
            65, 65, 65, 65, 65,                // scd4
            65, 65, 65,                        // scd5
            65, 65, 65, 65, 65, 65,            // scd6
            65, 65, 65, 65, 65, 65, 65, 65, 65 // scd7
        },
        new byte[] // 5 / Flute / 074flute.scd
        {
            65, 65, 65, 65, 65, 65, 65, 65, 65, 65, // scd5
            65, 65, 65, 65, 65,                     // scd4
            65, 65, 65, 65,                         // scd3
            65, 65, 65, 65,                         // scd2
            67, 67, 67, 67, 67,                     // scd1
            66, 66, 66, 66, 66, 66, 66, 66, 66      // scd0
        },
        new byte[] // 6 / Oboe / 069oboe.scd
        {
            67, 67, 67, 67, 67, 67, 67, 67, 67, 67,                    // scd4
            67, 67, 67, 67, 67,                                        // scd2
            68, 68, 68,                                                // scd3
            64, 64, 64, 64,                                            // scd1
            64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64 // scd0
        },
        new byte[] // 7 / Clarinet / 072clarinet.scd
        {
            126, 126, 126, 126, 126, 126, 126, 126, 126, 126, 126, // scd4
            63, 63, 63, 63, 63, 63,                                // scd3
            64, 64, 64, 64, 64,                                    // scd2
            66, 66, 66, 66, 66, 66, 66,                            // scd1
            66, 66, 66, 66, 66, 66, 66, 66                         // scd0
        },
        new byte[] // 8 / Fife / 073piccolo.scd
        {
            65, 65, 65, 65, 65, 65, 65, 65,                    // scd4
            67, 67, 67, 67, 67, 67,                            // scd1
            66, 66, 66,                                        // scd0
            67, 67, 67, 67, 67, 67, 67,                        // scd2
            68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68 // scd3
        },
        new byte[] // 9 / Panpipes / 076panflute.scd
        {
            63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, // scd2
            66, 66, 66, 66,                                                 // scd1
            66, 66, 66, 66,                                                 // scd3
            66, 66, 66, 66, 66, 66, 66, 66, 66, 66, 66, 66, 66              // scd0
        },
        new byte[] // 10 / Timpani / 048timpani.scd
        {
            67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, // scd2
            65, 65, 65, 65, 65, 65, 65, 65,                                 // scd1
            67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67              // scd0
        },
        new byte[] // 11 / Bongo / 097bongo.scd
        {
            69, 69, 69, 69, 69, 69, 69, 69,                            // scd2
            65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65,    // scd1
            53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53 // scd0
        },
        new byte[] // 12 / Bass Drum / 098bd.scd
        {
            70, 70, 70, 70, 70, 70, 70,                        // scd3
            64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64,        // scd2
            53, 53, 53, 53, 53, 53,                            // scd0
            46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46 // scd1
        },
        new byte[] // 13 / Snare Drum / 099snare.scd
        {
            70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70,    // scd0
            63, 63, 63, 63, 63, 63, 63, 63,                    // scd1
            62, 62, 62, 62,                                    // scd2
            55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55 // scd3
        },
        new byte[] // 14 / Cymbal / 100cymbal.scd
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 // scd0
        },
        new byte[] // 15 / Trumpet / 057trumpet.scd
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // scd2
            3, 3, 3, 3,                                  // scd1
            7, 7, 7, 7, 7,                               // scd4
            4, 4, 4,                                     // scd5
            1, 1, 1, 1,                                  // scd3
            13, 13, 13, 13, 13, 13                       // scd0
        },
        new byte[] // 16 / Trombone / 058trombone.scd
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // scd0
            3, 3, 3, 3, 3,                               // scd1
            4, 4, 4, 4,                                  // scd2
            1, 1, 1,                                     // scd3
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3                 // scd4
        },
        new byte[] // 17 / Tuba / 059tuba.scd
        {
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, // scd0
            1, 1, 1,                         // scd5
            1, 1, 1, 1, 1,                   // scd1
            1, 1, 1, 1, 1,                   // scd3
            2, 2, 2, 2, 2,                   // scd2
            2, 2, 2, 2, 2, 2, 2, 2           // scd4
        },
        new byte[] // 18 / Horn / 061frenchhorn.scd
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1,                  // scd0
            1, 1, 1, 1, 1, 1,                           // scd3
            3, 3, 3,                                    // scd4
            1, 1, 1, 1,                                 // scd2
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 // scd1
        },
        new byte[] // 19 / Saxophone / 066altosax.scd
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // scd3
            3, 3, 3, 3, 3, 3,                   // scd2
            2, 2, 2, 2, 2, 2, 2, 2,             // scd1
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3     // scd0
        },
        new byte[] // 20 / Violin / 041violin.scd
        {
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, // scd0
            1, 1, 1, 1, 1, 1, 1, 1, 1,    // scd1
            3, 3, 3, 3, 3, 3, 3,          // scd4
            1, 1, 1, 1,                   // scd3
            2, 2, 2, 2, 2, 2, 2           // scd2
        },
        new byte[] // 21 / Viola / 042viola.scd
        {
            4, 4, 4, 4, 4, 4, 4, 4, 4, // scd0
            3, 3, 3, 3, 3, 3,          // scd1
            3, 3, 3, 3, 3, 3, 3, 3,    // scd2
            3, 3, 3, 3, 3, 3, 3, 3,    // scd3
            2, 2, 2, 2, 2, 2           // scd4
        },
        new byte[] // 22 / Cello / 043cello.scd
        {
            6, 6, 6, 6, 6, 6,         // scd0
            1, 1, 1, 1, 1, 1, 1, 1,   // scd1
            3, 3, 3, 3, 3, 3, 3, 3,   // scd2
            2, 2, 2, 2, 2, 2,         // scd3
            3, 3, 3, 3, 3, 3, 3, 3, 3 // scd4
        },
        new byte[] // 23 / Double Bass / 044contrabass.scd
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // scd0
            2, 2, 2, 2, 2,                // scd1
            2, 2, 2, 2, 2, 2, 2,          // scd2
            2, 2, 2, 2, 2, 2,             // scd3
            3, 3, 3, 3, 3, 3, 3, 3, 3     // scd4
        },
        new byte[] // 24 / Electric Guitar: Overdriven / 030driveguitar.scd
        {
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, // scd3
            1, 1, 1, 1, 1, 1,                // scd0
            3, 3, 3, 3, 3,                   // scd4
            2, 2, 2, 2, 2, 2, 2, 2, 2,       // scd1
            3, 3, 3, 3, 3, 3                 // scd2
        },
        new byte[] // 25 / Electric Guitar: Clean / 028cleanguitar.scd
        {
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, // scd4
            0, 0, 0, 0, 0, 0, 0,          // scd0
            3, 3, 3, 3, 3, 3, 3,          // scd3
            1, 1, 1, 1, 1, 1, 1,          // scd1
            3, 3, 3, 3, 3, 3              // scd2
        },
        new byte[] // 26 / Electric Guitar: Muted / 029muteguitar.scd
        {
            64, 64, 64,     // scd3
            59, 59, 59,     // scd9
            67, 67, 67, 67, // scd6
            62, 62, 62, 62, // scd2
            64, 64, 64, 64, // scd8
            66, 66, 66, 66, // scd5
            59, 59, 59, 59, // scd1
            65, 65, 65, 65, // scd7
            66, 66, 66, 66, // scd4
            66, 66, 66      // scd0
        },
        new byte[] // 27 / Electric Guitar: Power Chords / 031powerguitar.scd
        {
            6, 6, 6, 6, 6, 6, 6, 6, 6, // scd0
            1, 1, 1, 1, 1, 1, 1, 1,    // scd1
            6, 6, 6, 6, 6,             // scd2
            0, 0, 0, 0, 0, 0, 0,       // scd3
            6, 6, 6, 6, 6, 6, 6, 6     // scd4
        },
        new byte[] // 28 / Electric Guitar: Special / 032fxguitar.scd
        {
            0, 0, 0, 0, 0, 0,          // scd0
            0, 0, 0, 0, 0, 0, 0,       // scd1
            0, 0, 0, 0, 0, 0, 0,       // scd2
            0, 0, 0, 0, 0, 0, 0, 0, 0, // scd3
            0, 0, 0, 0, 0, 0, 0, 0     // scd4
        }
    };

    // Global maximum attack time across all instruments and notes (ms).
    // Used as the synchronisation deadline: every event is delayed by (max − its own attack time).
    public static readonly int CompensationMax = Compensation10pct.Max(i => i.Max());
    // Fastest (minimum) attack time per instrument across all its notes.
    // Used for non-note events so they are always scheduled before any note event of that instrument.
    public static readonly int[] CompensationMin = Compensation10pct.Select(i => (int)i.Min()).ToArray();
    private static int[]? _compensationAverCache;
    // Average attack time per instrument across all its notes.
    // Used as the default compensation baseline in ByInstrument mode when no override is configured.
    public static int[] GetCompensationAver() =>
        _compensationAverCache ??= Compensation10pct.Select(i => (int)Math.Round(i.Average(b => (double)b))).ToArray();

    public static Dictionary<int, int> DefaultInstrumentCompensations => new()
    {
        //[0] = 105,
        [1] = 85, // Harp
        [2] = 90, // Piano
        [3] = 105, // Lute
        [4] = 90, // Fiddle
        [5] = 95, // Flute
        [6] = 95, // Oboe
        [7] = 95, // Clarinet
        [8] = 95, // Fife
        [9] = 90, // Panpipes
        [10] = 90, // Timpani
        [11] = 80, // Bongo
        [12] = 80, // BassDrum
        [13] = 85, // SnareDrum
        [14] = 30, // Cymbal
        [15] = 30, // Trumpet
        [16] = 30, // Trombone
        [17] = 30, // Tuba
        [18] = 30, // Horn
        [19] = 30, // Saxophone
        [20] = 30, // Violin
        [21] = 30, // Viola
        [22] = 30, // Cello
        [23] = 30, // DoubleBass
        [24] = 30, // ElectricGuitarOverdriven
        [25] = 30, // ElectricGuitarClean
        [26] = 30, // ElectricGuitarMuted
        [27] = 30, // ElectricGuitarPowerChords
        [28] = 30, // ElectricGuitarSpecial
    };

    internal void InvokeEnsembleStop() => EnsembleStopped?.Invoke();

    public void Dispose()
    {
        NetworkEnsembleHook?.Dispose();
        //UpdateMetronomeHook?.Dispose();
    }
}
