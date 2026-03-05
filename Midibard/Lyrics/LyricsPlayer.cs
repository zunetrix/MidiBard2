using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Dalamud.Plugin.Services;

using MidiBard.Control.MidiControl;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.String;

namespace MidiBard.Util.Lyrics;

public class LyricsPlayer : IDisposable
{
    private Plugin Plugin { get; }

    public Lyrics CurrentLyrics { get; set; }

    public long Offset { get; set; }
    public int LrcIdx = -1;
    internal int LRCDeltaTime = 50;
    private bool SongTitlePosted = false;

    private static readonly Regex ParsePoster = new Regex(@"^(?<poster>.+?):(?<text>.+)$", RegexOptions.Compiled);

    public LyricsPlayer(Plugin plugin)
    {
        Plugin = plugin;
        CurrentLyrics = new Lyrics();
        DalamudApi.Framework.Update += Tick;
    }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= Tick;
    }

    public void ResetState()
    {
        CurrentLyrics = new Lyrics();
        Offset = 0;
        SongTitlePosted = false;
        LRCDeltaTime = 50;
        LrcIdx = -1;
    }

    public void Play()
    {
        if (!HasLyric()) return;

        // if (!DalamudApi.PartyList.IsInParty())
        // {
        //     DalamudApi.ChatGui.Print(string.Format("[MidiBard 2] Not in a party, Lyrics will not be posted."));
        //     return;
        // }

        // Assume usual delay between sending and other clients receiving the message would be ~100ms
        LRCDeltaTime = 100;
        CurrentLyrics.Sort();

        if (Plugin.MidiPlayerControl._status != MidiPlayerControl.MidiPlayerStatus.Paused)
        {
            LrcIdx = -1;
        }
    }

    public void Stop()
    {
        ResetState();
    }

    public bool LyricsLoaded()
    {
        return HasLyric();
        // return DalamudApi.PartyList.IsInParty() && HasLyric();
    }

    public void LoadLyrics(string midiFilePath)
    {
        if (!Plugin.Config.playLyrics) return;

        bool loadSuccessfull = true;
        var lrcFilePath = Path.ChangeExtension(midiFilePath, "lrc");
        if (!File.Exists(lrcFilePath)) return;

        ResetState();

        try
        {
            CurrentLyrics = new Lyrics(lrcFilePath);
            Plugin.Ui.LyricsEditorWindow.LoadLrcToEditor(CurrentLyrics);
        }
        catch
        {
            ResetState();
            loadSuccessfull = false;
        }

        if (loadSuccessfull)
        {
            DalamudApi.PluginLog.Debug($"Load LRC: {lrcFilePath}");
            DalamudApi.ChatGui.Print($"[MidiBard 2] Lyrics Loaded: {lrcFilePath}");
        }
    }

    public bool HasLyric()
    {
        return CurrentLyrics?.LrcLines.Count > 0;
    }

    /// <summary>
    /// process lrc line to get poster character name and lyric
    /// </summary>
    /// <param name="line">input lrc line without timestamp</param>
    /// <param name="characterName">parsed character name if exist</param>
    /// <param name="lyric">parsed lyric text ready to post</param>
    /// <returns>Input line has a character name</returns>
    private bool ProcessLine(string line, out string characterName, out string lyric)
    {
        var match = ParsePoster.Match(line);
        if (match.Success)
        {
            characterName = match.Groups["poster"].Value;
            lyric = match.Groups["text"].Value;
            return true;
        }

        characterName = "";
        lyric = line;

        return false;
    }

    internal void ChangeLRCDeltaTime(int delta)
    {
        if (!Plugin.CurrentBardPlayback.IsRunning)
        {
            LRCDeltaTime = 100;
            return;
        }

        LRCDeltaTime += delta;
    }

    public void EnsembleStart()
    {
        // a hack way to get ensemble delay, see MidiFilePlot.cs:90
        Offset += (long)(4.045 * 1000);
        // DalamudApi.PluginLog.Info("LRC Offset: " + Plugin.LyricsPlayer.Offset);
    }

    public void Tick(IFramework framework)
    {
        try
        {
            if (!Plugin.Config.playLyrics || Plugin.MidiPlayerControl._status != MidiPlayerControl.MidiPlayerStatus.Playing || !HasLyric())
            {
                return;
            }

            var chatComand = Plugin.Config.GetChatCommand(Plugin.Config.LyricsChatTarget);
            var isInParty = DalamudApi.PartyList.IsInParty();
            var isPartyLeader = isInParty && DalamudApi.PartyList.IsPartyLeader();

            // post song info at the beginning if not in party or is party leader
            if (!SongTitlePosted && (!isInParty || isPartyLeader))
            {
                var sb = new StringBuilder($"♪ {CurrentLyrics.Title} ♪ ");
                if (!string.IsNullOrWhiteSpace(CurrentLyrics.Artist))
                    sb.Append($"Artist: {CurrentLyrics.Artist} ♪ ");
                if (!string.IsNullOrWhiteSpace(CurrentLyrics.Album))
                    sb.Append($"Album: {CurrentLyrics.Album} ♪ ");
                if (!string.IsNullOrWhiteSpace(CurrentLyrics.LrcBy))
                    sb.Append($"Lyric By: {CurrentLyrics.LrcBy} ♪ ");

                var msg = sb.ToString();
                var chatText = $"{chatComand}{msg}";
                Chat.SendMessage(chatText);
                SongTitlePosted = true;
                DalamudApi.PluginLog.Debug("song title posted");
            }

            //TODO: when lrc multiple lines has same timestamp, all lines should be posted
            // post lyrics
            int idx = FindLrcIdx(Plugin.CurrentBardPlayback.GetCurrentTimeSpan());
            if (idx < 0 || idx == LrcIdx || LrcIdx >= CurrentLyrics.LrcLines.Count) return;

            bool isCharacterPostLyric = ProcessLine(CurrentLyrics.LrcLines[idx].Text, out string characterName, out string lyric);
            DalamudApi.PluginLog.Debug($"Lyric ({idx}) Poster: {characterName}, Lyric: {lyric}");

            // Determine if lyric should be posted:
            // - If line has a character name: only post if character name matches current player
            // - If line has no character name: post if not in party OR is party leader
            bool shouldPostLyric;
            if (isCharacterPostLyric)
            {
                // Line has a specific character name - only post if it matches
                shouldPostLyric = DalamudApi.PlayerState.CharacterName.ContainsIgnoreCase(characterName);
            }
            else
            {
                // Line has no specific character name - post if not in party or is party leader
                shouldPostLyric = !isInParty || isPartyLeader;
            }

            DalamudApi.PluginLog.Verbose($"Post Lyrics: {shouldPostLyric}");

            if (shouldPostLyric)
            {
                string msg = $"♪ {lyric} ♪";
                DalamudApi.PluginLog.Verbose(lyric);

                string chatText = $"{chatComand}{msg}";
                Chat.SendMessage(chatText);
            }

            LrcIdx = idx;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error($"exception: {ex}");
        }
    }

    internal int FindLrcIdx(TimeSpan? playbackTime)
    {
        if (playbackTime is null) return -1;
        if (CurrentLyrics?.LrcLines.Count == 0) return -1;
        var currentLrcTime = playbackTime - TimeSpan.FromMilliseconds(Offset) + TimeSpan.FromMilliseconds(LRCDeltaTime);
        if (currentLrcTime < TimeSpan.Zero) return -1;

        var maxBy = CurrentLyrics.LrcLines.MaxBy(i => i.TimeStamp < currentLrcTime ? (TimeSpan?)i.TimeStamp : null);
        // For the 1st line of lyrics
        // Even Func<TSource,TKey> keySelector is NULL, MaxBy always return the 1st element of the list
        // So we need an extra check to avoid posting 1st line immediately
        return currentLrcTime < maxBy.TimeStamp ? -1 : CurrentLyrics.LrcLines.IndexOf(maxBy);
    }

    public string GetLrcExportString()
    {
        return CurrentLyrics?.GetLrcExportString() ?? string.Empty;
    }

    public bool ExportLrcTemplate()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[ar:Artist Name]");
        sb.AppendLine("[ti:Song Title]");
        sb.AppendLine("[al:Album]");
        sb.AppendLine("[by:Lyrics by]");
        sb.AppendLine("[offset:0]");
        sb.AppendLine("[00:07.40]Bard Name:Lyric Line 1");
        sb.AppendLine("[00:08.40]Another Bard Name:Lyric Line 2");
        sb.AppendLine("[00:10.40]Bard Name:Lyric Line 3");
        sb.AppendLine("[00:15.40]Bard Name:Lyric Line 4");
        sb.AppendLine("[00:15.40]Lyric Line 5");
        sb.AppendLine("[00:15.40]Lyric Line 6");
        var fileContent = sb.ToString();

        var filePathInfo = new FileInfo(Plugin.Config.defaultPerformerFolder + $@"\LyricsTemplateExample.lrc");
        try
        {
            File.WriteAllText(filePathInfo.FullName, fileContent);
            DalamudApi.PluginLog.Warning($"{filePathInfo.FullName} Saved");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e.ToString());
            return false;
        }

        return true;
    }
}

