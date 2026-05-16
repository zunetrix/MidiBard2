using System;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Multimedia;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

public sealed class DeviceInfoDebugWidget : Widget
{
    public override string Title => "Device Info";

    public DeviceInfoDebugWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        try
        {
            DrawConfigState();
            ImGui.Spacing();
            DrawEventControls();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawInputDevices();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawOutputDevices();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawCurrentDevice();
        }
        catch (Exception e)
        {
            ImGui.TextColored(Style.Colors.Red, $"Error: {e.Message}");
            DalamudApi.PluginLog.Error(e.ToString());
        }
    }

    //  Config state

    private void DrawConfigState()
    {
        var cfg = Context.Plugin.Config;
        ImGui.TextUnformatted("Config");
        ImGui.Separator();

        ImGui.TextUnformatted("UseMidiInputDevice:");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, cfg.UseMidiInputDevice
            ? Style.Colors.GrassGreen
            : Style.Colors.Yellow))
        {
            ImGui.TextUnformatted(cfg.UseMidiInputDevice.ToString());
        }

        ImGui.TextUnformatted($"Last Used Device:  {(string.IsNullOrEmpty(cfg.LastUsedMidiDeviceName) ? "(none)" : cfg.LastUsedMidiDeviceName)}");
    }

    //  Event listening controls

    private void DrawEventControls()
    {
        ImGui.TextUnformatted("Event Listening");
        ImGui.Separator();

        bool hasDevice = InputDeviceManager.CurrentInputDevice is not null;

        using (ImRaii.Disabled(!hasDevice))
        {
            if (ImGui.SmallButton("Start Event Listening"))
                InputDeviceManager.CurrentInputDevice?.StartEventsListening();

            ImGui.SameLine();
            if (ImGui.SmallButton("Stop Event Listening"))
                InputDeviceManager.CurrentInputDevice?.StopEventsListening();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Manual Scan"))
            Context.Plugin.InputDeviceManager.TriggerManualScan();
    }

    //  Input device list

    private void DrawInputDevices()
    {
        var cached = InputDeviceManager.Devices;
        var live = GetDevicesSafe(() => InputDevice.GetAll().OrderBy(i => i.Name).ToArray());

        ImGui.TextUnformatted($"Input Devices (cached: {cached.Length}  live: {live.Length})");
        ImGui.Separator();

        if (live.Length == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Gray))
                ImGui.TextUnformatted("  (none)");
            return;
        }

        foreach (var d in live)
        {
            bool isCurrent = d.DeviceName() == InputDeviceManager.CurrentInputDevice?.DeviceName();
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.GrassGreen, isCurrent))
            {
                ImGui.TextUnformatted($"  [{d.Name}]  ({d.DeviceName()})  {(isCurrent ? "← active" : "")}");
            }
        }
    }

    //  Output device list

    private void DrawOutputDevices()
    {
        OutputDevice[] live;
        try { live = OutputDevice.GetAll().OrderBy(i => i.Name).ToArray(); }
        catch { live = []; }

        ImGui.TextUnformatted($"Output Devices ({live.Length})");
        ImGui.Separator();

        if (live.Length == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Gray))
                ImGui.TextUnformatted("  (none)");
            return;
        }

        foreach (var d in live)
            ImGui.TextUnformatted($"  [{d.Name}]");
    }

    //  Current device detail

    private void DrawCurrentDevice()
    {
        ImGui.TextUnformatted("Current Input Device");
        ImGui.Separator();

        var dev = InputDeviceManager.CurrentInputDevice;
        if (dev is null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Gray))
                ImGui.TextUnformatted("  (none)");
            return;
        }

        ImGui.TextUnformatted($"  Name:      {dev.Name}");
        ImGui.TextUnformatted($"  DeviceName:{dev.DeviceName()}");

        bool listening = Context.Plugin.InputDeviceManager.IsListeningForEvents;
        ImGui.TextUnformatted("  Listening: ");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, listening ? Style.Colors.GrassGreen : Style.Colors.Yellow))
            ImGui.TextUnformatted(listening.ToString());
    }

    //  Helpers

    private static T[] GetDevicesSafe<T>(Func<T[]> getter)
    {
        try { return getter(); }
        catch { return []; }
    }
}
