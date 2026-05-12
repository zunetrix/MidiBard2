using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AlphaTab;
using AlphaTab.Importer;

using Melanchall.DryWetMidi.Core;

using AlphaSynthMidiFileHandler = AlphaTab.Midi.AlphaSynthMidiFileHandler;
using DryMidiEvent = Melanchall.DryWetMidi.Core.MidiEvent;
using MidiFileFormat = AlphaTab.Midi.MidiFileFormat;
using MidiFileGenerator = AlphaTab.Midi.MidiFileGenerator;
using MidiFile = Melanchall.DryWetMidi.Core.MidiFile;

namespace MidiBard.Control.MidiControl.Editing;

public static class MidiForgeGuitarTabImporter
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gp",
        ".gp3",
        ".gp4",
        ".gp5",
        ".gpx",
    };

    public static bool IsSupportedExtension(string fileName)
        => SupportedExtensions.Contains(Path.GetExtension(fileName));

    public static string GetConvertedMidiFileName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(baseName) ? "guitar-tab.mid" : $"{baseName}.mid";
    }

    public static byte[] ConvertToMidiBytes(byte[] tabFileData)
    {
        ArgumentNullException.ThrowIfNull(tabFileData);
        if (tabFileData.Length == 0)
            throw new InvalidDataException("Guitar tab file is empty.");

        var settings = new Settings();
        var score = ScoreLoader.LoadScoreFromBytes(tabFileData, settings);
        var midiFile = new AlphaTab.Midi.MidiFile
        {
            Format = MidiFileFormat.MultiTrack,
        };

        var handler = new AlphaSynthMidiFileHandler(midiFile, true);
        var generator = new MidiFileGenerator(score, settings, handler);
        generator.Generate();

        var binary = midiFile.ToBinary();
        var length = Convert.ToInt32(binary.Length);
        var result = new byte[length];
        for (var i = 0; i < length; i++)
            result[i] = Convert.ToByte(binary[i]);

        return result;
    }

    public static int RemoveAlphaTabHelperEvents(MidiFile midiFile)
    {
        if (midiFile == null)
            return 0;

        var removed = 0;
        foreach (var track in midiFile.GetTrackChunks())
        {
            var count = track.Events.Count(IsAlphaTabPrivateSysEx);
            if (count == 0)
                continue;

            track.Events.RemoveAll(IsAlphaTabPrivateSysEx);
            removed += count;
        }

        return removed;
    }

    private static bool IsAlphaTabPrivateSysEx(DryMidiEvent midiEvent)
        => midiEvent switch
        {
            NormalSysExEvent normal => HasAlphaTabManufacturerId(normal.Data),
            EscapeSysExEvent escape => HasAlphaTabManufacturerId(escape.Data),
            _ => false,
        };

    private static bool HasAlphaTabManufacturerId(byte[] data)
        => data is { Length: > 0 } && data[0] == 0x7d;
}
