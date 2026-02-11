using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Lumina.Excel.Sheets;

using MidiBard.Extensions.Dalamud.PerformSheet;
using MidiBard.Extensions.String;
using MidiBard.Util;

namespace MidiBard.Control.CharacterControl;

internal class InstrumentSwitcher
{
    private Plugin Plugin { get; }
    public bool SwitchingInstrument { get; private set; }
    private readonly Regex midiTrackRegex = new Regex(@"^#(?<ins>.*?)(?<trans>[-|+][0-9]+)?#(?<name>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public InstrumentSwitcher(Plugin plugin)
    {
        Plugin = plugin;
    }

    public void SwitchToContinue(uint instrumentId)
    {
        Task.Run(async () =>
        {
            try
            {
                var isPlaying = Plugin.IsPlaying;
                Plugin.CurrentBardPlayback.Stop();
                await SwitchToAsync(instrumentId);
                if (isPlaying) Plugin.CurrentBardPlayback.Start();
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "Error when switching instrument");
            }
        });
    }

    public async Task SwitchToAsync(uint instrumentId, int timeOut = 3000)
    {
        if (Plugin.PlayingGuitar)
        {
            var instrument = Plugin.Instruments[instrumentId];
            if (instrument.IsGuitar)
            {
                Playlib.GuitarSwitchTone(instrument.GuitarTone);
                return;
            }
        }

        if (Plugin.CurrentInstrument == instrumentId)
            return;

        SwitchingInstrument = true;
        var sw = Stopwatch.StartNew();
        try
        {
            await DoSwitchInstrumentAsync(instrumentId, timeOut);
            DalamudApi.PluginLog.Debug($"instrument switching succeed in {sw.Elapsed.TotalMilliseconds} ms");
            //ImGuiUtil.AddNotification(NotificationType.Success, $"Switched to {MidiBard.InstrumentStrings[instrumentId]}");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, $"instrument switching failed in {sw.Elapsed.TotalMilliseconds} ms");
        }
        finally
        {
            SwitchingInstrument = false;
        }
    }

    private async Task DoSwitchInstrumentAsync(uint instrumentId, int timeOut)
    {
        if (Plugin.CurrentInstrument != 0)
        {
            PerformActions.DoPerformActionOnTick(0);
            await Coroutine.WaitUntil(() => Plugin.CurrentInstrument == 0, timeOut);
        }

        PerformActions.DoPerformActionOnTick(instrumentId);
        await Coroutine.WaitUntil(() => Plugin.CurrentInstrument == instrumentId, timeOut);
        await Task.Delay(200);
    }

    // private void DoSwitchInstrument(uint instrumentId, int timeOut)
    // {
    //     if (Plugin.CurrentInstrument != 0)
    //     {
    //         PerformActions.DoPerformActionOnTick(0);
    //         Coroutine.WaitUntilSync(() => Plugin.CurrentInstrument == 0, timeOut);
    //     }

    //     PerformActions.DoPerformActionOnTick(instrumentId);
    //     Coroutine.WaitUntilSync(() => Plugin.CurrentInstrument == instrumentId, timeOut);
    //     Thread.Sleep(200);
    // }

    public string ParseSongName(string inputString, out uint? instrumentId, out int? transpose)
    {
        var match = midiTrackRegex.Match(inputString);
        if (match.Success)
        {
            var capturedInstrumentString = match.Groups["ins"].Value;
            var capturedTransposeString = match.Groups["trans"].Value;
            var capturedSongName = match.Groups["name"].Value;

            DalamudApi.PluginLog.Debug($"input: \"{inputString}\", instrumentString: {capturedInstrumentString}, transposeString: {capturedTransposeString}");
            transpose = int.TryParse(capturedTransposeString, out var t) ? t : null;
            instrumentId = TryParseInstrumentName(capturedInstrumentString, out var id) ? id : null;
            return !string.IsNullOrEmpty(capturedSongName) ? capturedSongName : inputString;
        }

        instrumentId = null;
        transpose = null;
        return inputString;
    }

    public bool TryParseInstrumentName(string capturedInstrumentString, out uint instrumentId)
    {
        var bmpNameEqual = TrackInfo.GetInstrumentIDByName(capturedInstrumentString);
        string lookupstr = capturedInstrumentString.ToLower().Trim();

        Perform? sheet = Plugin.InstrumentSheet.FirstOrDefault(i => i.GetGameProgramName().ContainsIgnoreCase(lookupstr) ||
                                                                      i.GetGameProgramName().StartsWith(lookupstr) ||
                                                                      i.GetGameProgramName().Equals(lookupstr, StringComparison.Ordinal));
        var rowId = bmpNameEqual ?? sheet?.RowId;
        DalamudApi.PluginLog.Debug($"idFromBmpName: {bmpNameEqual}, equal: {sheet?.GetGameProgramName()}, finalId: {rowId}");
        if (rowId is null)
        {
            instrumentId = 0;
            return false;
        }
        else
        {
            instrumentId = rowId.Value;
            return true;
        }
    }

    internal async Task WaitSwitchInstrumentForSong(string songName)
    {
        // if (MidiBard.CurrentPlayback == null) return;
        if (Plugin.Config.bmpTrackNames)
        {
            Plugin.CurrentBardPlayback.ApplyTransposeToTracks();
            Plugin.CurrentBardPlayback.UpdateGuitarToneByConfig();
            Plugin.CurrentBardPlayback.SyncTrackStatusWithMidiFileConfig();

            uint instrumentId = Plugin.CurrentBardPlayback.GetInstrumentId();
            await SwitchToAsync(instrumentId);
            return;
        }

        // TODO: refactor to include this inside CurrentPlayback functions ApplyTransposeToTracks GetInstrumentId
        ParseSongName(songName, out uint? idFromSongName, out var transposeGlobal);

        if (Plugin.Config.autoTransposeBySongName)
        {
            Plugin.Config.TransposeGlobal = transposeGlobal != null ? (int)transposeGlobal : 0;
        }

        if (Plugin.Config.autoSwitchInstrumentBySongName && idFromSongName is uint songNameInstrumentId)
        {
            await SwitchToAsync(songNameInstrumentId);
        }
    }
}
