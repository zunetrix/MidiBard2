# Agent Instructions

Use this file for repo-wide agent guidance, including MIDI editor command
architecture and command implementation instructions.

## Build And Test

- Build: `dotnet build MidiBard2.sln --no-restore`
- Test: `dotnet test MidiBard.Tests/MidiBard.Tests.csproj --no-restore`
- The test project may emit the existing `System.Drawing.Common` version
  conflict warning.
- For documentation-only edits, run at least `git diff --check`.

## MIDI Editor Architecture

- Commands mutate MIDI/editor state. A command owns validation, mutation policy,
  selected-track or selected-event loops, result shapes, refresh hints, and
  tests for its operation.
- Queries read or analyze state. They must not mutate `EditableMidiFile`, dirty
  state, version state, history, or UI state.
- Preview commands and queries use preview contexts. Keep live audio and
  scheduler behavior behind preview adapter interfaces.
- Orchestrator commands must invoke child commands through
  `IEditorCommandInvoker`. Do not duplicate child command algorithms.
- Shared helpers should be thin primitives for reusable single-object behavior
  such as parsing, constants, note math, or single-track edits. Do not move
  operation behavior into fat services.

## State And UI

- Put operation popup state in `MidiEditorSessionState.PopupStates` using typed
  state payloads.
- Do not add operation-specific fields to the `MidiEditorWindow` root.
- Keep cohesive ImGui flows in feature-oriented window partials when that is
  easier to read than a presenter.
- Use presenters or generated menus only when they improve readability or enable
  standalone/generated UI.
- Keep grouped hand-written menus for workflows with custom selection counts,
  disabled-state checks, quick actions, hover help, or popup setup.

## Help Text

- Put MIDI editor operation descriptions and tooltips in feature-scoped
  `MidiEditorOperationHelp.*.cs` partials.
- Keep help text concise, user-facing, and close to the related UI feature
  module.
- Do not hard-code operation tooltip strings inline in popup or menu code.

## Command Metadata

- Operation ids use lowercase dotted namespaces with kebab-case segments, such
  as `track.clear-names` or `auto-edit.prepare-for-playback`.
- Generated menu paths use slash-separated title-case segments, such as
  `Track/Names` or `Forge/Auto Edit`.
- Registry metadata is for discovery, conventions, tests, command orchestration,
  and optional generated UI.

## Compatibility And Services

- Do not add `MidiForgeOperations`-style static facades. Use command/query
  executors or existing narrow primitives directly.
- Services must stay thin and must not own user-facing `MidiForge*Result`
  shapes.
- Commands should return `Changed = false` when an operation succeeds but does
  not mutate the file.
- Commands should return refresh hints instead of directly reloading UI lists.

## Testing Expectations

- Commands need focused tests for mutation, no-op behavior, validation, undo or
  history behavior, refresh hints, and compatibility wrappers when wrappers
  remain.
- Queries need focused tests for result shape, edge cases, and read-only
  behavior. File-backed query tests should assert file version and dirty state
  do not change.
- Orchestrators need tests that prove composition through the executor/invoker
  path instead of copied algorithms.
- Preview work should be testable without live audio, ImGui, or Dalamud runtime
  dependencies.
- UI state migrations should compile through the full test project; add direct
  tests only where behavior is meaningfully testable outside ImGui.
