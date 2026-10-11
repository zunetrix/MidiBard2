using System;
using System.Numerics;
using System.Text.RegularExpressions;
using MidiBard.Extensions.Json;
using MidiBard.Extensions.String;

namespace MidiBard;

public class CustomThemeData : IUiTheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Custom Theme";

    public Vector4 Text { get; set; }
    public Vector4 TextDisabled { get; set; }
    public Vector4 TextSelectedBg { get; set; }
    public Vector4 WindowBg { get; set; }
    public Vector4 MenuBarBg { get; set; }
    public Vector4 ChildBg { get; set; }
    public Vector4 PopupBg { get; set; }
    public Vector4 Border { get; set; }
    public Vector4 BorderShadow { get; set; }
    public Vector4 FrameBg { get; set; }
    public Vector4 FrameBgHovered { get; set; }
    public Vector4 FrameBgActive { get; set; }
    public Vector4 TitleBg { get; set; }
    public Vector4 TitleBgActive { get; set; }
    public Vector4 TitleBgCollapsed { get; set; }
    public Vector4 ScrollbarBg { get; set; }
    public Vector4 ScrollbarGrab { get; set; }
    public Vector4 ScrollbarGrabHovered { get; set; }
    public Vector4 ScrollbarGrabActive { get; set; }
    public Vector4 CheckMark { get; set; }
    public Vector4 SliderGrab { get; set; }
    public Vector4 SliderGrabActive { get; set; }
    public Vector4 Button { get; set; }
    public Vector4 ButtonHovered { get; set; }
    public Vector4 ButtonActive { get; set; }
    public Vector4 Header { get; set; }
    public Vector4 HeaderHovered { get; set; }
    public Vector4 HeaderActive { get; set; }
    public Vector4 Separator { get; set; }
    public Vector4 SeparatorHovered { get; set; }
    public Vector4 SeparatorActive { get; set; }
    public Vector4 ResizeGrip { get; set; }
    public Vector4 ResizeGripHovered { get; set; }
    public Vector4 ResizeGripActive { get; set; }
    public Vector4 Tab { get; set; }
    public Vector4 TabHovered { get; set; }
    public Vector4 TabActive { get; set; }
    public Vector4 TabUnfocused { get; set; }
    public Vector4 TabUnfocusedActive { get; set; }
    public Vector4 DockingPreview { get; set; }
    public Vector4 DockingEmptyBg { get; set; }
    public Vector4 PlotLines { get; set; }
    public Vector4 PlotLinesHovered { get; set; }
    public Vector4 PlotHistogram { get; set; }
    public Vector4 PlotHistogramHovered { get; set; }
    public Vector4 TableHeaderBg { get; set; }
    public Vector4 TableBorderStrong { get; set; }
    public Vector4 TableBorderLight { get; set; }
    public Vector4 TableRowBg { get; set; }
    public Vector4 TableRowBgAlt { get; set; }
    public Vector4 NavHighlight { get; set; }
    public Vector4 NavWindowingHighlight { get; set; }
    public Vector4 NavWindowingDimBg { get; set; }
    public Vector4 DragDropTarget { get; set; }
    public Vector4 ModalWindowDimBg { get; set; }

    // Style metrics - stored as fields so they can be passed by ref
    public float? WindowRounding;
    public float? FrameRounding;
    public float? ScrollbarRounding;

    // IUiTheme interface implementation
    float? IUiTheme.WindowRounding => WindowRounding;
    float? IUiTheme.FrameRounding => FrameRounding;
    float? IUiTheme.ScrollbarRounding => ScrollbarRounding;

    public CustomThemeData() { }

    public static CustomThemeData CreateFrom(IUiTheme source, string name)
    {
        return new CustomThemeData
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Text = source.Text,
            TextDisabled = source.TextDisabled,
            TextSelectedBg = source.TextSelectedBg,
            WindowBg = source.WindowBg,
            MenuBarBg = source.MenuBarBg,
            ChildBg = source.ChildBg,
            PopupBg = source.PopupBg,
            Border = source.Border,
            BorderShadow = source.BorderShadow,
            FrameBg = source.FrameBg,
            FrameBgHovered = source.FrameBgHovered,
            FrameBgActive = source.FrameBgActive,
            TitleBg = source.TitleBg,
            TitleBgActive = source.TitleBgActive,
            TitleBgCollapsed = source.TitleBgCollapsed,
            ScrollbarBg = source.ScrollbarBg,
            ScrollbarGrab = source.ScrollbarGrab,
            ScrollbarGrabHovered = source.ScrollbarGrabHovered,
            ScrollbarGrabActive = source.ScrollbarGrabActive,
            CheckMark = source.CheckMark,
            SliderGrab = source.SliderGrab,
            SliderGrabActive = source.SliderGrabActive,
            Button = source.Button,
            ButtonHovered = source.ButtonHovered,
            ButtonActive = source.ButtonActive,
            Header = source.Header,
            HeaderHovered = source.HeaderHovered,
            HeaderActive = source.HeaderActive,
            Separator = source.Separator,
            SeparatorHovered = source.SeparatorHovered,
            SeparatorActive = source.SeparatorActive,
            ResizeGrip = source.ResizeGrip,
            ResizeGripHovered = source.ResizeGripHovered,
            ResizeGripActive = source.ResizeGripActive,
            Tab = source.Tab,
            TabHovered = source.TabHovered,
            TabActive = source.TabActive,
            TabUnfocused = source.TabUnfocused,
            TabUnfocusedActive = source.TabUnfocusedActive,
            DockingPreview = source.DockingPreview,
            DockingEmptyBg = source.DockingEmptyBg,
            PlotLines = source.PlotLines,
            PlotLinesHovered = source.PlotLinesHovered,
            PlotHistogram = source.PlotHistogram,
            PlotHistogramHovered = source.PlotHistogramHovered,
            TableHeaderBg = source.TableHeaderBg,
            TableBorderStrong = source.TableBorderStrong,
            TableBorderLight = source.TableBorderLight,
            TableRowBg = source.TableRowBg,
            TableRowBgAlt = source.TableRowBgAlt,
            NavHighlight = source.NavHighlight,
            NavWindowingHighlight = source.NavWindowingHighlight,
            NavWindowingDimBg = source.NavWindowingDimBg,
            DragDropTarget = source.DragDropTarget,
            ModalWindowDimBg = source.ModalWindowDimBg,
            WindowRounding = source.WindowRounding,
            FrameRounding = source.FrameRounding,
            ScrollbarRounding = source.ScrollbarRounding,
        };
    }

    public CustomThemeData Clone(string newName) => CreateFrom(this, newName);

    public string ExportToString()
    {
        return this.JsonSerialize().Compress();
    }

    public static CustomThemeData ImportFromString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Theme import string is empty.");

        input = input.Trim();
        input = Regex.Replace(
            input,
            @"^```[a-zA-Z]*\r?\n?|```$",
            string.Empty,
            RegexOptions.Multiline).Trim();

        string json;
        if (input.StartsWith("{") && input.EndsWith("}"))
        {
            json = input;
        }
        else
        {
            json = input.Decompress();
        }

        var theme = json.JsonDeserialize<CustomThemeData>();
        if (theme == null)
            throw new ArgumentException("Invalid theme data.");

        theme.Id = Guid.NewGuid().ToString();
        if (string.IsNullOrWhiteSpace(theme.Name))
            theme.Name = "Imported Theme";

        return theme;
    }
}
