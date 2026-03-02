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
}
