using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

public enum ThemeVariant
{
    Default = 0,
    Dark = 1,
    ModernDark = 2,
    Light = 3,
    OceanFishing = 4,
    DeepBlue = 5,
    Catnip = 6,
    Chocobo = 7,
    Dracula = 8,
    Neon = 9,
    Purple = 10,
    Wine = 11,
    BarbiePink = 12,
    CottonCandy = 13,
    Tropical = 14,
    Sunset = 15,
    Orange = 16,
}

public class ThemeManager
{
    public static IUiTheme CurrentTheme { get; private set; } = new DefaultTheme();

    public ThemeManager()
    {
        CurrentTheme = new DefaultTheme();
    }

    public ThemeManager(ThemeVariant themeType)
    {
        if (!Enum.IsDefined(themeType))
            themeType = ThemeVariant.Default;

        SetTheme(themeType);
    }

    public static void SetTheme(ThemeVariant themeType)
    {
        CurrentTheme = themeType switch
        {
            ThemeVariant.Default => new DefaultTheme(),
            ThemeVariant.Dark => new DarkTheme(),
            ThemeVariant.Light => new LightTheme(),
            ThemeVariant.OceanFishing => new OceanFishingTheme(),
            ThemeVariant.DeepBlue => new DeepBlueTheme(),
            ThemeVariant.Catnip => new CatnipTheme(),
            ThemeVariant.Chocobo => new ChocoboTheme(),
            ThemeVariant.Dracula => new DraculaTheme(),
            ThemeVariant.Wine => new WineTheme(),
            ThemeVariant.Purple => new PurpleTheme(),
            ThemeVariant.BarbiePink => new BarbiePinkTheme(),
            ThemeVariant.CottonCandy => new CottonCandyTheme(),
            ThemeVariant.Tropical => new TropicalTheme(),
            ThemeVariant.Neon => new NeonTheme(),
            ThemeVariant.ModernDark => new ModernDarkTheme(),
            ThemeVariant.Sunset => new SunsetTheme(),
            ThemeVariant.Orange => new OrangeTheme(),
            _ => new DefaultTheme()
        };
    }

    private int pushCount = 0;

    private void PushColor(ImGuiCol color, Vector4 value)
    {
        ImGui.PushStyleColor(color, value);
        pushCount++;
    }

    public void PopThemeStyles()
    {
        ImGui.PopStyleColor(pushCount);
        pushCount = 0;
    }

    public void PushThemeStyles()
    {
        PushColor(ImGuiCol.Text, CurrentTheme.Text);
        PushColor(ImGuiCol.TextDisabled, CurrentTheme.TextDisabled);
        PushColor(ImGuiCol.TextSelectedBg, CurrentTheme.TextSelectedBg);

        PushColor(ImGuiCol.WindowBg, CurrentTheme.WindowBg);
        PushColor(ImGuiCol.MenuBarBg, CurrentTheme.MenuBarBg);
        PushColor(ImGuiCol.ChildBg, CurrentTheme.ChildBg);
        PushColor(ImGuiCol.PopupBg, CurrentTheme.PopupBg);
        PushColor(ImGuiCol.ModalWindowDimBg, CurrentTheme.ModalWindowDimBg);

        PushColor(ImGuiCol.Border, CurrentTheme.Border);
        PushColor(ImGuiCol.BorderShadow, CurrentTheme.BorderShadow);

        PushColor(ImGuiCol.FrameBg, CurrentTheme.FrameBg);
        PushColor(ImGuiCol.FrameBgHovered, CurrentTheme.FrameBgHovered);
        PushColor(ImGuiCol.FrameBgActive, CurrentTheme.FrameBgActive);

        PushColor(ImGuiCol.TitleBg, CurrentTheme.TitleBg);
        PushColor(ImGuiCol.TitleBgActive, CurrentTheme.TitleBgActive);
        PushColor(ImGuiCol.TitleBgCollapsed, CurrentTheme.TitleBgCollapsed);

        PushColor(ImGuiCol.ScrollbarBg, CurrentTheme.ScrollbarBg);
        PushColor(ImGuiCol.ScrollbarGrab, CurrentTheme.ScrollbarGrab);
        PushColor(ImGuiCol.ScrollbarGrabHovered, CurrentTheme.ScrollbarGrabHovered);
        PushColor(ImGuiCol.ScrollbarGrabActive, CurrentTheme.ScrollbarGrabActive);

        PushColor(ImGuiCol.SliderGrab, CurrentTheme.SliderGrab);
        PushColor(ImGuiCol.SliderGrabActive, CurrentTheme.SliderGrabActive);

        PushColor(ImGuiCol.Separator, CurrentTheme.Separator);
        PushColor(ImGuiCol.SeparatorHovered, CurrentTheme.SeparatorHovered);
        PushColor(ImGuiCol.SeparatorActive, CurrentTheme.SeparatorActive);

        PushColor(ImGuiCol.ResizeGrip, CurrentTheme.ResizeGrip);
        PushColor(ImGuiCol.ResizeGripHovered, CurrentTheme.ResizeGripHovered);
        PushColor(ImGuiCol.ResizeGripActive, CurrentTheme.ResizeGripActive);

        PushColor(ImGuiCol.Tab, CurrentTheme.Tab);
        PushColor(ImGuiCol.TabHovered, CurrentTheme.TabHovered);
        PushColor(ImGuiCol.TabActive, CurrentTheme.TabActive);
        PushColor(ImGuiCol.TabUnfocused, CurrentTheme.TabUnfocused);
        PushColor(ImGuiCol.TabUnfocusedActive, CurrentTheme.TabUnfocusedActive);

        PushColor(ImGuiCol.DockingPreview, CurrentTheme.DockingPreview);
        PushColor(ImGuiCol.DockingEmptyBg, CurrentTheme.DockingEmptyBg);

        PushColor(ImGuiCol.PlotLines, CurrentTheme.PlotLines);
        PushColor(ImGuiCol.PlotLinesHovered, CurrentTheme.PlotLinesHovered);
        PushColor(ImGuiCol.PlotHistogram, CurrentTheme.PlotHistogram);
        PushColor(ImGuiCol.PlotHistogramHovered, CurrentTheme.PlotHistogramHovered);

        PushColor(ImGuiCol.TableHeaderBg, CurrentTheme.TableHeaderBg);
        PushColor(ImGuiCol.TableBorderStrong, CurrentTheme.TableBorderStrong);
        PushColor(ImGuiCol.TableBorderLight, CurrentTheme.TableBorderLight);
        PushColor(ImGuiCol.TableRowBg, CurrentTheme.TableRowBg);
        PushColor(ImGuiCol.TableRowBgAlt, CurrentTheme.TableRowBgAlt);

        PushColor(ImGuiCol.NavHighlight, CurrentTheme.NavHighlight);
        PushColor(ImGuiCol.NavWindowingHighlight, CurrentTheme.NavWindowingHighlight);
        PushColor(ImGuiCol.NavWindowingDimBg, CurrentTheme.NavWindowingDimBg);

        PushColor(ImGuiCol.Header, CurrentTheme.Header);
        PushColor(ImGuiCol.HeaderHovered, CurrentTheme.HeaderHovered);
        PushColor(ImGuiCol.HeaderActive, CurrentTheme.HeaderActive);

        PushColor(ImGuiCol.Button, CurrentTheme.Button);
        PushColor(ImGuiCol.ButtonHovered, CurrentTheme.ButtonHovered);
        PushColor(ImGuiCol.ButtonActive, CurrentTheme.ButtonActive);

        PushColor(ImGuiCol.DragDropTarget, CurrentTheme.DragDropTarget);
        PushColor(ImGuiCol.CheckMark, CurrentTheme.CheckMark);
    }
}

public interface IUiTheme
{
    Vector4 Text { get; }
    Vector4 TextDisabled { get; }
    Vector4 TextSelectedBg { get; }
    Vector4 WindowBg { get; }
    Vector4 MenuBarBg { get; }
    Vector4 ChildBg { get; }
    Vector4 PopupBg { get; }
    Vector4 Border { get; }
    Vector4 BorderShadow { get; }
    Vector4 FrameBg { get; }
    Vector4 FrameBgHovered { get; }
    Vector4 FrameBgActive { get; }
    Vector4 TitleBg { get; }
    Vector4 TitleBgActive { get; }
    Vector4 TitleBgCollapsed { get; }
    Vector4 ScrollbarBg { get; }
    Vector4 ScrollbarGrab { get; }
    Vector4 ScrollbarGrabHovered { get; }
    Vector4 ScrollbarGrabActive { get; }
    Vector4 CheckMark { get; }
    Vector4 SliderGrab { get; }
    Vector4 SliderGrabActive { get; }
    Vector4 Button { get; }
    Vector4 ButtonHovered { get; }
    Vector4 ButtonActive { get; }
    Vector4 Header { get; }
    Vector4 HeaderHovered { get; }
    Vector4 HeaderActive { get; }
    Vector4 Separator { get; }
    Vector4 SeparatorHovered { get; }
    Vector4 SeparatorActive { get; }
    Vector4 ResizeGrip { get; }
    Vector4 ResizeGripHovered { get; }
    Vector4 ResizeGripActive { get; }
    Vector4 Tab { get; }
    Vector4 TabHovered { get; }
    Vector4 TabActive { get; }
    Vector4 TabUnfocused { get; }
    Vector4 TabUnfocusedActive { get; }
    Vector4 DockingPreview { get; }
    Vector4 DockingEmptyBg { get; }
    Vector4 PlotLines { get; }
    Vector4 PlotLinesHovered { get; }
    Vector4 PlotHistogram { get; }
    Vector4 PlotHistogramHovered { get; }
    Vector4 TableHeaderBg { get; }
    Vector4 TableBorderStrong { get; }
    Vector4 TableBorderLight { get; }
    Vector4 TableRowBg { get; }
    Vector4 TableRowBgAlt { get; }
    Vector4 NavHighlight { get; }
    Vector4 NavWindowingHighlight { get; }
    Vector4 NavWindowingDimBg { get; }
    Vector4 DragDropTarget { get; }
    Vector4 ModalWindowDimBg { get; }

    //  float WindowRounding = 5.3f;
    //  float FrameRounding = 2.3f;
    //  float ScrollbarRounding = 0;
}
