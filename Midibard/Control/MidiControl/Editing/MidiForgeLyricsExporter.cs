using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Util.Lyrics;

namespace MidiBard.Control.MidiControl.Editing;

public sealed record MidiForgeLrcLine(TimeSpan Time, string Text);

public sealed record MidiForgeLrcExportResult(IReadOnlyList<MidiForgeLrcLine> Lines, string Content)
{
    public bool HasLyrics => Lines.Count > 0;
}

public static class MidiForgeLyricsExporter
{
    private static readonly Regex EscapedLineBreakPattern = new(@"\\[rn]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LineSeparatorPattern = new(@"[\r\n/\\]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceMarkerPattern = new(@"(?:_|\s{2,})", RegexOptions.Compiled);
    private static readonly Regex BracketOnlyPattern = new(@"^\s*\[[^\]]*\]\s*$", RegexOptions.Compiled);

    public static MidiForgeLrcExportResult Export(MidiFile midiFile, string? songTitle)
    {
        if (midiFile == null) throw new ArgumentNullException(nameof(midiFile));

        var lines = ExtractLyrics(midiFile);
        var sb = new StringBuilder();
        sb.AppendLine("[ar:Artist Name]");
        sb.AppendLine($"[ti:{SanitizeMetadataValue(songTitle, "Song Title")}]");
        sb.AppendLine("[offset:0]");

        if (lines.Count == 0)
        {
            sb.AppendLine("[00:00.00]");
        }
        else
        {
            foreach (var line in lines)
                sb.AppendLine($"[{Lyrics.ToLrcTime(line.Time)}]{line.Text}");
        }

        return new MidiForgeLrcExportResult(lines, sb.ToString());
    }

    public static IReadOnlyList<MidiForgeLrcLine> ExtractLyrics(MidiFile midiFile)
    {
        if (midiFile == null) throw new ArgumentNullException(nameof(midiFile));

        var tempoMap = midiFile.GetTempoMap();
        var lyricEvents = midiFile.GetTrackChunks()
            .SelectMany(chunk => chunk.GetTimedEvents())
            .Where(timedEvent => timedEvent.Time > 0)
            .Select(timedEvent => new LyricToken(timedEvent.Time, GetText(timedEvent.Event)))
            .Where(token => token.RawText != null)
            .OrderBy(token => token.Tick)
            .ToList();

        var phraseGroups = new List<List<LyricToken>> { new() };
        var groupIndex = 0;

        foreach (var lyricEvent in lyricEvents)
        {
            if (HasLineSeparator(lyricEvent.RawText!))
            {
                groupIndex++;
                while (phraseGroups.Count <= groupIndex)
                    phraseGroups.Add(new List<LyricToken>());
            }

            phraseGroups[groupIndex].Add(lyricEvent with { RawText = SanitizeLyricText(lyricEvent.RawText!) });
        }

        var lyrics = new List<MidiForgeLrcLine>();
        foreach (var phraseGroup in phraseGroups)
        {
            var validPhrases = phraseGroup
                .Where(token => IsUsableLyricText(token.RawText))
                .ToList();
            if (validPhrases.Count == 0) continue;

            var time = TimeConverter.ConvertTo<MetricTimeSpan>(validPhrases[0].Tick, tempoMap);
            var text = string.Concat(validPhrases.Select(token => token.RawText)).Trim();
            if (text.Length == 0) continue;

            lyrics.Add(new MidiForgeLrcLine(new TimeSpan(time.TotalMicroseconds * 10), text));
        }

        return lyrics
            .OrderBy(line => line.Time)
            .ToList();
    }

    private static string? GetText(MidiEvent midiEvent)
        => midiEvent switch
        {
            LyricEvent lyricEvent => lyricEvent.Text,
            TextEvent textEvent => textEvent.Text,
            _ => null,
        };

    private static bool HasLineSeparator(string text)
        => text.Contains('\r')
           || text.Contains('\n')
           || text.Contains('/')
           || text.Contains('\\');

    private static string SanitizeLyricText(string text)
    {
        var withoutEscapedLineBreaks = EscapedLineBreakPattern.Replace(text, string.Empty);
        var withoutSeparators = LineSeparatorPattern.Replace(withoutEscapedLineBreaks, string.Empty);
        var normalizedWhitespace = WhitespaceMarkerPattern.Replace(withoutSeparators, " ");
        return normalizedWhitespace.Replace("\0", string.Empty).Trim();
    }

    private static bool IsUsableLyricText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return !BracketOnlyPattern.IsMatch(text);
    }

    private static string SanitizeMetadataValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var cleaned = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(']', ')')
            .Trim();

        return cleaned.Length == 0 ? fallback : cleaned;
    }

    private sealed record LyricToken(long Tick, string? RawText);
}
