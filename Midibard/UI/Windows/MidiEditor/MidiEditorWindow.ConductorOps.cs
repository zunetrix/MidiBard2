using System;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands.Conductor;
using MidiBard.Util;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private const string SetTempoPopupStateKey = "conductor.set-tempo.popup";
    private const string SetTimeSignaturePopupStateKey = "conductor.set-time-signature.popup";

    private SetTempoPopupState GetSetTempoPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(SetTempoPopupStateKey, static () => new SetTempoPopupState());

    private SetTimeSignaturePopupState GetSetTimeSignaturePopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(SetTimeSignaturePopupStateKey, static () => new SetTimeSignaturePopupState());

    private void OpenSetTempoPopup(long tick)
    {
        var state = GetSetTempoPopupState();
        state.Tick = Math.Max(0, tick);
        state.Bpm = GetExistingBpmAtTick(state.Tick) ?? 120;
        state.ExistingBpm = GetExistingBpmAtTick(state.Tick);
        _pendingPopup = "##SetTempoPopup";
    }

    private void OpenSetTimeSignaturePopup(long tick)
    {
        var state = GetSetTimeSignaturePopupState();
        state.Tick = Math.Max(0, tick);
        var existing = GetExistingTimeSignatureAtTick(state.Tick);
        state.Numerator = existing?.Numerator ?? 4;
        state.Denominator = existing?.Denominator ?? 4;
        _pendingPopup = "##SetTimeSignaturePopup";
    }

    /// <summary>
    /// Converts the current playback cursor position (seconds) to a tick for use
    /// as a default position when opening conductor popups from the menu bar.
    /// </summary>
    private long GetPlaybackCursorTick()
    {
        if (_file == null || _previewState.CameraTime <= 0) return 0;
        return TimeConverter.ConvertFrom(
            new MetricTimeSpan((long)(_previewState.CameraTime * 1_000_000.0)),
            _file.TempoMap);
    }

    /// <summary>Formats a tick as "bar.beat.tick" for display.</summary>
    private string FormatBarBeatTick(long tick)
    {
        if (_file == null) return string.Empty;
        var pos = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(Math.Max(0, tick), _file.TempoMap);
        return $"{pos.Bars + 1}.{pos.Beats + 1}.{pos.Ticks}";
    }

    private int? GetExistingBpmAtTick(long tick)
    {
        if (_file == null) return null;
        var conductor = _file.Tracks.FirstOrDefault(track => track.IsConductorTrack);
        if (conductor is null) return null;
        conductor.FlushChanges();
        var tempoEvent = conductor.Chunk.GetTimedEvents()
            .FirstOrDefault(te => te.Event is SetTempoEvent && te.Time == tick);
        if (tempoEvent is null) return null;
        return (int)(60_000_000.0 / ((SetTempoEvent)tempoEvent.Event).MicrosecondsPerQuarterNote);
    }

    private (int Numerator, int Denominator)? GetExistingTimeSignatureAtTick(long tick)
    {
        if (_file == null) return null;
        var conductor = _file.Tracks.FirstOrDefault(track => track.IsConductorTrack);
        if (conductor is null) return null;
        conductor.FlushChanges();
        var tsEvent = conductor.Chunk.GetTimedEvents()
            .FirstOrDefault(te => te.Event is TimeSignatureEvent && te.Time == tick);
        if (tsEvent is null) return null;
        var ts = (TimeSignatureEvent)tsEvent.Event;
        return (ts.Numerator, ts.Denominator);
    }

    private void DrawSetTempoPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SetTempoPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetSetTempoPopupState();

        ImGui.Text("Set Tempo");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.ConductorSetTempo);

        int tickInt = (int)state.Tick;
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("Tick##setTempoTick", ref tickInt))
            state.Tick = Math.Max(0, tickInt);
        ImGui.TextDisabled($"Position: {FormatBarBeatTick(state.Tick)}");

        if (state.ExistingBpm.HasValue)
            ImGui.TextDisabled($"Current: {state.ExistingBpm.Value} BPM");

        ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("BPM##setTempoBpm", ref state.Bpm);
        state.Bpm = int.Clamp(state.Bpm, 1, 300);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##setTempoApply"))
        {
            var result = _editorCommandExecutor.Execute(
                new SetTempoAtTickCommand(),
                CreateEditorCommandContext(),
                new SetTempoOptions(state.Tick, state.Bpm));
            if (result.Succeeded)
            {
                ApplyEditorCommandRefreshHints();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton("Cancel##setTempoCancel"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawSetTimeSignaturePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SetTimeSignaturePopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetSetTimeSignaturePopupState();

        ImGui.Text("Set Time Signature");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.ConductorSetTimeSignature);

        int tickInt = (int)state.Tick;
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("Tick##setTimeSigTick", ref tickInt))
            state.Tick = Math.Max(0, tickInt);
        ImGui.TextDisabled($"Position: {FormatBarBeatTick(state.Tick)}");

        ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Numerator##setTimeSigNum", ref state.Numerator);
        state.Numerator = int.Clamp(state.Numerator, 1, 32);

        var denomLabels = new[] { "1", "2", "4", "8", "16", "32", "64", "128", "256" };
        var denomValues = new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 };
        int denomIndex = Array.IndexOf(denomValues, state.Denominator);
        if (denomIndex < 0) denomIndex = 2;
        ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo("Denominator##setTimeSigDen", ref denomIndex, denomLabels, denomLabels.Length))
            state.Denominator = denomValues[denomIndex];

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##setTimeSigApply"))
        {
            var result = _editorCommandExecutor.Execute(
                new SetTimeSignatureAtTickCommand(),
                CreateEditorCommandContext(),
                new SetTimeSignatureOptions(state.Tick, state.Numerator, state.Denominator));
            if (result.Succeeded)
            {
                ApplyEditorCommandRefreshHints();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton("Cancel##setTimeSigCancel"))
            ImGui.CloseCurrentPopup();
    }

    private sealed class SetTempoPopupState
    {
        public long Tick;
        public int Bpm = 120;
        public int? ExistingBpm;
    }

    private sealed class SetTimeSignaturePopupState
    {
        public long Tick;
        public int Numerator = 4;
        public int Denominator = 4;
    }
}
