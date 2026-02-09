using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

public sealed class KeyStrokeDebugWidget : Widget
{
    public override string Title => "Key Stroke";

    public KeyStrokeDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        // ImGui.TextUnformatted($"useRawHook: {Testhooks.Instance?.playnoteHook?.IsEnabled}");
        // if (ImGui.Button("useRawhook"))
        // {
        //     if (Testhooks.Instance.playnoteHook.IsEnabled)
        //         Testhooks.Instance.playnoteHook.Disable();
        //     else
        //         Testhooks.Instance.playnoteHook.Enable();
        // }
        // ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        // ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
        // ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1);
        // var wdl = ImGui.GetWindowDrawList();
        // wdl.ChannelsSplit(2);
        // for (int i = Testhooks.min; i <= Testhooks.max; i++)
        // {
        //     var note = (i - Testhooks.min + 1) % 12;
        //     var vector2 = new Vector2(40, 300);
        //     var cursorPosX = GetCursorPosX();
        //     if (note is 2 or 4 or 7 or 9 or 11)
        //     {
        //         wdl.ChannelsSetCurrent(0);
        //         ImGui.SetCursorPosX(cursorPosX - 20);
        //         vector2.Y = 200;
        //     }
        //     else
        //     {
        //         wdl.ChannelsSetCurrent(1);
        //     }

        //     if (ImGui.Button($"##b{i}", vector2) || ImGui.IsWindowFocused() && ImGui.IsItemHovered())
        //     {
        //         Testhooks.Instance.noteOn(i);
        //     }
        //     ImGui.SameLine();

        //     if (note is 2 or 4 or 7 or 9 or 11)
        //     {
        //         ImGui.SetCursorPosX(cursorPosX);
        //     }

        //     if (note == 0)
        //     {
        //         ImGui.Dummy(new Vector2(3, 0));
        //         ImGui.SameLine();
        //     }
        // }
        // wdl.ChannelsMerge();
        // ImGui.PopStyleVar(3);

        // if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        // {
        //     Testhooks.Instance.noteOff();
        // }

        // ImGui.Dummy(Vector2.Zero);
        // var configBase = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->SystemConfig.SystemConfigBase.ConfigBase;
        // var configBaseConfigCount = configBase.ConfigCount;
        // //Util.ShowObject(configBase);
        // if (ImGui.Button("logconfig"))
        // {
        //     int i = 0;
        //     while (true)
        //     {
        //         try
        //         {
        //             var entry = configBase.ConfigEntry[i++];

        //             // DalamudApi.PluginLog.Information(
        //             //     $"[{entry.Index:000}] {entry.Type} {(entry.Type != 1 ? "\t" : "")}{MemoryHelper.ReadStringNullTerminated((IntPtr)entry.Name),-40}" +
        //             //     (entry.Type != 1 ? $"{entry.Value.UInt,-10}{entry.Value.Float,-10}" : ""));

        //             if (entry.Index >= configBaseConfigCount - 1)
        //             {
        //                 break;
        //             }
        //         }
        //         catch (Exception e)
        //         {
        //             //DalamudApi.PluginLog.Information($"{i} {e.Message}");
        //         }
        //     }

        //     DalamudApi.PluginLog.Information(configBaseConfigCount.ToString());
        // }
        // End();

        // if (midiChannels && Begin(nameof(MidiBard) + "midiChannels"))
        // {
        //     TextUnformatted($"current channel: {BardPlayDevice.chan}");


        //     Spacing();
        //     for (var i = 0; i < CurrentOutputDevice.Channels.Length; i++)
        //     {
        //         var b = CurrentOutputDevice.CurrentChannel == i;
        //         if (b) PushStyleColor(ImGuiCol.Text, ImGuiColors.ParsedGreen);
        //         TextUnformatted($"[{i:00}]");
        //         SameLine(40);
        //         TextUnformatted($"{CurrentOutputDevice.Channels[i].Program}");
        //         SameLine(70);
        //         TextUnformatted($"{ProgramNames.GetGMProgramName(CurrentOutputDevice.Channels[i].Program)}");
        //         if (b) PopStyleColor();
        //     }
        // }
    }
}

