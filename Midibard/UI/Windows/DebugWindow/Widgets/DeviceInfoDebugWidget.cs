using System;
using System.Linq;

using Dalamud.Bindings.ImGui;

using Melanchall.DryWetMidi.Multimedia;


namespace MidiBard;

public sealed class DeviceInfoDebugWidget : Widget
{
    public override string Title => "Device Info";

    public DeviceInfoDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        try
        {
            //var devicesList = DeviceManager.Devices.Select(i => i.ToDeviceString()).ToArray();


            //var inputDevices = DeviceManager.Devices;
            ////ImGui.BeginListBox("##auofhiao", new Vector2(-1, ImGui.GetTextLineHeightWithSpacing()* (inputDevices.Length + 1)));
            //if (ImGui.BeginCombo("Input Device", DeviceManager.CurrentInputDevice.ToDeviceString()))
            //{
            //    if (ImGui.Selectable("None##device", DeviceManager.CurrentInputDevice is null))
            //    {
            //        DeviceManager.DisposeDevice();
            //    }
            //    for (int i = 0; i < inputDevices.Length; i++)
            //    {
            //        var device = inputDevices[i];
            //        if (ImGui.Selectable($"{device.Name}##{i}", device.Id == DeviceManager.CurrentInputDevice?.Id))
            //        {
            //            DeviceManager.SetDevice(device);
            //        }
            //    }
            //    ImGui.EndCombo();
            //}


            //ImGui.EndListBox();

            //if (ImGui.ListBox("##????", ref InputDeviceID, devicesList, devicesList.Length))
            //{
            //    if (InputDeviceID == 0)
            //    {
            //        DeviceManager.DisposeDevice();
            //    }
            //    else
            //    {
            //        DeviceManager.SetDevice(InputDevice.GetByName(devicesList[InputDeviceID]));
            //    }
            //}

            if (ImGui.SmallButton("Start Event Listening"))
            {
                InputDeviceManager.CurrentInputDevice?.StartEventsListening();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Stop Event Listening"))
            {
                InputDeviceManager.CurrentInputDevice?.StopEventsListening();
            }

            ImGui.Text($"InputDevices: {InputDevice.GetDevicesCount()}\n{string.Join("\n", InputDevice.GetAll().Select(i => $"[{i}] {i.Name}"))}");
            ImGui.Text($"OutputDevices: {OutputDevice.GetDevicesCount()}\n{string.Join("\n", OutputDevice.GetAll().Select(i => $"[{i}] {i.Name}"))}");

            ImGui.Text($"CurrentInputDevice: \n{InputDeviceManager.CurrentInputDevice} Listening: {InputDeviceManager.CurrentInputDevice?.IsListeningForEvents}");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e.ToString());
        }


        //ImGui.Separator();

        //if (ImGui.BeginChild("Generate", new Vector2(size - 5, 150), false, ImGuiWindowFlags.NoDecoration))
        //{
        //    ImGui.DragInt("length##keyboard", ref Plugin.Config.testLength, 0.05f);
        //    ImGui.DragInt("interval##keyboard", ref Plugin.Config.testInterval, 0.05f);
        //    ImGui.DragInt("repeat##keyboard", ref Plugin.Config.testRepeat, 0.05f);
        //    if (Plugin.Config.testLength < 0)
        //    {
        //        Plugin.Config.testLength = 0;
        //    }

        //    if (Plugin.Config.testInterval < 0)
        //    {
        //        Plugin.Config.testInterval = 0;
        //    }

        //    if (Plugin.Config.testRepeat < 0)
        //    {
        //        Plugin.Config.testRepeat = 0;
        //    }

        //    if (ImGui.Button("generate##keyboard"))
        //    {
        //        try
        //        {
        //            testplayback?.Dispose();

        //        }
        //        catch (Exception e)
        //        {
        //            //
        //        }

        //        static Pattern GetSequence(int octave)
        //        {
        //            return new PatternBuilder()
        //                .SetRootNote(Note.Get(NoteName.C, octave))
        //                .SetNoteLength(new MetricTimeSpan(0, 0, 0, Plugin.Config.testLength))
        //                .SetStep(new MetricTimeSpan(0, 0, 0, Plugin.Config.testInterval))
        //                .Note(Interval.Zero)
        //                .StepForward()
        //                .Note(Interval.One)
        //                .StepForward()
        //                .Note(Interval.Two)
        //                .StepForward()
        //                .Note(Interval.Three)
        //                .StepForward()
        //                .Note(Interval.Four)
        //                .StepForward()
        //                .Note(Interval.Five)
        //                .StepForward()
        //                .Note(Interval.Six)
        //                .StepForward()
        //                .Note(Interval.Seven)
        //                .StepForward()
        //                .Note(Interval.Eight)
        //                .StepForward()
        //                .Note(Interval.Nine)
        //                .StepForward()
        //                .Note(Interval.Ten)
        //                .StepForward()
        //                .Note(Interval.Eleven)
        //                .StepForward().Build();
        //        }

        //        static Pattern GetSequenceDown(int octave)
        //        {
        //            return new PatternBuilder()
        //                .SetRootNote(Note.Get(NoteName.C, octave))
        //                .SetNoteLength(new MetricTimeSpan(0, 0, 0, Plugin.Config.testLength))
        //                .SetStep(new MetricTimeSpan(0, 0, 0, Plugin.Config.testInterval))
        //                .Note(Interval.Eleven)
        //                .StepForward()
        //                .Note(Interval.Ten)
        //                .StepForward()
        //                .Note(Interval.Nine)
        //                .StepForward()
        //                .Note(Interval.Eight)
        //                .StepForward()
        //                .Note(Interval.Seven)
        //                .StepForward()
        //                .Note(Interval.Six)
        //                .StepForward()
        //                .Note(Interval.Five)
        //                .StepForward()
        //                .Note(Interval.Four)
        //                .StepForward()
        //                .Note(Interval.Three)
        //                .StepForward()
        //                .Note(Interval.Two)
        //                .StepForward()
        //                .Note(Interval.One)
        //                .StepForward()
        //                .Note(Interval.Zero)
        //                .StepForward()
        //                .Build();
        //        }

        //        Pattern pattern = new PatternBuilder()

        //            .SetNoteLength(new MetricTimeSpan(0, 0, 0, Plugin.Config.testLength))
        //            .SetStep(new MetricTimeSpan(0, 0, 0, Plugin.Config.testInterval))

        //            .Pattern(GetSequence(3))
        //            .Pattern(GetSequence(4))
        //            .Pattern(GetSequence(5))
        //            .SetRootNote(Note.Get(NoteName.C, 5))
        //            .StepForward()
        //            .Note(Interval.Twelve)
        //            .Pattern(GetSequenceDown(5))
        //            .Pattern(GetSequenceDown(4))
        //            .Pattern(GetSequenceDown(3))
        //            // Get pattern
        //            .Build();

        //        var repeat = new PatternBuilder().Pattern(pattern).Repeat(Plugin.Config.testRepeat).Build();

        //        testplayback = repeat.ToTrackChunk(TempoMap.Default).GetPlayback(TempoMap.Default, Plugin.CurrentOutputDevice,
        //            new MidiClockSettings() { CreateTickGeneratorCallback = () => new HighPrecisionTickGenerator() });
        //    }

        //    ImGui.SameLine();
        //    if (ImGui.Button("chord##keyboard"))
        //    {
        //        try
        //        {
        //            testplayback?.Dispose();

        //        }
        //        catch (Exception e)
        //        {
        //            //
        //        }

        //        var pattern = new PatternBuilder()
        //            //.SetRootNote(Note.Get(NoteName.C, 3))
        //            //C-G-Am-(G,Em,C/G)-F-(C,Em)-(F,Dm)-G
        //            .SetOctave(Octave.Get(3))
        //            .SetStep(new MetricTimeSpan(0, 0, 0, Plugin.Config.testInterval))
        //            .Chord(Chord.GetByTriad(NoteName.C, ChordQuality.Major)).Repeat(Plugin.Config.testRepeat)
        //            .Chord(Chord.GetByTriad(NoteName.G, ChordQuality.Major)).Repeat(Plugin.Config.testRepeat)
        //            .Chord(Chord.GetByTriad(NoteName.A, ChordQuality.Minor)).Repeat(Plugin.Config.testRepeat)
        //            .Chord(Chord.GetByTriad(NoteName.G, ChordQuality.Major)).Repeat(Plugin.Config.testRepeat)
        //            .Chord(Chord.GetByTriad(NoteName.F, ChordQuality.Major)).Repeat(Plugin.Config.testRepeat)
        //            .Chord(Chord.GetByTriad(NoteName.C, ChordQuality.Major)).Repeat(Plugin.Config.testRepeat)
        //            .Chord(Chord.GetByTriad(NoteName.F, ChordQuality.Major)).Repeat(Plugin.Config.testRepeat)
        //            .Chord(Chord.GetByTriad(NoteName.G, ChordQuality.Major)).Repeat(Plugin.Config.testRepeat)

        //            .Build();

        //        testplayback = pattern.ToTrackChunk(TempoMap.Default).GetPlayback(TempoMap.Default, Plugin.CurrentOutputDevice,
        //            new MidiClockSettings() { CreateTickGeneratorCallback = () => new HighPrecisionTickGenerator() });
        //    }

        //    ImGui.Spacing();
        //    if (ImGui.Button("play##keyboard"))
        //    {
        //        try
        //        {
        //            testplayback?.MoveToStart();
        //            testplayback?.Start();
        //        }
        //        catch (Exception e)
        //        {
        //            DalamudApi.PluginLog.Error(e.ToString());
        //        }
        //    }

        //    ImGui.SameLine();
        //    if (ImGui.Button("dispose##keyboard"))
        //    {
        //        try
        //        {
        //            testplayback?.Dispose();
        //        }
        //        catch (Exception e)
        //        {
        //            DalamudApi.PluginLog.Error(e.ToString());
        //        }
        //    }

        //    try
        //    {
        //        ImGui.Text($"{testplayback.GetDuration(TimeSpanType.Metric)}");
        //    }
        //    catch (Exception e)
        //    {
        //        ImGui.Text("null");
        //    }
        //    //ImGui.SetNextItemWidth(120);
        //    //UIcurrentInstrument = Plugin.CurrentInstrument;
        //    //if (ImGui.ListBox("##instrumentSwitch", ref UIcurrentInstrument, InstrumentSheet.Select(i => i.Instrument.ToString()).ToArray(), (int)InstrumentSheet.RowCount, (int)InstrumentSheet.RowCount))
        //    //{
        //    //    Task.Run(() => SwitchInstrument.SwitchToAsync((uint)UIcurrentInstrument));
        //    //}

        //    //if (ImGui.Button("Quit"))
        //    //{
        //    //    Task.Run(() => SwitchInstrument.SwitchToAsync(0));
        //    //}

        //    ImGui.EndChild();
        //}

    }
}

