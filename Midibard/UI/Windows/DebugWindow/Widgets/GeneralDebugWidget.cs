using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;

using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace MidiBard;

public sealed class GeneralDebugWidget : Widget
{
    public override string Title => "General";
    public string color = string.Empty;
    public string _inputText = string.Empty;

    public GeneralDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        if (ImGui.Button("Toggle Ensemble Window"))
        {
            Context.Plugin.Ui.EnsembleWindow.Toggle();
        }

        ImGui.Text($"PID: {Environment.ProcessId}");

        if (ImGui.Button("Get setting"))
        {
            DalamudApi.GameConfig.System.TryGet("Fps", out uint fps);
            DalamudApi.PluginLog.Warning($"fps: {fps}");
            // DalamudApi.PluginLog.Warning($"{backgroundFrameLimit}");
            // var backgroundFrameLimit = MidiBard.AgentConfigSystem.BackgroundFrameLimit;
        }

        if (ImGui.Button("GetImgui Colors"))
        {
            // var btn1 = ImGui.ColorConvertU32ToFloat4(0xFF000000 | 0x005E5BFF);
            // DalamudApi.PluginLog.Warning(vec4print(*ImGui.GetStyleColorVec4(ImGuiCol.Button)));
            // DalamudApi.PluginLog.Warning(vec4print(ImGui.ColorConvertU32ToFloat4(0xFFFFA8A8)));

            try
            {

                var sb = new StringBuilder();
                foreach (var imGuiColItem in GetAllImGuiColors())
                {
                    sb.AppendLine($"{imGuiColItem.PropName} = {imGuiColItem.Vector4Str};");
                }

                var filePathInfo = new FileInfo(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + $@"\ImGuiCol.txt");

                File.WriteAllText(filePathInfo.FullName, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // ignored
            }
        }

        // ImGuiCol.
        ImGui.Text($"Convert Color");
        ImGui.InputText("Hex Color", ref color, 1000);
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy##ConvertColor"))
        {
            if (!TryParseHexColorExpression(color, out var colorInt))
            {
                ImGuiUtil.AddNotification(NotificationType.Error, "Invalid color value");
                return;
            }

            var vector4Text = Vec4Print(ImGui.ColorConvertU32ToFloat4(colorInt));
            ImGui.SetClipboardText($"{vector4Text}");
            color = "";
            ImGuiUtil.AddNotification(NotificationType.Success, "Color copied");

        }

        // if (Button("open"))
        // {
        //     fileDialogManager.OpenFileDialog("Import midi file", ".mid", (b, strings) =>
        //     {
        //         DalamudApi.PluginLog.Information($"{b}\n{string.Join("\n", strings)}");
        //         if (b) ImportMidiFiles(strings);
        //     });
        // }
        // if (Button("close"))
        // {
        //     fileDialogManager.Reset();
        // }

        // if (MidiBard.Plugin.Config.DebugEnsemble)
        // {
        //     EnsemblePartyList();
        // }

        // if (setup)
        // {
        //     setup = false;
        //     PartyWatcher.Instance.PartyMemberJoin += member =>
        //         {
        //             try
        //             {
        //                 DalamudApi.PluginLog.Information($"[++]{member:X}");
        //             }
        //             catch (Exception e)
        //             {
        //                 DalamudApi.PluginLog.Error(e.ToString());
        //             }
        //         };
        //     PartyWatcher.Instance.PartyMemberLeave += member =>
        //     {
        //         try
        //         {
        //             DalamudApi.PluginLog.Information($"[--]{member:X}");
        //         }
        //         catch (Exception e)
        //         {
        //             DalamudApi.PluginLog.Error(e.ToString());
        //         }
        //     };
        // }

        if (ImGui.Button("Test Log Warning"))
        {
            DalamudApi.PluginLog.Warning($"{Context.Plugin.Config.AlignMidi}");
        }


        var io = ImGui.GetIO();

        ImGui.Begin("Keyboard Debug");

        ImGui.Text($"WantCaptureKeyboard: {io.WantCaptureKeyboard}");
        ImGui.Text($"WindowFocused: {ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)}");

        ImGui.InputText("test focus", ref _inputText);
        unsafe
        {
            bool ctrlA = UIInputData.Instance()->IsKeyDown(SeVirtualKey.CONTROL) && UIInputData.Instance()->IsKeyPressed(SeVirtualKey.A);
            bool ctrlUp = UIInputData.Instance()->IsKeyDown(SeVirtualKey.CONTROL) && UIInputData.Instance()->IsKeyPressed(SeVirtualKey.UP);
            bool ctrlDown = UIInputData.Instance()->IsKeyDown(SeVirtualKey.CONTROL) && UIInputData.Instance()->IsKeyPressed(SeVirtualKey.DOWN);
            bool delete1 = UIInputData.Instance()->IsKeyDown(SeVirtualKey.DELETE);
            bool delete2 = UIInputData.Instance()->IsKeyPressed(SeVirtualKey.DELETE);
            ImGui.Text($"Ctrl + up: {ctrlUp}");
            ImGui.Text($"Ctrl + down: {ctrlDown}");
            ImGui.Text($"ctrl + a: {ctrlA}");

            ImGui.Text($"delete1: {delete1}");
            ImGui.Text($"delete2: {delete2}");
        }

        ImGui.Separator();

        // CTRL
        ImGui.Text($"Ctrl: {io.KeyCtrl}");

        ImGui.Text($"Shift: {ImGui.GetIO().KeyShift}");

        // Setas
        ImGui.Text($"Up Pressed: {ImGui.IsKeyPressed(ImGuiKey.UpArrow)}");
        ImGui.Text($"Up Down: {ImGui.IsKeyDown(ImGuiKey.UpArrow)}");

        ImGui.Text($"Down Pressed: {ImGui.IsKeyPressed(ImGuiKey.DownArrow)}");
        ImGui.Text($"Down Down: {ImGui.IsKeyDown(ImGuiKey.DownArrow)}");

        // A
        ImGui.Text($"A Pressed: {ImGui.IsKeyPressed(ImGuiKey.A)}");
        ImGui.Text($"A Down: {ImGui.IsKeyDown(ImGuiKey.A)}");

        // Delete / Escape
        ImGui.Text($"Delete Pressed: {ImGui.IsKeyPressed(ImGuiKey.Delete)}");
        ImGui.Text($"Delete Down: {ImGui.IsKeyDown(ImGuiKey.Delete)}");

        ImGui.Text($"Escape Pressed: {ImGui.IsKeyPressed(ImGuiKey.Escape)}");
        ImGui.Text($"Escape Down: {ImGui.IsKeyDown(ImGuiKey.Escape)}");

        ImGui.End();

    }

    private static bool TryParseHexColorExpression(string input, out uint result)
    {
        result = 0;
        try
        {
            var parts = input.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed[2..];

                if (!uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var value))
                    return false;

                result |= value;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string Vec4Print(Vector4 color)
    {
        return $"new Vector4({color.X.ToString().Replace(',', '.')}f, {color.Y.ToString().Replace(',', '.')}f, {color.Z.ToString().Replace(',', '.')}f, {color.W.ToString().Replace(',', '.')}f)";
    }

    public static unsafe List<ColorEntry> GetAllImGuiColors()
    {
        var colors = new List<ColorEntry>();

        void Add(ImGuiCol col, string name)
        {
            colors.Add(new ColorEntry
            {
                PropName = name,
                Vector4Str = Vec4Print(*ImGui.GetStyleColorVec4(col)),
            });
        }

        Add(ImGuiCol.Text, "Text");
        Add(ImGuiCol.TextDisabled, "TextDisabled");
        Add(ImGuiCol.TextSelectedBg, "TextSelectedBg");
        Add(ImGuiCol.WindowBg, "WindowBg");
        Add(ImGuiCol.MenuBarBg, "MenuBarBg");
        Add(ImGuiCol.ChildBg, "ChildBg");
        Add(ImGuiCol.PopupBg, "PopupBg");
        Add(ImGuiCol.Border, "Border");
        Add(ImGuiCol.BorderShadow, "BorderShadow");
        Add(ImGuiCol.FrameBg, "FrameBg");
        Add(ImGuiCol.FrameBgHovered, "FrameBgHovered");
        Add(ImGuiCol.FrameBgActive, "FrameBgActive");
        Add(ImGuiCol.TitleBg, "TitleBg");
        Add(ImGuiCol.TitleBgActive, "TitleBgActive");
        Add(ImGuiCol.TitleBgCollapsed, "TitleBgCollapsed");
        Add(ImGuiCol.ScrollbarBg, "ScrollbarBg");
        Add(ImGuiCol.ScrollbarGrab, "ScrollbarGrab");
        Add(ImGuiCol.ScrollbarGrabHovered, "ScrollbarGrabHovered");
        Add(ImGuiCol.ScrollbarGrabActive, "ScrollbarGrabActive");
        Add(ImGuiCol.CheckMark, "CheckMark");
        Add(ImGuiCol.SliderGrab, "SliderGrab");
        Add(ImGuiCol.SliderGrabActive, "SliderGrabActive");
        Add(ImGuiCol.Button, "Button");
        Add(ImGuiCol.ButtonHovered, "ButtonHovered");
        Add(ImGuiCol.ButtonActive, "ButtonActive");
        Add(ImGuiCol.Header, "Header");
        Add(ImGuiCol.HeaderHovered, "HeaderHovered");
        Add(ImGuiCol.HeaderActive, "HeaderActive");
        Add(ImGuiCol.Separator, "Separator");
        Add(ImGuiCol.SeparatorHovered, "SeparatorHovered");
        Add(ImGuiCol.SeparatorActive, "SeparatorActive");
        Add(ImGuiCol.ResizeGrip, "ResizeGrip");
        Add(ImGuiCol.ResizeGripHovered, "ResizeGripHovered");
        Add(ImGuiCol.ResizeGripActive, "ResizeGripActive");
        Add(ImGuiCol.Tab, "Tab");
        Add(ImGuiCol.TabHovered, "TabHovered");
        Add(ImGuiCol.TabActive, "TabActive");
        Add(ImGuiCol.TabUnfocused, "TabUnfocused");
        Add(ImGuiCol.TabUnfocusedActive, "TabUnfocusedActive");
        Add(ImGuiCol.DockingPreview, "DockingPreview");
        Add(ImGuiCol.DockingEmptyBg, "DockingEmptyBg");
        Add(ImGuiCol.PlotLines, "PlotLines");
        Add(ImGuiCol.PlotLinesHovered, "PlotLinesHovered");
        Add(ImGuiCol.PlotHistogram, "PlotHistogram");
        Add(ImGuiCol.PlotHistogramHovered, "PlotHistogramHovered");
        Add(ImGuiCol.TableHeaderBg, "TableHeaderBg");
        Add(ImGuiCol.TableBorderStrong, "TableBorderStrong");
        Add(ImGuiCol.TableBorderLight, "TableBorderLight");
        Add(ImGuiCol.TableRowBg, "TableRowBg");
        Add(ImGuiCol.TableRowBgAlt, "TableRowBgAlt");
        Add(ImGuiCol.NavHighlight, "NavHighlight");
        Add(ImGuiCol.NavWindowingHighlight, "NavWindowingHighlight");
        Add(ImGuiCol.NavWindowingDimBg, "NavWindowingDimBg");
        Add(ImGuiCol.DragDropTarget, "DragDropTarget");
        Add(ImGuiCol.ModalWindowDimBg, "ModalWindowDimBg");

        return colors;
    }
}

public class ColorEntry
{
    public string PropName { get; set; }
    public string Vector4Str { get; set; }
}

