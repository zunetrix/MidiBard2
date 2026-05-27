using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    private Thread _scanMidiDeviceThread;

    public InputDeviceManager(Plugin plugin)
    {
        Plugin = plugin;

        if (Plugin.Config.UseMidiInputDevice)
        {
            StartScanning();
        }

        Plugin.Config.OnConfigurationChanged += OnConfigurationChanged;
    }

    private void OnConfigurationChanged()
    {
        if (Plugin.Config.UseMidiInputDevice)
        {
            StartScanning();
        }
        else
        {
            StopScanning();
        }
    }

    public void StartScanning()
    {
        if (_scanMidiDeviceThread?.IsAlive == true) return;

        ShouldScanMidiDeviceThread = true;
        _scanMidiDeviceThread = new Thread(ScanMidiDeviceLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };

        _scanMidiDeviceThread.Start();
    }

    public void StopScanning()
    {
        ShouldScanMidiDeviceThread = false;
        DisposeCurrentInputDevice();
    }

    public void Dispose()
    {
        Plugin.Config.OnConfigurationChanged -= OnConfigurationChanged;
        StopScanning();
    }

    private void ScanMidiDeviceLoop()
    {
        DalamudApi.PluginLog.Information("Device scanning thread started.");

        while (ShouldScanMidiDeviceThread)
        {
            try
            {
                RunScanIteration();
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error in midi device scanning thread");
            }

            Thread.Sleep(500);
        }

        DalamudApi.PluginLog.Information("device scanning thread ended.");
    }

    /// <summary>
    /// Executes a single scan iteration: refreshes the device list and attempts to restore
    /// the last-used device when none is active.
    /// </summary>
    private void RunScanIteration()
    {
        Devices = InputDevice.GetAll().OrderBy(i => i.Name).ToArray();
        var devicesNames = Devices.Select(i => i.DeviceName()).ToArray();

        if (CurrentInputDevice is not null)
        {
            if (!devicesNames.Contains(CurrentInputDevice.DeviceName()))
            {
                DalamudApi.PluginLog.Debug("Disposing disconnected device");
                DisposeCurrentInputDevice();
            }
        }
        else
        {
            if (devicesNames.Contains(Plugin.Config.LastUsedMidiDeviceName))
            {
                DalamudApi.PluginLog.Information($"Try restoring midi device: \"{Plugin.Config.LastUsedMidiDeviceName}\"");

                var newDevice = Devices.FirstOrDefault(
                    i => i.Name == Plugin.Config.LastUsedMidiDeviceName);

                if (newDevice != null)
                    SetDevice(newDevice);
            }
        }
    }

    /// <summary>
    /// Triggers a non-blocking, one-shot device scan. Safe to call from the UI thread.
    /// </summary>
    internal void TriggerManualScan()
    {
        Task.Run(() =>
        {
            try
            {
                DalamudApi.PluginLog.Information("Manual MIDI device scan triggered.");
                RunScanIteration();
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "Error during manual MIDI device scan.");
            }
        });
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
                DalamudApi.PluginLog.Debug(e, "Device maybe disposed.");
            }

            return ret;
        }
    }

    internal void SetDevice(InputDevice device)
    {
        DisposeCurrentInputDevice();
        Plugin.Config.LastUsedMidiDeviceName = device?.DeviceName();
        if (device is null) return;

        try
        {
            CurrentInputDevice = device;
            CurrentInputDevice.SilentNoteOnPolicy = SilentNoteOnPolicy.NoteOff;
            CurrentInputDevice.EventReceived += InputDevice_EventReceived;
            CurrentInputDevice.StartEventsListening();
            ImGuiUtil.AddNotification(NotificationType.Success,
                string.Format(Language.notify_midi_device_start, CurrentInputDevice.Name));
        }
        catch (Exception e)
        {
            Plugin.Config.LastUsedMidiDeviceName = string.Empty;
            ImGuiUtil.AddNotification(NotificationType.Error,
                string.Format(Language.notify_midi_device_error, CurrentInputDevice.Name));
            DalamudApi.PluginLog.Error(e, "Midi device is possibly being occupied");
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
            ImGuiUtil.AddNotification(NotificationType.Info, string.Format(Language.notify_midi_device_stop, CurrentInputDevice.Name));
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "Error when disposing existing Input device");
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
