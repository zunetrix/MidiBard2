using System;
using System.Linq;
using System.Text;

using Dalamud.Hooking;

namespace MidiBard.Managers;

#if DEBUG
class NetworkManager : IDisposable
{
    private readonly Plugin Plugin;

    delegate IntPtr sub_14070A1C0(uint sourceId, IntPtr data);
    // private readonly Hook<sub_14070A1C0> soloReceivedHook;

    delegate IntPtr sub_14070A230(uint sourceId, IntPtr data);
    private readonly Hook<sub_14070A230> ensembleReceivedHook;

    delegate void sub_14119B2E0(IntPtr a1);
    // private readonly Hook<sub_14119B2E0> soloSendHook;

    delegate void sub_14119B120(IntPtr a1);
    // private readonly Hook<sub_14119B120> ensembleSendHook;

    // [StructLayout(LayoutKind.Explicit, Size = 1)]
    // public struct Note
    // {
    //    [FieldOffset(0)] public byte note;

    //    public override string ToString()
    //    {
    //        return new Melanchall.DryWetMidi.MusicTheory.Note();
    //    }
    // }

    public NetworkManager(Plugin plugin)
    {
        Plugin = plugin;

        // ensembleSendHook = DalamudApi.GameInteropProvider.HookFromAddress<sub_14119B120>(Offsets.EnsembleSendHandler, (dataptr) =>
        // {
        //     try
        //     {
        //         EnsembleSend(dataptr);
        //     }
        //     catch (Exception e)
        //     {
        //         DalamudApi.PluginLog.Error(e, $"error in {nameof(ensembleSendHook)}");
        //     }

        //     ensembleSendHook.Original(dataptr);
        // });

        // soloSendHook = DalamudApi.GameInteropProvider.HookFromAddress<sub_14119B2E0>(Offsets.SoloSendHandler, (dataptr) =>
        // {
        //     try
        //     {
        //         SoloSend(dataptr);
        //     }
        //     catch (Exception e)
        //     {
        //         DalamudApi.PluginLog.Error(e, "error in solo send handler hook");
        //     }

        //     soloSendHook.Original(dataptr);
        // });

        // soloReceivedHook = DalamudApi.GameInteropProvider.HookFromAddress<sub_14070A1C0>(Offsets.SoloReceivedHandler, (id, data) =>
        // {
        //     try
        //     {
        //         SoloRecv(id, data);
        //     }
        //     catch (Exception e)
        //     {
        //         DalamudApi.PluginLog.Error(e, "error in solo recv handler hook");
        //     }
        //     return soloReceivedHook.Original(id, data);
        // });

        ensembleReceivedHook = DalamudApi.GameInteropProvider.HookFromAddress<sub_14070A230>(Offsets.EnsembleReceivedHandler, (id, data) =>
        {
            try
            {
                EnsembleRecv(id, data);
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error in ensemble recv handler hook");
            }
            return ensembleReceivedHook.Original(id, data);
        });

        // ensembleSendHook?.Enable();
        // soloSendHook?.Enable();
        // soloReceivedHook?.Enable();
        ensembleReceivedHook?.Enable();
    }

    public void Dispose()
    {
        // soloSendHook?.Dispose();
        // ensembleSendHook?.Dispose();
        // soloReceivedHook?.Dispose();
        ensembleReceivedHook?.Dispose();
    }

    private void SoloSend(IntPtr dataptr)
    {
        var l = 10;
        LogNotes("SoloSend", dataptr, l);
    }

    private void SoloRecv(uint sourceId, IntPtr data)
    {
        // var ipc = Marshal.PtrToStructure<SoloPerformanceIpc>(data);
        // DalamudApi.PluginLog.Information($"[{nameof(SoloRecv)}] {toString(ipc.NoteNumbers)} : {toString(ipc.NoteTones)}");
    }

    private void EnsembleSend(IntPtr dataptr)
    {
#if DEBUG
        LogNotes("EnsembleSend", dataptr, 60);
#endif
    }

    private void EnsembleRecv(uint sourceId, IntPtr data)
    {
        var firstEnsemblePacket = !Plugin.EnsembleManager.EnsembleRecvTime.Any();
        if (firstEnsemblePacket)
        {
            Plugin.Config.EnsembleIndicatorDelay = (float)Plugin.EnsembleManager.EnsembleTimer.Elapsed.TotalSeconds + 1.15f;
        }

        //  DalamudApi.PluginLog.Warning($"EnsembleRecv {EnsembleManager.EnsembleTimer.Elapsed}");
        //var ipc = Marshal.PtrToStructure<EnsemblePerformanceIpc>(data);
        Plugin.EnsembleManager.EnsembleRecvTime.Add(Plugin.EnsembleManager.EnsembleTimer.Elapsed);
        //foreach (var perCharacterData in ipc.EnsembleCharacterDatas.Where(i => i.IsValid))
        //{
        //    //DalamudApi.PluginLog.Information($"[{nameof(EnsembleRecv)}] {perCharacterData.CharacterId:X} {perCharacterData.NoteNumbers.toString()}");
        //}
    }

    private unsafe void LogNotes(string label, IntPtr dataptr, int count)
    {
        Span<byte> notes = new Span<byte>((dataptr + 0x10).ToPointer(), count);
        Span<byte> tones = new Span<byte>((dataptr + 0x10 + count).ToPointer(), count);
        //for (int i = 0; i < notes.Length; i++)
        //{
        //    if (notes[i] is not (0xFF or 0xFE))
        //    {
        //        tones[i] = 3;
        //    }
        //}
        StringBuilder sb = new StringBuilder();
        sb.Append($"[{label}] ");
        for (int i = 0; i < count; i++)
        {
            var t = tones[i] switch
            {
                0 => "A",
                1 => "B",
                2 => "C",
                3 => "D",
                4 => "E",
                _ => throw new ArgumentOutOfRangeException()
            };

            var s = notes[i] switch
            {
                0xff => "    ",
                0xfe => "----",
                var n => $"{n:00} {t}"
            };

            sb.Append(s + "|");
        }

        //DalamudApi.PluginLog.Information($"[{nameof(SoloSend)}] {notes.toString()} : {tones.toString()}");
        DalamudApi.PluginLog.Information(sb.ToString());
    }
}
#endif
