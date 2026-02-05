using System;
using System.Linq;
using System.Threading;

using Dalamud.Interface.ImGuiNotification;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

using MidiBard.Control;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Resources;

namespace MidiBard;

internal class InputDeviceManager : IDisposable
{
    private Plugin Plugin { get; }
    internal static bool ShouldScanMidiDeviceThread = true;
    internal static InputDevice CurrentInputDevice { get; private set; }
    internal static string[] LastDevicesNames { get; private set; } = [];
    internal static InputDevice[] Devices { get; private set; } = [];
    internal readonly Thread ScanMidiDeviceThread;

    public InputDeviceManager(Plugin plugin)
    {
        Plugin = plugin;

        ScanMidiDeviceThread = new Thread(ScanMidiDeviceLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };

        ScanMidiDeviceThread.Start();
    }

    public void Dispose()
    {
        ShouldScanMidiDeviceThread = false;
        DisposeCurrentInputDevice();
    }

    private void ScanMidiDeviceLoop()
    {
        DalamudApi.PluginLog.Information("device scanning thread started.");

        while (ShouldScanMidiDeviceThread)
        {
            try
            {
                Devices = InputDevice.GetAll().OrderBy(i => i.Name).ToArray();
                var devicesNames = Devices.Select(i => i.DeviceName()).ToArray();

                if (CurrentInputDevice is not null)
                {
                    if (!devicesNames.Contains(CurrentInputDevice.DeviceName()))
                    {
                        DalamudApi.PluginLog.Debug("disposing disconnected device");
                        DisposeCurrentInputDevice();
                    }
                }
                else
                {
                    if (devicesNames.Contains(Plugin.Config.lastUsedMidiDeviceName))
                    {
                        DalamudApi.PluginLog.Information(
                            $"try restoring midi device: \"{Plugin.Config.lastUsedMidiDeviceName}\"");

                        var newDevice = Devices.FirstOrDefault(
                            i => i.Name == Plugin.Config.lastUsedMidiDeviceName);

                        if (newDevice != null)
                            SetDevice(newDevice);
                    }
                }
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error in midi device scanning thread");
            }

            Thread.Sleep(500);
        }

        DalamudApi.PluginLog.Information("device scanning thread ended.");
    }

    internal bool IsListeningForEvents
    {
        get
        {
            var ret = false;
            try
            {
                ret = CurrentInputDevice?.IsListeningForEvents == true;
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Debug(e, "device maybe disposed.");
            }

            return ret;
        }
    }

    internal void SetDevice(InputDevice device)
    {
        DisposeCurrentInputDevice();
        Plugin.Config.lastUsedMidiDeviceName = device?.DeviceName();
        if (device is null) return;

        try
        {
            CurrentInputDevice = device;
            CurrentInputDevice.SilentNoteOnPolicy = SilentNoteOnPolicy.NoteOff;
            CurrentInputDevice.EventReceived += InputDevice_EventReceived;
            CurrentInputDevice.StartEventsListening();
            ImGuiUtil.AddNotification(NotificationType.Success,
                string.Format(Language.text_start_event_listening, CurrentInputDevice.Name));
        }
        catch (Exception e)
        {
            Plugin.Config.lastUsedMidiDeviceName = "";
            ImGuiUtil.AddNotification(NotificationType.Error,
                string.Format(Language.notice_midi_device_error, CurrentInputDevice.Name));
            DalamudApi.PluginLog.Error(e, "midi device is possibly being occupied.");
            DisposeCurrentInputDevice();
        }
    }

    public void DisposeCurrentInputDevice()
    {
        if (CurrentInputDevice == null) return;

        try
        {
            CurrentInputDevice.EventReceived -= InputDevice_EventReceived;
            CurrentInputDevice.Dispose();
            ImGuiUtil.AddNotification(NotificationType.Info, string.Format(Language.notice_midi_device_stop_listening, CurrentInputDevice.Name));
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "error when disposing existing Input device");
        }
        finally
        {
            CurrentInputDevice?.Dispose();
            CurrentInputDevice = null;
        }
    }

    private void InputDevice_EventReceived(object sender, MidiEventReceivedEventArgs e)
    {
        DalamudApi.PluginLog.Verbose($"[{sender}]{e.Event}");
        Plugin.BardPlayDevice.SendEventWithMetadata(e.Event, new BardPlayDevice.MidiDeviceMetaData());
    }
}
