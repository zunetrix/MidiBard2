using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;
using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

public sealed class MidiDeviceSettingsWidget : Widget
{
    public override string Title => Language.setting_midi_devices_title;
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Keyboard;

    private int _selectedDeviceIndex = -1;

    public MidiDeviceSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var cfg = Context.Plugin.Config;
        var mgr = Context.Plugin.InputDeviceManager;

        //  Enable / Disable toggle
        bool useMidi = cfg.UseMidiInputDevice;
        if (ImGui.Checkbox(Language.setting_perf_enable_midi_device, ref useMidi))
        {
            cfg.UseMidiInputDevice = useMidi;
            // The OnConfigurationChanged event will trigger the InputDeviceManager to start/stop the thread
            Context.Plugin.SaveConfig();
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.HelpMarker(Language.setting_perf_enable_midi_device_tooltip);

        ImGui.Spacing();

        //  Manual scan
        using (ImRaii.Disabled(!useMidi))
        {
            if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Sync, "##MidiScanBtn", Language.setting_midi_device_scan_devices))
            {
                mgr.TriggerManualScan();
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(Language.setting_midi_device_scan_devices);
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
            ImGui.Text(Language.setting_midi_devices_title);
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

                ImGui.TextUnformatted(Language.main_status_listening_midi_device);
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
                if (ImGuiUtil.DangerButton($"{Language.setting_midi_device_disconnect_devices}##MidiDeviceDisconnect"))
                {
                    mgr.DisposeCurrentInputDevice();
                    cfg.LastUsedMidiDeviceName = string.Empty;
                    _selectedDeviceIndex = -1;
                    Context.Plugin.SaveConfig();
                }
            }
            else
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Gray))
                    ImGui.TextUnformatted(Language.setting_midi_device_status_not_connected);
            }
        }
    }
}
