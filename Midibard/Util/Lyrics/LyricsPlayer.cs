using System;
using System.Collections.Generic;
using System.Globalization;
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
    public string Title => LrcMetadata.GetValueOrDefault("ti");
    public string Artist => LrcMetadata.GetValueOrDefault("ar");
    public string Album => LrcMetadata.GetValueOrDefault("al");
    public string LrcBy => LrcMetadata.GetValueOrDefault("by");
    public long Offset { get; set; }
    public int LrcIdx = -1;
    internal int LRCDeltaTime = 50;
    private bool SongTitlePosted = false;
    public string FilePath { get; set; }
    public Dictionary<string, string> LrcMetadata { get; init; }
    public List<LyricEntry> LrcLines { get; init; }

    private static readonly Regex ParseTimeLyric = new Regex(@"^\[(?<min>\d+?):(?<sec>\d{1,2})\.(?<ff>\d+?)\](?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex ParseMetadata = new Regex(@"^\[(?<idTag>.+?):(?<tagContent>.*)\]\s*$", RegexOptions.Compiled);
    private static readonly Regex ParsePoster = new Regex(@"^(?<poster>.+?):(?<text>.+)$", RegexOptions.Compiled);
    public static string ToLrcTime(TimeSpan timeSpan) => $"{(int)timeSpan.TotalMinutes:00}:{timeSpan.Seconds:00}.{timeSpan:ff}";
    public void Sort() => LrcLines.Sort((x, y) => x.TimeStamp.CompareTo(y.TimeStamp));

    public LyricsPlayer(Plugin plugin)
    {
        Plugin = plugin;
        LrcMetadata = new Dictionary<string, string>();
        LrcLines = new List<LyricEntry>();
        DalamudApi.Framework.Update += Tick;
    }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= Tick;
    }

    private static Encoding GetEncoding(string lrcPath)
    {
        var encoding = FileHelpers.GetEncoding(lrcPath);
        DalamudApi.PluginLog.Information(encoding.ToString());
        return encoding;
    }

    public void ResetState()
    {
        // clear existing data
        LrcMetadata.Clear();
        LrcLines.Clear();
        Offset = 0;
        SongTitlePosted = false;
    }

    public void LoadLyricsData(string[] lines, string filePath)
    {
        ResetState();

        FilePath = filePath;
        // parse lines
        foreach (var line in lines)
        {
            var matchLyric = ParseTimeLyric.Match(line);
            if (matchLyric.Success)
            {
                var minutes = matchLyric.Groups["min"].Value;
                var seconds = matchLyric.Groups["sec"].Value;
                var ff = matchLyric.Groups["ff"].Value;
                var lyricText = matchLyric.Groups["text"].Value;

                var fractionSecond = double.Parse($"0.{ff}", CultureInfo.InvariantCulture);
                var time = TimeSpan.FromMinutes(int.Parse(minutes)) + TimeSpan.FromSeconds(int.Parse(seconds) + fractionSecond);

                LrcLines.Add(new LyricEntry() { TimeStamp = time, Text = lyricText });
            }
            else
            {
                var matchMetadata = ParseMetadata.Match(line);
                if (matchMetadata.Success)
                {
                    var idTag = matchMetadata.Groups["idTag"].Value;
                    var tagContent = matchMetadata.Groups["tagContent"].Value;

                    LrcMetadata[idTag] = tagContent;
                }
            }
        }

        Offset = LrcMetadata.TryGetValue("offset", out var offsetString) && long.TryParse(offsetString, out var offset) ? offset : 0L;
    }


    /// <summary>
    /// Load lyric file into the current instance
    /// </summary>
    /// <param name="midiFilePath">path of midi file</param>
    public void LoadLyrics(string midiFilePath)
    {
        bool loadSuccessfull = true;

        var lrcPath = Path.ChangeExtension(midiFilePath, "lrc");

        try
        {
            var lrcLines = File.ReadAllLines(lrcPath, GetEncoding(lrcPath));
            LoadLyricsData(lrcLines, lrcPath);
            // TODO: fix editor window
            // Plugin.Ui.LyricsEditorWindow.LoadLrcToEditor(this);
        }
        catch
        {
            // ignored
            LrcMetadata.Clear();
            LrcLines.Clear();
            FilePath = null;
            loadSuccessfull = false;
            //DalamudApi.PluginLog.Error(ex.ToString());
        }

        if (loadSuccessfull)
        {
            DalamudApi.PluginLog.Debug($"Load LRC: {lrcPath}");
            DalamudApi.ChatGui.Print($"[MidiBard 2] Lyrics Loaded: {lrcPath}");
        }
    }

    public bool HasLyric()
    {
        return LrcLines.Count > 0;
    }

    /// <summary>
    /// process lrc line to get poster character name and lyric
    /// </summary>
    /// <param name="line">input lrc line without timestamp</param>
    /// <param name="characterName">parsed character name if exist</param>
    /// <param name="lyric">parsed lyric text ready to post</param>
    /// <returns>Input line has a character name</returns>
    static bool ProcessLine(string line, out string characterName, out string lyric)
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

    public bool LrcLoaded()
    {
        return DalamudApi.PartyList.IsInParty() && Plugin.LyricsPlayer != null && Plugin.LyricsPlayer.LrcLines.Count > 0;
    }

    public void Play()
    {
        LRCDeltaTime = 100; // Assume usual delay between sending and other clients receiving the message would be ~100ms

        if (HasLyric())
        {
            if (!DalamudApi.PartyList.IsInParty())
            {
                DalamudApi.ChatGui.Print(string.Format("[MidiBard 2] Not in a party, Lyrics will not be posted."));
            }
        }

        try
        {
            Plugin.LyricsPlayer?.Sort();
            if (Plugin.MidiPlayerControl._status != MidiPlayerControl.MidiPlayerStatus.Paused)
            {
                LrcIdx = -1;
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e.ToString());
        }
    }

    public void Stop()
    {
        LrcIdx = -1;
        SongTitlePosted = false;
    }

    internal void ChangeLRCDeltaTime(int delta)
    {
        if (!Plugin.IsPlaying)
        {
            LRCDeltaTime = 100;
            return;
        }

        LRCDeltaTime += delta;
    }

    public void EnsembleStart()
    {
        if (Plugin.LyricsPlayer == null)
            return;

        // a hack way to get ensemble delay, see MidiFilePlot.cs:90
        Plugin.LyricsPlayer.Offset += (long)(4.045 * 1000);
        // DalamudApi.PluginLog.LogVerbose("LRC Offset: " + Plugin.LyricsPlayer.Offset);
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
            var ensembleRunning = Plugin.AgentMetronome.EnsembleModeRunning;
            var playingLrc = Plugin.LyricsPlayer;

            // post song info at the beginning
            if (!SongTitlePosted && DalamudApi.PartyList.IsPartyLeader())
            {
                var msg = $"♪ {playingLrc.Title} ♪ ";
                msg += !string.IsNullOrWhiteSpace(playingLrc.Artist) ? $"Artist: {playingLrc.Artist} ♪ " : "";
                msg += !string.IsNullOrWhiteSpace(playingLrc.Album) ? $"Album: {playingLrc.Album} ♪ " : "";
                msg += !string.IsNullOrWhiteSpace(playingLrc.LrcBy) ? $"Lyric By: {playingLrc.LrcBy} ♪ " : "";

                var chatText = $"{chatComand}{msg}";
                Chat.SendMessage(chatText);
                SongTitlePosted = true;
                DalamudApi.PluginLog.Debug($"song title posted");
            }

            //TODO: when lrc multiple lines has same timestamp, all lines should be posted
            // post lyrics
            var idx = playingLrc.FindLrcIdx(Plugin.CurrentPlaybackTime);
            if (idx < 0 || idx == LrcIdx || LrcIdx >= playingLrc.LrcLines.Count) return;
            DalamudApi.PluginLog.Debug($"post lyric {idx}");

            bool shouldPostLyric = false;
            var isCharacterPostLyric = ProcessLine(playingLrc.LrcLines[idx].Text, out var characterName, out var lyric);
            DalamudApi.PluginLog.Debug($"Poster: {characterName}, Lyric: {lyric}");

            if (ensembleRunning)
            {
                if (isCharacterPostLyric)
                {
                    if (DalamudApi.PlayerState.CharacterName.ContainsIgnoreCase(characterName))
                    {
                        shouldPostLyric = true;
                    }
                }
                else
                {
                    if (DalamudApi.PartyList.IsPartyLeader())
                    {
                        shouldPostLyric = true;
                    }
                }
            }

            DalamudApi.PluginLog.Verbose($@"Post Lyrics: {shouldPostLyric}");

            if (shouldPostLyric)
            {
                string msg = $"♪ {lyric} ♪";
                DalamudApi.PluginLog.Verbose($"{lyric}");

                var chatText = $"{chatComand}{msg}";
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
        if (!LrcLines.Any()) return -1;
        var currentLrcTime = playbackTime - TimeSpan.FromMilliseconds(Offset) + TimeSpan.FromMilliseconds(LRCDeltaTime);
        if (currentLrcTime < TimeSpan.Zero) return -1;

        var maxBy = LrcLines.MaxBy(i => i.TimeStamp < currentLrcTime ? (TimeSpan?)i.TimeStamp : null);
        // For the 1st line of lyrics
        // Even Func<TSource,TKey> keySelector is NULL, MaxBy always return the 1st element of the list
        // So we need an extra check to avoid posting 1st line immediately
        return currentLrcTime < maxBy.TimeStamp ? -1 : LrcLines.IndexOf(maxBy);
    }

    public string GetLrcExportString()
    {
        var sb = new StringBuilder();
        //if (LrcLines.Any()) LrcMetadata["length"] = ToLrcTime(MidiBard.CurrentPlaybackDuration ?? LrcLines.Max(i => i.TimeStamp));
        LrcMetadata["re"] = @"www.MidiBard.org";
        LrcMetadata["ve"] = Plugin.VersionString;

        //write metadatas
        foreach (var metadata in LrcMetadata)
        {
            sb.AppendLine($"[{metadata.Key}:{metadata.Value}]");
        }

        //write time-lyric lines
        foreach (var lyricEntry in LrcLines)
        {
            sb.AppendLine($"[{ToLrcTime(lyricEntry.TimeStamp)}]{lyricEntry.Text}");
        }

        return sb.ToString();
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

