using System.Numerics;

using ImGuiNET;

namespace MidiBard;

public static class Theme
{
    public static UITheme Current { get; private set; } = CreateDefault();

    public static void SetTheme(ThemeType type)
    {
        Current = type switch
        {
            ThemeType.Dark => CreateDark(),
            ThemeType.Light => CreateDefault(),
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
        TextPrimary = new Vector4(1f, 1f, 1f, 1f),
        TextDisabled = new Vector4(0.5f, 0.5f, 0.5f, 1f),
        Background = new Vector4(0.95f, 0.95f, 0.95f, 1f),
        WindowBackground = new Vector4(0.06f, 0.06f, 0.06f, 0.93f),
        Error = new Vector4(1f, 0.35686275f, 0.36862746f, 1f),
        FrameBackground = new Vector4(0.29f, 0.29f, 0.29f, 0.54f),

        Header = new HeaderColors
        {
            Normal = new Vector4(0.26f, 0.59f, 0.98f, 0.40f),
            Hovered = new Vector4(0.26f, 0.59f, 0.98f, 1.00f),
            Active = new Vector4(0.06f, 0.53f, 0.98f, 1.00f)
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

    public static UITheme CreateDark() => new UITheme
    {

    };
}
public enum ThemeType
{
    Light,
    Dark
}
public class UITheme
{
    public ThemeType Type;
    public HeaderColors Header = new();
    public ButtonColors Button = new();


    public Vector4 TextPrimary;
    public Vector4 TextDisabled;
    public Vector4 Background;
    public Vector4 WindowBackground;
    public Vector4 FrameBackground;
    public Vector4 Error;
}

public class HeaderColors
{
    public Vector4 Normal;
    public Vector4 Hovered;
    public Vector4 Active;
}

public class ButtonColors
{
    public Vector4 Normal;
    public Vector4 Hovered;
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
    public Vector4 Red = ImGui.ColorConvertU32ToFloat4(0xAA0000D0);
    public Vector4 Green = new Vector4(0.2f, 1f, 0.2f, 1f);
    public Vector4 Blue = new Vector4(0.2f, 0.6f, 1f, 1f);
    public Vector4 Violet = ImGui.ColorConvertU32ToFloat4(0xAAFF888E);
    // public Vector4 Violet = new Vector4(0.7f, 0.5f, 0.9f, 1f);
    // public Vector4 Orange = ImGui.ColorConvertU32ToFloat4(0xAA00B0E0);
    // public Vector4 Orange = new Vector4(1f, 0.5f, 0.9f, 1f);
    public Vector4 Orange = new Vector4(1.0f, 0.64705884f, 0.0f, 1.0f);

    // public Vector4 Orange = new Vector4(1f, 0.6f, 0.2f, 1f);
    public Vector4 Yellow = ImGui.ColorConvertU32ToFloat4(0xFF00C8C8);
    // public Vector4 Yellow = new Vector4(1f, 1f, 0.4f, 1f);
    public Vector4 Cyan = new Vector4(0.0f, 0.9804f, 1.0f, 1.0f);
    // public Vector4 Cyan = new Vector4(0.4f, 1f, 1f, 1f);
    public Vector4 Gray = new Vector4(0.5f, 0.5f, 0.5f, 1f);
    public Vector4 White = new Vector4(1f, 1f, 1f, 1f);
    public Vector4 Black = new Vector4(0f, 0f, 0f, 1f);

    public Vector4 GrassGreen = ImGui.ColorConvertU32ToFloat4(0x9C60FF8E);
    public Vector4 GrassGreen50 = ImGui.ColorConvertU32ToFloat4(0x3C60FF8E);
    public Vector4 DarkGreen = ImGui.ColorConvertU32ToFloat4(0xAC104020);

    // opacity
    public Vector4 Red50 => Red with { W = 0.3f };
    public Vector4 Blue30 => Blue with { W = 0.3f };
}
