using System.Numerics;

namespace MidiBard;

public static class Theme
{
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

    public static void ToggleTheme()
    {
        Current = Current.Type == ThemeType.Dark ? CreateDefault() : CreateDark();
    }

    public static ColorPalette Colors = new();

    public static UITheme CreateDefault() => new UITheme
    {
        Type = ThemeType.Default,
        TextPrimary = new Vector4(1f, 1f, 1f, 1f),
        TextDisabled = new Vector4(0.5f, 0.5f, 0.5f, 1f),
        WindowBackground = new Vector4(0.06f, 0.06f, 0.06f, 0.93f),

        FrameBackground = new Vector4(0.29f, 0.29f, 0.29f, 0.54f),
        // FrameBackgroundHovered,
        // FrameBackgroundActive,

        // Vector4 TitleBackground,
        // Vector4 TitleBackgroundActive,
        // Vector4 CheckMark,

        WindowRounding = 5.3f,
        FrameRounding = 2.3f,
        ScrollbarRounding = 0f,

        TooltipBorderColor = new Vector4(1.0f, 0.64705884f, 0.0f, 1.0f),

        Header = new HeaderColors
        {
            // blue
            // Normal = new Vector4(0.26f, 0.59f, 0.98f, 0.40f),
            // Hovered = new Vector4(0.26f, 0.59f, 0.98f, 1.00f),
            // Active = new Vector4(0.06f, 0.53f, 0.98f, 1.00f),

            //red
            Normal = new Vector4(0.71f, 0.71f, 0.71f, 0.4f),
            Hovered = new Vector4(0.48416287f, 0.10077597f, 0.10077597f, 0.94509804f),
            Active = new Vector4(0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f)
        },

        Button = new ButtonColors
        {
            Normal = new Vector4(0.26f, 0.59f, 0.98f, 0.40f),
            Hovered = new Vector4(0.26f, 0.59f, 0.98f, 1.00f),
            Active = new Vector4(0.06f, 0.53f, 0.98f, 1.00f),

            DiscordNormal = new Vector4(0.34509805f, 0.39607847f, 0.9490197f, 1f),
            DiscordActive = new Vector4(0.34509805f, 0.39607847f, 0.9490197f, 0.86666673f),
            DiscordHovered = new Vector4(0.34509805f, 0.39607847f, 0.9490197f, 0.6666667f),

            KofiNormal = new Vector4(1f, 0.35686275f, 0.36862746f, 1f),
            KofiActive = new Vector4(1f, 0.35686275f, 0.36862746f, 0.86666673f),
            KofiHovered = new Vector4(1f, 0.35686275f, 0.36862746f, 0.6666667f),

            WebsiteNormal = new Vector4(0.08627451f, 0.6431373f, 0.7803922f, 1f),
            WebsiteActive = new Vector4(0.08627451f, 0.6431373f, 0.7803922f, 0.86666673f),
            WebsiteHovered = new Vector4(0.08627451f, 0.6431373f, 0.7803922f, 0.6666667f),

            PluginNormal = new Vector4(0.71f, 0.71f, 0.71f, 0.4f),
            PluginActive = new Vector4(0.48416287f, 0.10077597f, 0.10077597f, 0.94509804f),
            PluginHovered = new Vector4(0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f),

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
    public HeaderColors Header = new();
    public ButtonColors Button = new();

    // ImGuiCol.Text
    public Vector4 TextPrimary;
    public Vector4 TextDisabled;
    // ImGuiCol.WindowBg
    public Vector4 WindowBackground;
    // ImGuiCol.FrameBg
    public Vector4 FrameBackground;
    // ImGuiCol.FrameBgHovered
    public Vector4 FrameBackgroundHovered;
    // ImGuiCol.FrameBgActive
    public Vector4 FrameBackgroundActive;
    // ImGuiCol.TitleBg
    public Vector4 TitleBackground;
    // ImGuiCol.TitleBgActive
    public Vector4 TitleBackgroundActive;
    // ImGuiCol.CheckMark
    public Vector4 CheckMark;

    public Vector4 TooltipBorderColor;

    public float WindowRounding;
    public float FrameRounding;
    public float ScrollbarRounding;
}


public class TabColors
{
    // ImGuiCol.Tab
    public Vector4 Normal;
    // ImGuiCol.TabHovered
    public Vector4 Hovered;
    // ImGuiCol.TabActive
    public Vector4 Active;
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

public class ButtonColors
{
    // ImGuiCol.Button
    public Vector4 Normal;
    // ImGuiCol.ButtonHovered
    public Vector4 Hovered;
    // ImGuiCol.ButtonActive
    public Vector4 Active;
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
    // public uint Red = 0xFF0000C8;
    // public Vector4 Red = new Vector4(0.784f, 0f, 0f, 1f);
    // public Vector4 Red = new Vector4(1f, 0.35686275f, 0.36862746f, 1f);
    public Vector4 Red = new Vector4(0.81568635f, 0f, 0f, 0.6666667f);
    public Vector4 Green = new Vector4(0.2f, 1f, 0.2f, 1f);
    public Vector4 Blue = new Vector4(0.2f, 0.6f, 1f, 1f);
    public Vector4 Violet = new Vector4(0.5568628f, 0.53333336f, 1f, 0.6666667f);
    // public Vector4 Violet = new Vector4(0.7f, 0.5f, 0.9f, 1f);
    // public Vector4 Orange = ImGui.ColorConvertU32ToFloat4(0xAA00B0E0);
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
