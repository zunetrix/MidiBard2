using System.Globalization;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

using MidiBard.Resources;

namespace MidiBard;

public sealed class AppearanceSettingsWidget : Widget
{
    public override string Title => "Appearance";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.PaintBrush;

    private static CultureInfo? _labelsCulture;
    private static string[]? _themeLabels;

    private static void EnsureLabelsValid()
    {
        if (_labelsCulture == Language.Culture) return;
        _labelsCulture = Language.Culture;
        _themeLabels =
        [
            Language.theme_default, Language.theme_dark, Language.theme_modern_dark,
            Language.theme_light, Language.theme_ocean_fishing, Language.theme_deepblue,
            Language.theme_catnip, Language.theme_chocobo, Language.theme_dracula,
            Language.theme_neon, Language.theme_purple, Language.theme_wine,
            Language.theme_barbie_pink, Language.theme_cotton_candy, Language.theme_tropical,
            Language.theme_sunset, Language.theme_orange,
        ];
    }

    public AppearanceSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        EnsureLabelsValid();
        var cfg = Context.Plugin.Config;

        ImGui.Text(Language.setting_label_theme_color);
        ImGui.Spacing();
        ImGui.ColorEdit4("##sw2ThemeColor", ref cfg.themeColor, ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##sw2BtnResetUIColor", "Reset"))
        {
            cfg.themeColor = Style.Colors.Lavender;
            Context.Plugin.IpcProvider.SyncAllSettings();
        }

        ImGui.Spacing();
        ImGui.Text(Language.setting_label_played_song_highlight_color);
        ImGui.Spacing();
        ImGui.ColorEdit4("##sw2PlayedSongColor", ref cfg.playedSongColor, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoLabel);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##sw2BtnResetHighlightColor", "Reset"))
        {
            cfg.playedSongColor = Style.Colors.Cyan;
            Context.Plugin.IpcProvider.SyncAllSettings();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text(Language.setting_label_theme);
        if (ImGuiUtil.EnumCombo("##sw2ThemeVariant", ref cfg.CurrentTheme, labelsOverride: _themeLabels))
        {
            ThemeManager.SetTheme(cfg.CurrentTheme);
            Context.Plugin.IpcProvider.SyncAllSettings();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        bool allowMovement = cfg.AllowMovement;
        if (ImGui.Checkbox("Allow window movement", ref allowMovement))
        {
            cfg.AllowMovement = allowMovement;
            Context.Plugin.Ui.MainWindow.UpdateWindowConfig();
        }
        bool allowResize = cfg.AllowResize;
        if (ImGui.Checkbox("Allow window resize", ref allowResize))
        {
            cfg.AllowResize = allowResize;
            Context.Plugin.Ui.MainWindow.UpdateWindowConfig();
        }
    }
}
