using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Melanchall.DryWetMidi.Interaction;

namespace MidiBard;

public record TrackInfo
{
    public string[] TrackNameEventsText { get; init; }
    public string[] ProgramChangeEventsText { get; init; }
    public int NoteCount { get; init; }
    public Note LowestNote { get; init; }
    public Note HighestNote { get; init; }
    public MetricTimeSpan DurationMetric { get; init; }
    public long DurationMidi { get; init; }
    public bool IsProgramControlled { get; init; }
    public string TrackName { get; init; }
    public int Index { get; set; }
    public bool IsProgramElectricGuitar { get; set; }

    public int TransposeFromTrackName => GetTransposeByName(TrackName);
    /*
     harp 竖琴  piano 钢琴  lute 鲁特  fiddle提琴拨弦 flute长笛 oboe 双簧管 clarinet 单簧管 fife 横笛 panpipes 排箫
    TIMPANI定音鼓 BONGO邦戈鼓 bassdrum低音鼓 snaredrum小军鼓 CYMBAL镲 Trumpet小号 Trombone长号 Tuba大号 Horn圆号 Saxophone萨克斯 Violin小提琴 Viola中提琴 Cello大提琴
    DoubleBass 低音提琴 ElectricGuitaroverdriven过载 ElectricGuitarclean清音 ElectricGuitarMuted闷音 ElectricGuitarPowerchords重力 ElectricGuitarspecial特殊奏法
    */
    private static readonly Dictionary<string, uint?> instrumentIdMap = new()
    {
        { "harp", 1 },
        { "竖琴", 1 },

        { "piano", 2 },
        { "钢琴", 2 },

        { "lute", 3 },
        { "鲁特", 3 },

        { "fiddle", 4 },
        { "提琴拨弦", 4 },

        { "flute", 5 },
        { "长笛", 5 },

        { "oboe", 6 },
        { "双簧管", 6 },

        { "clarinet", 7 },
        { "单簧管", 7 },

        { "fife", 8 },
        { "横笛", 8 },

        { "panpipes", 9 },
        { "排箫", 9 },

        { "timpani", 10 },
        { "定音鼓", 10 },

        { "bongo", 11 },
        { "邦戈鼓", 11 },

        { "bassdrum", 12 },
        { "低音鼓", 12 },

        { "snaredrum", 13 },
        { "小军鼓", 13 },
        { "军鼓", 13 },

        { "cymbal", 14 },
        { "镲", 14 },

        { "trumpet", 15 },
        { "小号", 15 },

        { "trombone", 16 },
        { "长号", 16 },

        { "tuba", 17 },
        { "大号", 17 },

        { "horn", 18 },
        { "圆号", 18 },

        { "saxophone", 19 },
        { "萨克斯", 19 },
        // alias
        { "sax", 19 },

        { "violin", 20 },
        { "小提琴", 20 },

        { "viola", 21 },
        { "中提琴", 21 },

        { "cello", 22 },
        { "大提琴", 22 },

        { "doublebass", 23 },
        { "低音提琴", 23 },
        // alias
        { "contrabass", 23 },

        { "electricguitaroverdriven", 24 },
        { "过载", 24 },

        { "electricguitarclean", 25 },
        { "清音", 25 },

        { "electricguitarmuted", 26 },
        { "闷音", 26 },

        { "electricguitarpowerchords", 27 },
        { "重力", 27 },

        { "electricguitarspecial", 28 },
        { "特殊奏法", 28 },

        //alias
        { "snare", 13 },
        { "programelectricguitar", 24 },
        { "program", 24 },
        { "electricguitar", 24 }
    };

    public override string ToString()
    {
        return $"{TrackName} / {NoteCount} notes / {LowestNote}-{HighestNote}";
    }

    public string ToLongString()
    {
        var trackInfo = $"""
        Track name:
            {TrackName}
        Note count:
            {NoteCount}
        Note Range:
            {LowestNote}-{HighestNote}
        ProgramChange events:
            {string.Join("\n ", ProgramChangeEventsText.Distinct())}
        Duration:
            {DurationMetric}
        """;
        return trackInfo;
    }

    public static uint? GetInstrumentIdByName(string trackName, ushort? defaultInstrumentId = null)
    {
        RegexOptions regexOptions = RegexOptions.IgnoreCase | RegexOptions.Multiline;
        string sanitizedTrackName = Regex.Replace(trackName, @"(\s+|:)", "", regexOptions).ToLowerInvariant();

        string[] instrumentsKeys = instrumentIdMap.Keys.ToArray();
        string instrumentsPattern = string.Join("|", instrumentsKeys);
        string trackNamePattern = $@"({instrumentsPattern})";
        Regex expression = new Regex(trackNamePattern, regexOptions);
        Match match = expression.Match(sanitizedTrackName);

        uint? instrumentId = null;

        string instrumentName = match.Success ? match.Value.ToString() : "";
        instrumentIdMap.TryGetValue(instrumentName, out instrumentId);

        // If not found, use default instrument if provided
        if (instrumentId == null && defaultInstrumentId != null && defaultInstrumentId > 0)
        {
            instrumentId = defaultInstrumentId;
        }

        return instrumentId;
    }

    public static int GetTransposeByName(string trackName)
    {
        RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Multiline;
        string sanitizedTrackName = Regex.Replace(trackName, @"(\s+|:)", "", options).ToLowerInvariant();
        string octavePattern = $@"(?:(\+|-)(?:\s+)?(\d))";
        Regex expression = new Regex(octavePattern, options);
        var matches = expression.Matches(sanitizedTrackName);

        int octave = 0;

        foreach (Match match in matches)
        {
            GroupCollection groups = match.Groups;
            string plusMinusSign = groups[1].Value.ToString();
            bool isParsable = int.TryParse(groups[2].Value, out octave);
            octave = (plusMinusSign == "-" ? -octave : octave) * 12;
        }
        // DalamudApi.PluginLog.Debug("Transpose octave: " + octave);

        return octave;
    }

    /// <summary>
    /// Maps a MIDI note number to a game note (C3=0, C6=36).
    /// Optionally wraps out-of-range notes back into the 0–36 playable window
    /// using the same algorithm as BardPlayDevice.
    /// </summary>
    /// <param name="noteNumber">Raw MIDI note number.</param>
    /// <param name="transposeGlobal">Semitone offset applied after mapping to game range.</param>
    /// <param name="adaptOOR">Whether to wrap OOR notes into the playable range.</param>
    /// <returns>Game note in range 0–36 (may exceed if adaptOOR is false).</returns>
    public static int TranslateNoteNumber(int noteNumber, int transposeGlobal = 0, bool adaptOOR = false)
    {
        noteNumber = noteNumber - 48 + transposeGlobal;

        if (adaptOOR)
        {
            if (noteNumber < 0)
                noteNumber = (noteNumber + 1) % 12 + 11;
            else if (noteNumber > 36)
                noteNumber = (noteNumber - 1) % 12 + 25;
        }

        return noteNumber;
    }
}
