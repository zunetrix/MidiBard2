using System.Numerics;

using ImGuiNET;

namespace MidiBard;

public static class Theme
{
    public static ColorPalette Colors = new();
    public static UITheme Current { get; private set; } = CreateDefault();

    public static void SetTheme(ThemeType type)
    {
        Current = type switch
        {
            ThemeType.Default => CreateDefault(),
            ThemeType.Light => CreateLight(),
            ThemeType.Dark => CreateDark(),
            _ => CreateDefault()
        };
    }

    public static UITheme CreateDefault() => new UITheme
    {
        Type = ThemeType.Default,

        TooltipBorderColor = new Vector4(1.0f, 0.64705884f, 0.0f, 1.0f),

        Border = new BorderColors(),
        Title = new TitleColors(),
        Scrollbar = new ScrollbarColors(),
        Slider = new SliderColors(),
        Separator = new SeparatorColors(),
        ResizeGrip = new ResizeGripColors(),
        Tab = new TabColors(),
        Docking = new DockingColors(),
        Plot = new PlotColors(),
        Table = new TableColors(),
        Navigation = new NavColors(),

        // Vector4 CheckMark;
        Overlay = new OverlayColors
        {
            DragDropTarget = Colors.Cyan
        },

        Text = new TextColors
        {
            Normal = new Vector4(1f, 1f, 1f, 1f),                 // #FFFFFF
            Disabled = new Vector4(0.5f, 0.5f, 0.5f, 1f),         // #808080
        },

        Window = new WindowColors
        {
            Background = new Vector4(0.06f, 0.06f, 0.06f, 0.93f), // #0F0F0FEC
        },

        Frame = new FrameColors
        {
            Background = new Vector4(0.29f, 0.29f, 0.29f, 0.54f), // #4A4A4A88
        },

        Header = new HeaderColors
        {
            // Normal = new Vector4(0.26f, 0.59f, 0.98f, 0.40f),           // #4296F966
            // Hovered = new Vector4(0.26f, 0.59f, 0.98f, 1.00f),          // #4296F9
            // Active = new Vector4(0.06f, 0.53f, 0.98f, 1.00f),           // #0F87F9

            // Red (Plugin colors)
            Normal = new Vector4(0.71f, 0.71f, 0.71f, 0.4f),            // #B5B5B566
            Hovered = new Vector4(0.48416287f, 0.10077597f, 0.10077597f, 0.94509804f),  // #7B1A1AF1
            Active = new Vector4(0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f), // #5D1414F1
        },

        Button = new ButtonColors
        {
            Normal = new Vector4(0.26f, 0.59f, 0.98f, 0.40f),            // #4296F966
            Hovered = new Vector4(0.26f, 0.59f, 0.98f, 1.00f),           // #4296F9
            Active = new Vector4(0.06f, 0.53f, 0.98f, 1.00f),            // #0F87F9

            SuccessNormal = new Vector4(0.10f, 0.53f, 0.33f, 1.00f),     // #198754
            SuccessHovered = new Vector4(0.08f, 0.45f, 0.28f, 1.00f),    // #157347
            SuccessActive = new Vector4(0.06f, 0.32f, 0.20f, 1.00f),     // #0F5132

            DangerNormal = new Vector4(0.86f, 0.21f, 0.27f, 1.00f),      // #DC3545
            DangerHovered = new Vector4(0.73f, 0.18f, 0.23f, 1.00f),     // #BB2D3B
            DangerActive = new Vector4(0.65f, 0.11f, 0.16f, 1.00f),      // #A71D2A

            InfoNormal = new Vector4(0.26f, 0.59f, 0.98f, 0.40f),            // #4296F966
            InfoHovered = new Vector4(0.26f, 0.59f, 0.98f, 1.00f),           // #4296F9
            InfoActive = new Vector4(0.06f, 0.53f, 0.98f, 1.00f),            // #0F87F9

            DiscordNormal = new Vector4(0.34509805f, 0.39607847f, 0.9490197f, 1f),     // #5865F2
            DiscordActive = new Vector4(0.34509805f, 0.39607847f, 0.9490197f, 0.86666673f), // #5865F2DD
            DiscordHovered = new Vector4(0.34509805f, 0.39607847f, 0.9490197f, 0.6666667f), // #5865F2AA

            KofiNormal = new Vector4(1f, 0.35686275f, 0.36862746f, 1f),               // #FF5B5E
            KofiActive = new Vector4(1f, 0.35686275f, 0.36862746f, 0.86666673f),      // #FF5B5EDD
            KofiHovered = new Vector4(1f, 0.35686275f, 0.36862746f, 0.6666667f),      // #FF5B5EAA

            WebsiteNormal = new Vector4(0.08627451f, 0.6431373f, 0.7803922f, 1f),               // #16A4C7
            WebsiteActive = new Vector4(0.08627451f, 0.6431373f, 0.7803922f, 0.86666673f),      // #16A4C7DD
            WebsiteHovered = new Vector4(0.08627451f, 0.6431373f, 0.7803922f, 0.6666667f),      // #16A4C7AA

            PluginNormal = new Vector4(0.71f, 0.71f, 0.71f, 0.4f),                               // #B5B5B566
            PluginActive = new Vector4(0.48416287f, 0.10077597f, 0.10077597f, 0.94509804f),     // #7B1A1AF1
            PluginHovered = new Vector4(0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f),   // #5D1414F1
        }
    };

    public static UITheme CreateLight() => new UITheme
    {
        Type = ThemeType.Light,
    };

    public static UITheme CreateDark() => new UITheme
    {
        Type = ThemeType.Dark,

    };

    // ImGuiCol default colors
    // public static Vector4 Text = new Vector4(1f, 1f, 1f, 1f);                         // #FFFFFF
    // public static Vector4 TextDisabled = new Vector4(0.5f, 0.5f, 0.5f, 1f);           // #808080
    // public static Vector4 TextSelectedBg = new Vector4(0.26f, 0.59f, 0.98f, 0.35f);   // #4195F2
    // public static Vector4 WindowBg = new Vector4(0.06f, 0.06f, 0.06f, 0.93f);         // #0F0F0FEC
    // public static Vector4 MenuBarBg = new Vector4(0.14f, 0.14f, 0.14f, 1f);           // #232323
    // public static Vector4 ChildBg = new Vector4(0f, 0f, 0f, 0f);                     // #00000000
    // public static Vector4 PopupBg = new Vector4(0.08f, 0.08f, 0.08f, 0.94f);          // #141414F0
    // public static Vector4 Border = new Vector4(0.43f, 0.43f, 0.5f, 0.5f);             // #6E6E80
    // public static Vector4 BorderShadow = new Vector4(0f, 0f, 0f, 0f);                 // #00000000
    // public static Vector4 FrameBg = new Vector4(0.29f, 0.29f, 0.29f, 0.54f);          // #4A4A4A8A
    // public static Vector4 FrameBgHovered = new Vector4(0.54f, 0.54f, 0.54f, 0.4f);     // #87878766
    // public static Vector4 FrameBgActive = new Vector4(0.64f, 0.64f, 0.64f, 0.67f);     // #A3A3A8AB
    // public static Vector4 TitleBg = new Vector4(0.022624433f, 0.022624206f, 0.022624206f, 0.85067874f);  // #060606D8
    // public static Vector4 TitleBgActive = new Vector4(0.38914025f, 0.10917056f, 0.10917056f, 0.8280543f);  // #639F1BDB
    // public static Vector4 TitleBgCollapsed = new Vector4(0f, 0f, 0f, 0.51f);           // #00000082
    // public static Vector4 ScrollbarBg = new Vector4(0f, 0f, 0f, 0f);                  // #00000000
    // public static Vector4 ScrollbarGrab = new Vector4(0.31f, 0.31f, 0.31f, 1f);        // #4F4F4F
    // public static Vector4 ScrollbarGrabHovered = new Vector4(0.41f, 0.41f, 0.41f, 1f);  // #696969
    // public static Vector4 ScrollbarGrabActive = new Vector4(0.51f, 0.51f, 0.51f, 1f);  // #828282
    // public static Vector4 CheckMark = new Vector4(0.86f, 0.86f, 0.86f, 1f);            // #DBDBDB
    // public static Vector4 SliderGrab = new Vector4(0.54f, 0.54f, 0.54f, 1f);           // #8A8A8A
    // public static Vector4 SliderGrabActive = new Vector4(0.67f, 0.67f, 0.67f, 1f);     // #A8A8A8
    // public static Vector4 Button = new Vector4(0.71f, 0.71f, 0.71f, 0.4f);            // #B4B4B4
    // public static Vector4 ButtonHovered = new Vector4(0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f);  // #5D1414F2
    // public static Vector4 ButtonActive = new Vector4(0.48416287f, 0.10077597f, 0.10077597f, 0.94509804f); // #7C1A1AF2
    // public static Vector4 Header = new Vector4(0.59f, 0.59f, 0.59f, 0.31f);            // #9494944F
    // public static Vector4 HeaderHovered = new Vector4(0.5f, 0.5f, 0.5f, 0.8f);          // #808080CC
    // public static Vector4 HeaderActive = new Vector4(0.6f, 0.6f, 0.6f, 1f);            // #999999
    // public static Vector4 Separator = new Vector4(0.43f, 0.43f, 0.5f, 0.5f);           // #6E6E80
    // public static Vector4 SeparatorHovered = new Vector4(0.3647059f, 0.078431375f, 0.078431375f, 0.78280544f);  // #5D1414C7
    // public static Vector4 SeparatorActive = new Vector4(0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f);  // #5D1414F2
    // public static Vector4 ResizeGrip = new Vector4(0.79f, 0.79f, 0.79f, 0.25f);         // #CACACA40
    // public static Vector4 ResizeGripHovered = new Vector4(0.78f, 0.78f, 0.78f, 0.67f);   // #C8C8C8AB
    // public static Vector4 ResizeGripActive = new Vector4(0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f);  // #5D1414F2
    // public static Vector4 Tab = new Vector4(0.23f, 0.23f, 0.23f, 0.86f);               // #3A3A3AF7
    // public static Vector4 TabHovered = new Vector4(0.58371043f, 0.30374074f, 0.30374074f, 0.7647059f);  // #94504C
    // public static Vector4 TabActive = new Vector4(0.47963798f, 0.15843244f, 0.15843244f, 0.7647059f);  // #7A2828
    // public static Vector4 TabUnfocused = new Vector4(0.068f, 0.10199998f, 0.14800003f, 0.9724f);       // #112648
    // public static Vector4 TabUnfocusedActive = new Vector4(0.13599998f, 0.26199996f, 0.424f, 1f);         // #227A6B
    // public static Vector4 DockingPreview = new Vector4(0.26f, 0.59f, 0.98f, 0.7f);                      // #4195F2B3
    // public static Vector4 DockingEmptyBg = new Vector4(0.2f, 0.2f, 0.2f, 1f);                          // #333333
    // public static Vector4 PlotLines = new Vector4(0.61f, 0.61f, 0.61f, 1f);                            // #9B9B9B
    // public static Vector4 PlotLinesHovered = new Vector4(1f, 0.43f, 0.35f, 1f);                        // #FF6F59
    // public static Vector4 PlotHistogram = new Vector4(0.9f, 0.7f, 0f, 1f);                             // #E5B200
    // public static Vector4 PlotHistogramHovered = new Vector4(1f, 0.6f, 0f, 1f);                         // #FF9900
    // public static Vector4 TableHeaderBg = new Vector4(0.19f, 0.19f, 0.2f, 1f);                         // #303030
    // public static Vector4 TableBorderStrong = new Vector4(0.31f, 0.31f, 0.35f, 1f);                     // #4F4F59
    // public static Vector4 TableBorderLight = new Vector4(0.23f, 0.23f, 0.25f, 1f);                      // #3A3A40
    // public static Vector4 TableRowBg = new Vector4(0f, 0f, 0f, 0f);                                    // #00000000
    // public static Vector4 TableRowBgAlt = new Vector4(1f, 1f, 1f, 0.06f);                              // #FFFFFF0F
    // public static Vector4 NavHighlight = new Vector4(0.26f, 0.59f, 0.98f, 1f);                         // #4195F2
    // public static Vector4 NavWindowingHighlight = new Vector4(1f, 1f, 1f, 0.7f);                        // #FFFFFFB3
    // public static Vector4 NavWindowingDimBg = new Vector4(0.8f, 0.8f, 0.8f, 0.2f);                      // #CCCCCC33
    // public static Vector4 DragDropTarget = new Vector4(1f, 1f, 0f, 0.9f);                              // #FFFF00E6
    // public static Vector4 ModalWindowDimBg = new Vector4(0.8f, 0.8f, 0.8f, 0.35f);                      // #CCCCCC59
}

public static class ColorUtil
{
    public static Vector4 HexToVector4(string hex, float alpha = 1.0f)
    {
        if (hex.StartsWith("#"))
            hex = hex.Substring(1);

        if (hex.Length != 6)
            return new Vector4();

        var r = int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
        var g = int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
        var b = int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f;

        return new Vector4(r, g, b, alpha);
    }
}

public enum ThemeType
{
    Default,
    Light,
    Dark
}

public class UITheme
{
    public ThemeType Type;

    public TextColors Text = new();
    public WindowColors Window = new();
    public BorderColors Border = new();
    public FrameColors Frame = new();
    public TitleColors Title = new();
    public ScrollbarColors Scrollbar = new();
    public SliderColors Slider = new();
    public SeparatorColors Separator = new();
    public ResizeGripColors ResizeGrip = new();
    public TabColors Tab = new();
    public DockingColors Docking = new();
    public PlotColors Plot = new();
    public TableColors Table = new();
    public NavColors Navigation = new();
    public OverlayColors Overlay = new();
    public HeaderColors Header = new();
    public ButtonColors Button = new();

    public Vector4 CheckMark;
    public Vector4 TooltipBorderColor;

    // public float WindowRounding;
    // public float FrameRounding;
    // public float ScrollbarRounding;
}

public class TextColors
{
    // ImGuiCol.Text
    public Vector4 Normal;
    // ImGuiCol.TextDisabled
    public Vector4 Disabled;
    // ImGuiCol.TextSelectedBg
    public Vector4 SelectedBg;
}

public class HeaderColors
{
    // ImGuiCol.Header
    public Vector4 Normal;
    // ImGuiCol.HeaderHovered
    public Vector4 Hovered;
    // ImGuiCol.HeaderActive
    public Vector4 Active;
}

public class WindowColors
{
    // ImGuiCol.WindowBg
    public Vector4 Background;
    // ImGuiCol.MenuBarBg
    public Vector4 MenuBar;
    // ImGuiCol.ChildBg
    public Vector4 Child;
    // ImGuiCol.PopupBg
    public Vector4 Popup;
}

public class BorderColors
{
    // ImGuiCol.Border
    public Vector4 Normal;
    // ImGuiCol.BorderShadow
    public Vector4 Shadow;
}

public class FrameColors
{
    // ImGuiCol.FrameBg
    public Vector4 Background;
    // ImGuiCol.FrameBgHovered
    public Vector4 Hovered;
    // ImGuiCol.FrameBgActive
    public Vector4 Active;
}

public class TitleColors
{
    // ImGuiCol.TitleBg
    public Vector4 Normal;
    // ImGuiCol.TitleBgActive
    public Vector4 Active;
    // ImGuiCol.TitleBgCollapsed
    public Vector4 Collapsed;
}

public class ScrollbarColors
{
    // ImGuiCol.ScrollbarBg
    public Vector4 Background;
    // ImGuiCol.ScrollbarGrab
    public Vector4 Grab;
    // ImGuiCol.ScrollbarGrabHovered
    public Vector4 GrabHovered;
    // ImGuiCol.ScrollbarGrabActive
    public Vector4 GrabActive;
}

public class SliderColors
{
    // ImGuiCol.SliderGrab
    public Vector4 Grab;
    // ImGuiCol.SliderGrabActive
    public Vector4 GrabActive;
}

public class SeparatorColors
{
    // ImGuiCol.Separator
    public Vector4 Normal;
    // ImGuiCol.SeparatorHovered
    public Vector4 Hovered;
    // ImGuiCol.SeparatorActive
    public Vector4 Active;
}

public class ResizeGripColors
{
    // ImGuiCol.ResizeGrip
    public Vector4 Normal;
    // ImGuiCol.ResizeGripHovered
    public Vector4 Hovered;
    // ImGuiCol.ResizeGripActive
    public Vector4 Active;
}

public class TabColors
{
    // ImGuiCol.Tab
    public Vector4 Normal;
    // ImGuiCol.TabHovered
    public Vector4 Hovered;
    // ImGuiCol.TabActive
    public Vector4 Active;
    // ImGuiCol.TabUnfocused
    public Vector4 Unfocused;
    // ImGuiCol.TabUnfocusedActive
    public Vector4 UnfocusedActive;
}

public class DockingColors
{
    // ImGuiCol.DockingPreview
    public Vector4 Preview;
    // ImGuiCol.DockingEmptyBg
    public Vector4 EmptyBg;
}

public class PlotColors
{
    // ImGuiCol.PlotLines
    public Vector4 Lines;
    // ImGuiCol.PlotLinesHovered
    public Vector4 LinesHovered;
    // ImGuiCol.PlotHistogram
    public Vector4 Histogram;
    // ImGuiCol.PlotHistogramHovered
    public Vector4 HistogramHovered;
}

public class TableColors
{
    // ImGuiCol.TableHeaderBg
    public Vector4 HeaderBg;
    // ImGuiCol.TableBorderStrong
    public Vector4 BorderStrong;
    // ImGuiCol.TableBorderLight
    public Vector4 BorderLight;
    // ImGuiCol.TableRowBg
    public Vector4 RowBg;
    // ImGuiCol.TableRowBgAlt
    public Vector4 RowBgAlt;
}

public class NavColors
{
    // ImGuiCol.NavHighlight
    public Vector4 Highlight;
    // ImGuiCol.NavWindowingHighlight
    public Vector4 WindowingHighlight;
    // ImGuiCol.NavWindowingDimBg
    public Vector4 WindowingDimBg;
}

public class OverlayColors
{
    // ImGuiCol.DragDropTarget
    public Vector4 DragDropTarget;
    // ImGuiCol.ModalWindowDimBg
    public Vector4 ModalWindowDimBg;
}

public class ButtonColors
{
    // ImGuiCol.Button
    public Vector4 Normal;
    // ImGuiCol.ButtonHovered
    public Vector4 Hovered;
    // ImGuiCol.ButtonActive
    public Vector4 Active;
    public Vector4 SuccessNormal;
    public Vector4 SuccessActive;
    public Vector4 SuccessHovered;
    public Vector4 DangerNormal;
    public Vector4 DangerActive;
    public Vector4 DangerHovered;
    public Vector4 InfoNormal;
    public Vector4 InfoActive;
    public Vector4 InfoHovered;
    public Vector4 PluginNormal;
    public Vector4 PluginActive;
    public Vector4 PluginHovered;
    public Vector4 DiscordNormal;
    public Vector4 DiscordActive;
    public Vector4 DiscordHovered;
    public Vector4 KofiNormal;
    public Vector4 KofiHovered;
    public Vector4 KofiActive;
    public Vector4 WebsiteNormal;
    public Vector4 WebsiteHovered;
    public Vector4 WebsiteActive;
}

public class ColorPalette
{
    // public Vector4 Red = new Vector4(1f, 0.2f, 0.2f, 1f);
    // public Vector4 Red = new Vector4(0.784f, 0f, 0f, 1f);
    // public Vector4 Red = new Vector4(1f, 0.35686275f, 0.36862746f, 1f);
    public Vector4 Red = new Vector4(0.81568635f, 0f, 0f, 0.6666667f);
    public Vector4 Green = new Vector4(0.2f, 1f, 0.2f, 1f);
    public Vector4 Blue = new Vector4(0.2f, 0.6f, 1f, 1f);
    public Vector4 Violet = new Vector4(0.5568628f, 0.53333336f, 1f, 0.6666667f);
    // public Vector4 Violet = new Vector4(0.7f, 0.5f, 0.9f, 1f);
    // public Vector4 Orange = new Vector4(1f, 0.5f, 0.9f, 1f);

    public Vector4 Orange = new Vector4(1f, 0.6f, 0.2f, 1f);
    public Vector4 Yellow = new Vector4(0.7843138f, 0.7843138f, 0f, 1f);
    public Vector4 Lavender = new Vector4(0.65882355f, 0.65882355f, 1f, 1f);
    // public Vector4 Yellow = new Vector4(1f, 1f, 0.4f, 1f);
    public Vector4 Cyan = new Vector4(0.0f, 0.9804f, 1.0f, 1.0f);
    // public Vector4 Cyan = new Vector4(0.4f, 1f, 1f, 1f);
    public Vector4 Gray = new Vector4(0.5f, 0.5f, 0.5f, 1f);
    public Vector4 White = new Vector4(1f, 1f, 1f, 1f);
    public Vector4 Black = new Vector4(0f, 0f, 0f, 1f);

    public Vector4 GrassGreen = new Vector4(0.5568628f, 1f, 0.37647063f, 0.6117647f);
    public Vector4 GrassGreen50 = new Vector4(0.5568628f, 1f, 0.37647063f, 0.23529413f);
    public Vector4 DarkGreen = new Vector4(0.1254902f, 0.2509804f, 0.0627451f, 0.6745098f);

    // opacity
    public Vector4 Red50 => Red with { W = 0.3f };
    public Vector4 Blue30 => Blue with { W = 0.3f };
}


