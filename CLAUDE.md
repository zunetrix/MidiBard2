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
