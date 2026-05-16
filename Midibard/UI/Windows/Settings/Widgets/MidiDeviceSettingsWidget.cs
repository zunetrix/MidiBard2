using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

public sealed class MidiDeviceSettingsWidget : Widget
{
    public override string Title => "MIDI Device";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Keyboard;

    private int _selectedDeviceIndex = -1;

    public MidiDeviceSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var cfg = Context.Plugin.Config;
        var mgr = Context.Plugin.InputDeviceManager;

        //  Enable / Disable toggle
        ImGui.TextUnformatted("MIDI Input Device");
        ImGui.Separator();

        bool useMidi = cfg.UseMidiInputDevice;
        if (ImGui.Checkbox("Enable MIDI Input Device", ref useMidi))
        {
            cfg.UseMidiInputDevice = useMidi;
            // The OnConfigurationChanged event will trigger the InputDeviceManager to start/stop the thread
            Context.Plugin.SaveConfig();
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.HelpMarker(
            "When disabled the MIDI input scanning thread idles and no device is kept open. " +
            "Enable it when you want to play using a physical MIDI controller.");

        ImGui.Spacing();

        //  Manual scan
        using (ImRaii.Disabled(!useMidi))
        {
            if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Sync, "##MidiScanBtn", "Scan for connected MIDI devices"))
            {
                mgr.TriggerManualScan();
            }

            ImGui.SameLine();
            ImGui.TextUnformatted("Scan for Devices");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        //  Device selector
        using (ImRaii.Disabled(!useMidi))
        {
            var devices = InputDeviceManager.Devices;
            var deviceNames = devices.Select(d => d.DeviceName()).ToArray();

            // Keep the combo index in sync with whatever is currently active.
            var current = InputDeviceManager.CurrentInputDevice;
            var currentName = current?.DeviceName() ?? string.Empty;
            var currentIdx = current is null ? -1 : System.Array.IndexOf(deviceNames, currentName);
            if (currentIdx != _selectedDeviceIndex)
                _selectedDeviceIndex = currentIdx;

            var previewLabel = current is null ? "None" : currentName;
            ImGui.Text("MIDI Devices:");
            ImGui.SetNextItemWidth(-1);
            using (var combo = ImRaii.Combo("##MidiDeviceCombo", previewLabel))
            {
                if (combo)
                {
                    // "None" option - disconnects the current device.
                    bool noneSelected = current is null;
                    if (ImGui.Selectable("None##device_none", noneSelected))
                    {
                        mgr.DisposeCurrentInputDevice();
                        cfg.LastUsedMidiDeviceName = string.Empty;
                        _selectedDeviceIndex = -1;
                        Context.Plugin.SaveConfig();
                    }

                    for (int i = 0; i < devices.Length; i++)
                    {
                        bool isSelected = i == _selectedDeviceIndex;
                        if (ImGui.Selectable($"{deviceNames[i]}##device_{i}", isSelected))
                        {
                            _selectedDeviceIndex = i;
                            mgr.SetDevice(devices[i]);
                            Context.Plugin.SaveConfig();
                        }
                    }
                }
            }

            ImGui.Spacing();

            //  Status row
            if (current is not null)
            {
                bool listening = mgr.IsListeningForEvents;

                ImGui.TextUnformatted("Active device: ");
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, listening
                    ? Style.Colors.GrassGreen
                    : Style.Colors.Yellow))
                {
                    ImGui.TextUnformatted(currentName);
                }

                ImGui.SameLine();
                var badge = listening ? "(listening)" : "(not listening)";
                using (ImRaii.PushColor(ImGuiCol.Text, listening
                    ? Style.Colors.GrassGreen
                    : Style.Colors.Yellow))
                {
                    ImGui.TextUnformatted(badge);
                }

                ImGui.Spacing();

                // Disconnect button
                if (ImGuiUtil.DangerButton("Disconnect##MidiDisconnect"))
                {
                    mgr.DisposeCurrentInputDevice();
                    cfg.LastUsedMidiDeviceName = string.Empty;
                    _selectedDeviceIndex = -1;
                    Context.Plugin.SaveConfig();
                }
                ImGuiUtil.ToolTip("Disconnect the current MIDI device.");
            }
            else
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Gray))
                    ImGui.TextUnformatted("No device connected.");
            }
        }
    }
}
