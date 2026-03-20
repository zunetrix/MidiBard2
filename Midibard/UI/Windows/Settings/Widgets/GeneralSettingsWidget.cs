using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

public sealed class GeneralSettingsWidget : Widget
{
    public override string Title => "General";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Cog;

    private static readonly (string Label, string Code)[] UiLanguages =
    [
        ("English",  "en"),
        ("简体中文",  "zh-Hans"),
        ("繁體中文",  "zh-Hant"),
        ("日本語",    "ja"),
        ("Deutsch",  "de"),
    ];

    private static readonly string[] UiLangLabels = UiLanguages.Select(l => l.Label).ToArray();

    private static int GetLangIndex(string code)
    {
        for (int i = 0; i < UiLanguages.Length; i++)
            if (UiLanguages[i].Code == code) return i;
        return 0;
    }

    public GeneralSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var cfg = Context.Plugin.Config;

        if (ImGui.Checkbox(Language.setting_label_auto_open_on_startup, ref cfg.OpenOnStartup))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_label_auto_open_on_startup);

        if (ImGui.Checkbox(Language.setting_label_auto_open_when_performing, ref cfg.AutoOpenPlayerWhenPerforming))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_label_auto_open_when_performing);

        if (ImGui.Checkbox(Language.setting_label_auto_close_when_performing, ref cfg.AutoClosePlayerWhenPerforming))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_label_auto_close_when_performing);

        if (ImGui.Checkbox(Language.w32_file_dialog, ref cfg.useLegacyFileDialog))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox(Language.setting_label_save_config_after_sync, ref cfg.SaveConfigAfterSync))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.HelpMarker("Enable for accounts with individual config file");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        int uiLangIndex = GetLangIndex(cfg.UiLanguage);
        ImGui.Text(Language.setting_label_select_ui_language);
        if (ImGui.Combo("##settingUiLang", ref uiLangIndex, UiLangLabels, UiLangLabels.Length))
        {
            cfg.UiLanguage = UiLanguages[uiLangIndex].Code;
            Plugin.OnLanguageChange(cfg.UiLanguage);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button(Language.open_plugin_folder))
            WindowsApi.OpenFolder(DalamudApi.PluginInterface.ConfigDirectory.FullName);

        ImGui.SameLine();
        if (ImGui.Button(Language.open_plugin_config_file))
            WindowsApi.OpenFile(DalamudApi.PluginInterface.ConfigFile.FullName);
    }
}
