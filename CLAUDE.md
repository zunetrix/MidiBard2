# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MidiBard is a Dalamud plugin for Final Fantasy XIV that allows players to play midi files ingame. The plugin allows create playlists with song metadata, import and play midi files.

## Build and Development Commands

### Building the Project
```bash
dotnet build MidiBard2.sln
```

### Running Tests
```bash
dotnet test MidiBard.Tests/MidiBard.Tests.csproj
```

### Debug Build
```bash
dotnet build MidiBard2.sln -c Debug
```

## Architecture Overview

### Core Components

- **Plugin.cs**: Main plugin entry point implementing IDalamudPlugin
- **PluginUi.cs**: Ui Window Manager System
- **/DalamudApi/DalamudApi.cs**: Dalamud services


### Key Systems

1. **/Ipc** (`MidiBard/Ipc/`):
    - Logic Inter-Process Communication, broadcast and message handlers

2 . **/Playlist** (`MidiBard/Playlist/`):
    - Logic to manage playlist and associated songs CRUD

3 . **/Lyrics** (`MidiBard/Lyrics/`):
    - Logic to load, edit and send lyrics text to game chat

4 . **/Control** (`MidiBard/Control/`):
    - Logic to control ingame actions
    - Midi and Bard Playbck to run a midi device with custom midi event handler (press keys instead of play midi events)

5 . **BardMusicPlayer API** (`BardMusicPlayer.XIVMIDI/`):
    - Solution to integrate with xivmidi.com api

6. **UI Windows** (`Midibard/Ui/`)
   - ImGui-based interface

7. **Rsources** (`Midibard/Resources/`)
   - Internationalization, i18n

### Dependencies

- **Dalamud.NET.Sdk**: FFXIV plugin framework
- **TinyIpc**: Ipc control, broacast and message handling
- **protobuf-net**: Ipc message serialization
- **Melanchall.DryWetMidi**: Midi processing
- **LiteDB**: Playlist and songs data persistence

## Code Style and Conventions

- 4-space indentation
- PascalCase for public members, camelCase for private fields
- Comprehensive EditorConfig
- C# nullable reference types enabled
- Generator.Equals for value type equality

## Testing

The project uses xUnit for testing with Shouldly assertions. Tests are located in `MidiBard.Tests/` and can be run with standard dotnet test commands.


## Development Notes

- The plugin integrates deeply with FFXIV's game state through Dalamud APIs
- The UI uses ImGui with custom extensions for enhanced functionality

## Midi File Flow
MidiFile (disk)
     │
     ▼
MidiFileService.LoadMidiFile()          ← ReadingSettings
     │
     ▼
FilePlayback.GetPlaybackInstance()
     │
     ▼
BardPlayback.CreatePlayback()
     │
     ├─► PreparePlaybackData()
     │        │
     │        ├─ RemoveStackedNotes()        ← AntiStack  chunk
     │        ├─ RealignMidiFile()           ← align config
     │        ├─ ProcessTracks()
     │        │       ├─ FixNoteOffChannels()  ← compat DryWetMidi 8.x
     │        │       └─ ProcessNotes/CutNote  ← cut notes >2000ms
     │        ├─ GetTrackInfos()             ← metadata (NoteCount, etc.)
     │        └─ GetTimedEventWithMetadata() ← merge all chunks
     │                  ordered events by Time+NoteNumber
     │                  each event with TrackIndex
     │
     └─► new InternalPlayback(timedEvents, tempoMap)
              TrackNotes = true
              InterruptNotesOnStop = true
              TryPlayEvent → SendMidiEvent callback

─────────────────── PLAYBACK LOOP ───────────────────

DryWetMidi HighPrecisionTickGenerator
     │  (tick ~1ms)
     ▼
Playback scheduler dequeue event time
     │
     ▼
InternalPlayback.TryPlayEvent(midiEvent, metadata)
     │
     ▼
BardPlayback.SendMidiEvent()
     │
     ▼
BardPlayDevice.SendEventWithMetadata()
     │
     ├─ IsDisposed / !InPerformanceMode  → return false
     │
     ├─ TrackInfos[TrackIndex].IsPlaying()
     │       false → return false          ← track disabled
     │       true  → continue
     │
     ├─ EnsembleRunning?
     │       yes → QueuePlaybackMidiEvent() → MidiClock (compensation delay)
     │       no  → PlayMidiEvent() play
     │
     ▼
PlayMidiEvent()
     │
     ├─ ProgramChangeEvent → Channels[ch].Program = ...
     │
     └─ NoteEvent
           ├─ GetNoteNumberTranslatedByTrack()  ← transpose + range clamp
           ├─ noteNum < 0 ou > 36 → return false
           ├─ GuitarTone → ApplyTone if needed
           └─ Playlib.PressKey(noteNum) / ReleaseKey(noteNum)
                    │
                    ▼
              AgentPerformance (game memory write)
                    │
                    ▼
              FFXIV bard → press key/release


## Ensemble Flow:
REAL TIME ──────────────────────────────────────────────────────────────────────────►

T=0          T≈3s                  T≈4s (game t=0)            T≈3+D         T≈4+D
 │            │                      │                           │             │
 ▼            ▼                      ▼                           ▼             ▼
[Ready    [Network pkt          [Metronome hits 0           [Playback_    [Last note
 check     received]             notes start sounding]      Finished      actually
 begins]   │                                                fires  ◄───── heard in
           │                                                │        gap] game]
           │ EnsembleIndicatorDelay = -(elapsed + 1.15) ≈ -4s
           │◄──────────────────────────────────────────────►│
           │         abs(EnsembleIndicatorDelay) ≈ 4s       │
           │                                                │
           └── MIDI playback starts here                    └── all events
                                                                dispatched
                                                                to buffer


### PER-EVENT DISPATCH FLOW (inside the 4s window above)

  BardPlayback (DryWetMidi scheduler)
       │
       │  fires TryPlayEvent for each scheduled MIDI event
       ▼
  BardPlayDevice.SendEventWithMetadata()
       │
       │  EnsembleRunning? ──► YES
       ▼
  QueuePlaybackMidiEvent()
       │
       │  delayMs = GetCompensationNew(instrument, note)   ← 0..~500ms
       │  slotIndex = (CurrentBufferIndex + delayMs) % 500
       ▼
  MidiEventsBuffer[slotIndex].Add(event)       ← circular buffer, 1 slot = 1ms


  MidiClock ticks every 1ms
       │
       ▼
  PlaybackTickerTicked()
       │
       └─► dequeue current slot → PlayMidiEvent() → Playlib.PressKey / ReleaseKey


### PLAYBACK_FINISHED TIMING

  DryWetMidi fires Playback.Finished when the last event leaves the scheduler.
  At that moment the event is IN the buffer, not yet heard.

  Audio still remaining = abs(EnsembleIndicatorDelay)  +  up to ~500ms (buffer drain)
                          ≈ 4s                              negligible

  So StopEnsemble() must wait at least abs(EnsembleIndicatorDelay) after Finished:

  total wait = EnsembleStopDelay  +  abs(EnsembleIndicatorDelay)
             =  user extra margin  +  audio tail

## Post song to chat flow
EnsembleManager.MonitorEnsembleState()
  └─► Plugin.FilePlayback.TryEnsembleAutoAdvance()   (EnableEnsemblePlayMode && IsPartyLeader)
        └─► Plugin.PlaylistManager.LoadPlayback(nextIndex)   (load next song)
              └─► BeginEnsembleReadyCheck()           (call ready check)
                    └─► HandleNetworkEnsemble()        (callback start ensemble)
                          └─► DoPlay(isEnsemble: true)

## Multi party sync
Party device                    Non-party device
──────────────────────────────  ──────────────────────────────
T+0s:  heartbeat → StartEnsemble()    heartbeat → Task.Delay(4s)
       MIDI start                     (waiting...)

T+4s:  metronome=0, game              Task.Delay  →
       accpet keys (lock) → play      StartEnsemble() →
                                      MIDI starts play notes (no lock)
       ← sync →
