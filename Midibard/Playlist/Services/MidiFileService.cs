using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Core;

using MidiBard.Extensions.DryWetMidi;

using Encoding = System.Text.Encoding;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for MIDI file operations.
/// </summary>
public class MidiFileService : IMidiFileService
{
    private readonly ReadingSettings _readingSettings;

    public MidiFileService(Configuration? config = null, ReadingSettings? readingSettings = null)
    {
        _readingSettings = readingSettings ?? CreateDefaultReadingSettings(config);
    }

    public MidiFile? LoadMidiFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        if (!File.Exists(filePath))
        {
            DalamudApi.PluginLog.Warning($"[MidiFileService] MIDI file not found: {filePath}");
            return null;
        }

        try
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return MidiFile.Read(stream, _readingSettings);
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[MidiFileService] Error loading MIDI file: {filePath}");
            return null;
        }
    }

    public MidiFile? LoadMidiFile(Stream midiStream)
    {
        if (midiStream == null)
            return null;

        try
        {
            return MidiFile.Read(midiStream, _readingSettings);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[MidiFileService] Error loading MIDI from stream");
            return null;
        }
    }

    public TimeSpan CalculateDuration(MidiFile midiFile)
    {
        if (midiFile == null)
            return TimeSpan.Zero;

        return midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
    }

    public async Task<TimeSpan> CalculateDurationFromFileAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            var midiFile = LoadMidiFile(filePath);
            return midiFile == null ? TimeSpan.Zero : CalculateDuration(midiFile);
        });
    }

    public async Task CalculateAllDurationsAsync(List<Song> songs)
    {
        if (songs == null || songs.Count == 0)
            return;

        await Task.Run(() =>
        {
            var songsNeedingCalculation = songs
                .Where(s => s.Duration == default && !string.IsNullOrWhiteSpace(s.FilePath))
                .ToList();

            if (songsNeedingCalculation.Count == 0)
                return;

            DalamudApi.PluginLog.Debug($"[MidiFileService] Calculating durations for {songsNeedingCalculation.Count} songs");

            songsNeedingCalculation.AsParallel().ForAll(song =>
            {
                try
                {
                    var midiFile = LoadMidiFile(song.FilePath);
                    if (midiFile != null)
                    {
                        song.Duration = CalculateDuration(midiFile);
                    }
                }
                catch (Exception ex)
                {
                    DalamudApi.PluginLog.Warning(ex, $"[MidiFileService] Failed to calculate duration for {song.FilePath}");
                }
            });

            DalamudApi.PluginLog.Debug("[MidiFileService] Duration calculation complete");
        });
    }

    private static ReadingSettings CreateDefaultReadingSettings(Configuration? config = null)
    {
        return new ReadingSettings
        {
            NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
            NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
            InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.ReadValid,
            InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
            InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
            MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
            UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
            ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read,
            UnknownChunkIdPolicy = UnknownChunkIdPolicy.ReadAsUnknownChunk,
            SilentNoteOnPolicy = SilentNoteOnPolicy.NoteOff,
            InvalidSystemCommonEventParameterValuePolicy = InvalidSystemCommonEventParameterValuePolicy.SnapToLimits,
            TextEncoding = GetTextEncoding(config)
        };
    }

    private static Encoding GetTextEncoding(Configuration? config = null)
    {
        try
        {
            var uiLanguage = config?.UiLanguage ?? "";
            if (uiLanguage == "zh-Hans" || uiLanguage == "zh-Hant")
                return Encoding.GetEncoding("gb18030");
        }
        catch { }

        return Encoding.Default;
    }

    /// <summary>
    /// Validate that a file is a valid MIDI file.
    /// </summary>
    public (bool isValid, string errorMessage) ValidateMidiFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return (false, "File path is empty");

        if (!File.Exists(filePath))
            return (false, $"File not found: {filePath}");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".mid" && ext != ".midi")
            return (false, $"Not a MIDI file: {Path.GetFileName(filePath)}");

        try
        {
            var midiFile = LoadMidiFile(filePath);
            if (midiFile == null)
                return (false, "Failed to parse MIDI file");

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, $"Invalid MIDI file: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract the song name from a MIDI file path (filename without extension).
    /// </summary>
    public string ExtractSongNameFromMidi(string filePath)
        => Path.GetFileNameWithoutExtension(filePath);
}
