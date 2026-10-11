using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

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
        CurrentTheme = GetBuiltInTheme(themeType);
    }

    public static void SetTheme(CustomThemeData custom)
    {
        CurrentTheme = custom;
    }

    /// <summary>Applies the active theme from configuration, honouring custom themes over built-ins.</summary>
    public static void ApplyFromConfig(Configuration config)
    {
        if (!string.IsNullOrEmpty(config.SelectedCustomThemeId))
        {
            var custom = config.CustomThemes.Find(t => t.Id == config.SelectedCustomThemeId);
            if (custom != null)
            {
                SetTheme(custom);
                return;
            }
        }

        SetTheme(config.CurrentTheme);
    }

    /// <summary>Returns a fresh built-in theme instance for the given variant.</summary>
    public static IUiTheme GetBuiltInTheme(ThemeVariant themeType) => themeType switch
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

    public static ThemeStyleScope PushTheme() => new(CurrentTheme);

    public sealed class ThemeStyleScope : IDisposable
    {
        private readonly ImRaii.ColorDisposable _colors;
        private readonly ImRaii.StyleDisposable _styles;

        public ThemeStyleScope(IUiTheme theme)
        {
            _colors = ImRaii.PushColor(ImGuiCol.Text, theme.Text)
                .Push(ImGuiCol.TextDisabled, theme.TextDisabled)
                .Push(ImGuiCol.TextSelectedBg, theme.TextSelectedBg)
                .Push(ImGuiCol.WindowBg, theme.WindowBg)
                .Push(ImGuiCol.MenuBarBg, theme.MenuBarBg)
                .Push(ImGuiCol.ChildBg, theme.ChildBg)
                .Push(ImGuiCol.PopupBg, theme.PopupBg)
                .Push(ImGuiCol.ModalWindowDimBg, theme.ModalWindowDimBg)
                .Push(ImGuiCol.Border, theme.Border)
                .Push(ImGuiCol.BorderShadow, theme.BorderShadow)
                .Push(ImGuiCol.FrameBg, theme.FrameBg)
                .Push(ImGuiCol.FrameBgHovered, theme.FrameBgHovered)
                .Push(ImGuiCol.FrameBgActive, theme.FrameBgActive)
                .Push(ImGuiCol.TitleBg, theme.TitleBg)
                .Push(ImGuiCol.TitleBgActive, theme.TitleBgActive)
                .Push(ImGuiCol.TitleBgCollapsed, theme.TitleBgCollapsed)
                .Push(ImGuiCol.ScrollbarBg, theme.ScrollbarBg)
                .Push(ImGuiCol.ScrollbarGrab, theme.ScrollbarGrab)
                .Push(ImGuiCol.ScrollbarGrabHovered, theme.ScrollbarGrabHovered)
                .Push(ImGuiCol.ScrollbarGrabActive, theme.ScrollbarGrabActive)
                .Push(ImGuiCol.SliderGrab, theme.SliderGrab)
                .Push(ImGuiCol.SliderGrabActive, theme.SliderGrabActive)
                .Push(ImGuiCol.Separator, theme.Separator)
                .Push(ImGuiCol.SeparatorHovered, theme.SeparatorHovered)
                .Push(ImGuiCol.SeparatorActive, theme.SeparatorActive)
                .Push(ImGuiCol.ResizeGrip, theme.ResizeGrip)
                .Push(ImGuiCol.ResizeGripHovered, theme.ResizeGripHovered)
                .Push(ImGuiCol.ResizeGripActive, theme.ResizeGripActive)
                .Push(ImGuiCol.Tab, theme.Tab)
                .Push(ImGuiCol.TabHovered, theme.TabHovered)
                .Push(ImGuiCol.TabActive, theme.TabActive)
                .Push(ImGuiCol.TabUnfocused, theme.TabUnfocused)
                .Push(ImGuiCol.TabUnfocusedActive, theme.TabUnfocusedActive)
                .Push(ImGuiCol.DockingPreview, theme.DockingPreview)
                .Push(ImGuiCol.DockingEmptyBg, theme.DockingEmptyBg)
                .Push(ImGuiCol.PlotLines, theme.PlotLines)
                .Push(ImGuiCol.PlotLinesHovered, theme.PlotLinesHovered)
                .Push(ImGuiCol.PlotHistogram, theme.PlotHistogram)
                .Push(ImGuiCol.PlotHistogramHovered, theme.PlotHistogramHovered)
                .Push(ImGuiCol.TableHeaderBg, theme.TableHeaderBg)
                .Push(ImGuiCol.TableBorderStrong, theme.TableBorderStrong)
                .Push(ImGuiCol.TableBorderLight, theme.TableBorderLight)
                .Push(ImGuiCol.TableRowBg, theme.TableRowBg)
                .Push(ImGuiCol.TableRowBgAlt, theme.TableRowBgAlt)
                .Push(ImGuiCol.NavHighlight, theme.NavHighlight)
                .Push(ImGuiCol.NavWindowingHighlight, theme.NavWindowingHighlight)
                .Push(ImGuiCol.NavWindowingDimBg, theme.NavWindowingDimBg)
                .Push(ImGuiCol.Header, theme.Header)
                .Push(ImGuiCol.HeaderHovered, theme.HeaderHovered)
                .Push(ImGuiCol.HeaderActive, theme.HeaderActive)
                .Push(ImGuiCol.Button, theme.Button)
                .Push(ImGuiCol.ButtonHovered, theme.ButtonHovered)
                .Push(ImGuiCol.ButtonActive, theme.ButtonActive)
                .Push(ImGuiCol.DragDropTarget, theme.DragDropTarget)
                .Push(ImGuiCol.CheckMark, theme.CheckMark);

            var styles = new ImRaii.StyleDisposable();
            if (theme.WindowRounding.HasValue)
                styles = styles.Push(ImGuiStyleVar.WindowRounding, theme.WindowRounding.Value);
            if (theme.FrameRounding.HasValue)
                styles = styles.Push(ImGuiStyleVar.FrameRounding, theme.FrameRounding.Value);
            if (theme.ScrollbarRounding.HasValue)
                styles = styles.Push(ImGuiStyleVar.ScrollbarRounding, theme.ScrollbarRounding.Value);

            _styles = styles;
        }

        public void Dispose()
        {
            _styles.Dispose();
            _colors.Dispose();
        }
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

    float? WindowRounding => null;
    float? FrameRounding => null;
    float? ScrollbarRounding => null;
}
