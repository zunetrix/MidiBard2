using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Core;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for MIDI file operations.
/// </summary>
public interface IMidiFileService
{
    /// <summary>
    /// Load a MIDI file from file path.
    /// </summary>
    MidiFile? LoadMidiFile(string filePath);

    /// <summary>
    /// Load a MIDI file from a stream.
    /// </summary>
    MidiFile? LoadMidiFile(Stream midiStream);

    /// <summary>
    /// Calculate duration of a MIDI file.
    /// </summary>
    TimeSpan CalculateDuration(MidiFile midiFile);

    /// <summary>
    /// Calculate duration of a MIDI file from file path asynchronously.
    /// </summary>
    Task<TimeSpan> CalculateDurationFromFileAsync(string filePath);

    /// <summary>
    /// Calculate durations for a list of songs asynchronously (parallel).
    /// </summary>
    Task CalculateAllDurationsAsync(List<Song> songs);

    /// <summary>
    /// Validate that a file is a valid MIDI file.
    /// </summary>
    /// <param name="filePath">Path to the file to validate</param>
    /// <returns>Tuple of (isValid, error message)</returns>
    (bool isValid, string errorMessage) ValidateMidiFile(string filePath);

    /// <summary>
    /// Extract the song name from a MIDI file.
    /// First tries to get the Sequence Track Name from the MIDI metadata.
    /// Falls back to filename (without extension) if no track name is found.
    /// </summary>
    /// <param name="filePath">Path to the MIDI file</param>
    /// <returns>Song name, or empty string on error</returns>
    string ExtractSongNameFromMidi(string filePath);
}
