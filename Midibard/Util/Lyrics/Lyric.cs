using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using MidiBard.Extensions.Dalamud.Party;

namespace MidiBard.Util.Lyrics;

public class Lyrics
{
    public string FilePath { get; set; }
    public string Title => LrcMetadata.GetValueOrDefault("ti");
    public string Artist => LrcMetadata.GetValueOrDefault("ar");
    public string Album => LrcMetadata.GetValueOrDefault("al");
    public string LrcBy => LrcMetadata.GetValueOrDefault("by");
    public Dictionary<string, string> LrcMetadata { get; set; }
    public List<LyricEntry> LrcLines { get; set; }
    private static readonly Regex ParseTimeLyric = new Regex(@"^\[(?<min>\d+?):(?<sec>\d{1,2})\.(?<ff>\d+?)\](?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex ParseMetadata = new Regex(@"^\[(?<idTag>.+?):(?<tagContent>.*)\]\s*$", RegexOptions.Compiled);
    private static readonly Regex ParsePoster = new Regex(@"^(?<poster>.+?):(?<text>.+)$", RegexOptions.Compiled);
    public static string ToLrcTime(TimeSpan timeSpan) => $"{(int)timeSpan.TotalMinutes:00}:{timeSpan.Seconds:00}.{timeSpan:ff}";
    public void Sort() => LrcLines.Sort((x, y) => x.TimeStamp.CompareTo(y.TimeStamp));


    // empty lyrics
    public Lyrics()
    {
        LrcMetadata = new Dictionary<string, string>();
        LrcLines = new List<LyricEntry>();
        FilePath = null;
    }

    // loads from file
    public Lyrics(string filePath)
    {
        LrcMetadata = new Dictionary<string, string>();
        LrcLines = new List<LyricEntry>();
        LoadLyrics(filePath);
    }

    public Lyrics(IEnumerable<string> lines)
    {
        LrcMetadata = new Dictionary<string, string>();
        LrcLines = new List<LyricEntry>();
        ParseLyricsData(lines.ToArray());
    }

    public void LoadLyrics(string midiFilePath)
    {
        try
        {
            var lrcFilePath = Path.ChangeExtension(midiFilePath, "lrc");
            var lrcLines = File.ReadAllLines(lrcFilePath, GetEncoding(lrcFilePath));
            ParseLyricsData(lrcLines);
            FilePath = lrcFilePath;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex.ToString());
        }
    }

    private void ParseLyricsData(string[] lines)
    {
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
    }

    public long GetOffset()
    {
        return LrcMetadata.TryGetValue("offset", out var offsetString) && long.TryParse(offsetString, out var offset) ? offset : 0L;
    }



    public bool HasLyric()
    {
        return LrcLines.Count > 0;
    }

    public bool LrcLoaded()
    {
        return DalamudApi.PartyList.IsInParty() && LrcLines.Count > 0;
    }

    /// <summary>
    /// Encontra o índice da linha de lyrics para um tempo específico
    /// </summary>
    public int FindLrcIdx(TimeSpan? playbackTime)
    {
        if (playbackTime is null) return -1;
        if (!LrcLines.Any()) return -1;
        return LrcLines.FindIndex(l => l.TimeStamp <= playbackTime);
    }

    public string GetLrcExportString()
    {
        var sb = new StringBuilder();
        //if (LrcLines.Any()) LrcMetadata["length"] = ToLrcTime(MidiBard.CurrentPlaybackDuration ?? LrcLines.Max(i => i.TimeStamp));
        LrcMetadata["re"] = @"www.MidiBard.org";
        LrcMetadata["ve"] = DalamudApi.PluginInterface.Manifest.AssemblyVersion.ToString();

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

    private static Encoding GetEncoding(string lrcPath)
    {
        var encoding = FileHelpers.GetEncoding(lrcPath);
        DalamudApi.PluginLog.Information(encoding.ToString());
        return encoding;
    }

    public static void ExportLrcTemplate(string exportPath)
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

        var filePathInfo = new FileInfo(exportPath);
        try
        {
            File.WriteAllText(filePathInfo.FullName, fileContent);
            DalamudApi.PluginLog.Warning($"{filePathInfo.FullName} Saved");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e.ToString());
        }
    }
}

