using System;
using System.Globalization;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;

namespace MidiBard;

public sealed class AppearanceSettingsWidget : Widget
{
    public override string Title => Language.setting_appearance_title;
    public override FontAwesomeIcon Icon => FontAwesomeIcon.PaintBrush;

    // Label cache
    private static CultureInfo? _labelsCulture;
    private static string[]? _builtInLabels;

    // Name edit buffer for the active custom theme
    private string _editingName = string.Empty;
    private string? _lastEditedId = null;

    public AppearanceSettingsWidget(WidgetContext ctx) : base(ctx) { }

    // Entry point
    public override void Draw()
    {
        EnsureLabelsValid();
        var cfg = Context.Plugin.Config;

        DrawAccentColors(cfg);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawThemeSelector(cfg);
    }

    // Accent / UI colours
    private void DrawAccentColors(Configuration cfg)
    {
        ImGui.Text(Language.setting_appearance_ui_color);
        ImGui.Spacing();
        ImGui.ColorEdit4("##ThemeColor", ref cfg.themeColor,
            ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetUIColor", Language.common_action_reset))
        {
            cfg.themeColor = Style.Colors.Lavender;
            Context.Plugin.IpcProvider.SyncAllSettings();
        }

        ImGui.Spacing();
        ImGui.Text(Language.setting_appearance_played_song_color);
        ImGui.Spacing();
        ImGui.ColorEdit4("##PlayedSongColor", ref cfg.playedSongColor,
            ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoLabel);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetHighlightColor", Language.common_action_reset))
        {
            cfg.playedSongColor = Style.Colors.Cyan;
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
    }

    // Theme selector
    private void DrawThemeSelector(Configuration cfg)
    {
        ImGui.Text(Language.setting_appearance_theme);
        ImGui.Spacing();

        bool usingCustom = !string.IsNullOrEmpty(cfg.SelectedCustomThemeId);
        DrawThemeCombo(cfg, usingCustom);
        ImGui.Spacing();

        if (usingCustom)
        {
            var custom = cfg.CustomThemes.Find(t => t.Id == cfg.SelectedCustomThemeId);
            if (custom != null)
                DrawCustomThemeEditor(cfg, custom);
        }
        else
        {
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float totalWidth = ImGui.GetContentRegionAvail().X;
            float btnWidth = (totalWidth - spacing * 2) / 3f;

            if (ImGui.Button("Copy to edit##CopyBuiltIn", new Vector2(btnWidth, 0)))
            {
                var copy = CustomThemeData.CreateFrom(
                    ThemeManager.GetBuiltInTheme(cfg.CurrentTheme),
                    $"{_builtInLabels![(int)cfg.CurrentTheme]} (Copy)");
                cfg.CustomThemes.Add(copy);
                cfg.SelectedCustomThemeId = copy.Id;
                _editingName = copy.Name;
                _lastEditedId = copy.Id;
                ThemeManager.SetTheme(copy);
                Context.Plugin.Config.Save();
            }

            ImGui.SameLine();
            if (ImGui.Button("Export##ExportBuiltIn", new Vector2(btnWidth, 0)))
            {
                var exportData = CustomThemeData.CreateFrom(
                    ThemeManager.GetBuiltInTheme(cfg.CurrentTheme),
                    _builtInLabels![(int)cfg.CurrentTheme]);
                ExportThemeToClipboard(exportData);
            }

            ImGui.SameLine();
            if (ImGui.Button("Import##ImportBuiltIn", new Vector2(btnWidth, 0)))
            {
                ImportThemeFromClipboard(cfg);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        bool allowMovement = cfg.AllowMovement;
        if (ImGui.Checkbox(Language.setting_appearance_window_movement, ref allowMovement))
        {
            cfg.AllowMovement = allowMovement;
            Context.Plugin.Ui.MainWindow.UpdateWindowConfig();
        }

        bool allowResize = cfg.AllowResize;
        if (ImGui.Checkbox(Language.setting_appearance_window_resize, ref allowResize))
        {
            cfg.AllowResize = allowResize;
            Context.Plugin.Ui.MainWindow.UpdateWindowConfig();
        }
    }

    // Unified combo
    private void DrawThemeCombo(Configuration cfg, bool usingCustom)
    {
        string preview = usingCustom
            ? (cfg.CustomThemes.Find(t => t.Id == cfg.SelectedCustomThemeId)?.Name ?? "Custom")
            : _builtInLabels![(int)cfg.CurrentTheme];

        ImGui.SetNextItemWidth(-1);
        using var combo = ImRaii.Combo("##ThemeCombo", preview);
        if (!combo) return;

        // Built-in section
        ImGui.TextDisabled(" Built-in ");
        for (int i = 0; i < _builtInLabels!.Length; i++)
        {
            var variant = (ThemeVariant)i;
            bool sel = !usingCustom && cfg.CurrentTheme == variant;
            if (ImGui.Selectable(_builtInLabels[i] + "##bi_" + i, sel))
            {
                cfg.SelectedCustomThemeId = null;
                cfg.CurrentTheme = variant;
                ThemeManager.SetTheme(variant);
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            if (sel) ImGui.SetItemDefaultFocus();
        }

        // Custom section
        if (cfg.CustomThemes.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled(" Custom ");
            foreach (var t in cfg.CustomThemes)
            {
                bool selC = usingCustom && cfg.SelectedCustomThemeId == t.Id;
                if (ImGui.Selectable(t.Name + "##cst_" + t.Id, selC))
                {
                    cfg.SelectedCustomThemeId = t.Id;
                    ThemeManager.SetTheme(t);
                    Context.Plugin.Config.Save();
                }
                if (selC) ImGui.SetItemDefaultFocus();
            }
        }
    }

    // Custom theme toolbar + colour editor
    private void DrawCustomThemeEditor(Configuration cfg, CustomThemeData theme)
    {
        // Sync name buffer on theme switch
        if (_lastEditedId != theme.Id)
        {
            _editingName = theme.Name;
            _lastEditedId = theme.Id;
        }

        //  Toolbar
        float btnWidth = ImGui.GetFrameHeight();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (btnWidth + ImGui.GetStyle().ItemSpacing.X) * 4);
        if (ImGui.InputText("##ThemeNameEdit", ref _editingName, 64))
            theme.Name = _editingName;

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Clone, "##DuplicateTheme", "Duplicate"))
        {
            var copy = theme.Clone($"{theme.Name} (Copy)");
            cfg.CustomThemes.Add(copy);
            cfg.SelectedCustomThemeId = copy.Id;
            _editingName = copy.Name;
            _lastEditedId = copy.Id;
            ThemeManager.SetTheme(copy);
            Context.Plugin.Config.Save();
            return;
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FileExport, "##ExportTheme", "Export to clipboard"))
        {
            ExportThemeToClipboard(theme);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FileImport, "##ImportTheme", "Import from clipboard"))
        {
            ImportThemeFromClipboard(cfg);
            return;
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.6f, 0.1f, 0.1f, 1f))
                     .Push(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.1f, 0.1f, 1f))
                     .Push(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.05f, 0.05f, 1f)))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##DeleteTheme", "Delete (Ctrl+Click)") &&
                ImGui.GetIO().KeyCtrl)
            {
                cfg.CustomThemes.Remove(theme);
                cfg.SelectedCustomThemeId = null;
                ThemeManager.SetTheme(cfg.CurrentTheme);
                Context.Plugin.Config.Save();
                return;
            }
        }

        ImGui.Spacing();

        // Colour groups
        bool changed = false;
        changed |= DrawGroupTexts(theme);
        changed |= DrawGroupWindows(theme);
        changed |= DrawGroupBorders(theme);
        changed |= DrawGroupInputs(theme);
        changed |= DrawGroupButtons(theme);
        changed |= DrawGroupTabsAndTables(theme);
        changed |= DrawGroupNavMisc(theme);
        changed |= DrawStyleGroup(theme);

        if (changed)
        {
            ThemeManager.SetTheme(theme);
            Context.Plugin.Config.Save();
        }
    }

    //  Colour row helper (value-based, properties)
    /// <summary>
    /// Draws a colour picker row. Receives get/set delegates to work around
    /// C# not allowing ref on auto-property getters.
    /// </summary>
    private static bool ColorRow(string label, int pickerId,
        Func<Vector4> get, Action<Vector4> set, Func<Vector4> getDefault)
    {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);

        ImGui.TableNextColumn();
        var cur = get();
        var next = ImGuiComponents.ColorPickerWithPalette(pickerId, "##" + label, cur);
        bool changed = next != cur;
        if (changed) set(next);

        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##rst_" + label, "Reset to default"))
        {
            set(getDefault());
            changed = true;
        }

        return changed;
    }

    //  Table wrapper
    private static bool BeginColorTable(string id) =>
        ImGui.BeginTable(id, 3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX);

    private static void SetupColorColumns()
    {
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 220f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 24f);
    }

    //  Group: Texts & Highlights
    private static bool DrawGroupTexts(CustomThemeData t)
    {
        if (!ImGui.CollapsingHeader("Texts & Highlights##grp_text")) return false;
        if (!BeginColorTable("##tbl_text")) return false;
        SetupColorColumns();
        var d = new DefaultTheme();
        bool c = false;
        c |= ColorRow("Text", 1, () => t.Text, v => t.Text = v, () => d.Text);
        c |= ColorRow("Text Disabled", 2, () => t.TextDisabled, v => t.TextDisabled = v, () => d.TextDisabled);
        c |= ColorRow("Text Selected Bg", 3, () => t.TextSelectedBg, v => t.TextSelectedBg = v, () => d.TextSelectedBg);
        ImGui.EndTable();
        return c;
    }

    //  Group: Windows & Panels
    private static bool DrawGroupWindows(CustomThemeData t)
    {
        if (!ImGui.CollapsingHeader("Windows & Panels##grp_win")) return false;
        if (!BeginColorTable("##tbl_win")) return false;
        SetupColorColumns();
        var d = new DefaultTheme();
        bool c = false;
        c |= ColorRow("Window Bg", 10, () => t.WindowBg, v => t.WindowBg = v, () => d.WindowBg);
        c |= ColorRow("Child Bg", 11, () => t.ChildBg, v => t.ChildBg = v, () => d.ChildBg);
        c |= ColorRow("Popup Bg", 12, () => t.PopupBg, v => t.PopupBg = v, () => d.PopupBg);
        c |= ColorRow("Menu Bar Bg", 13, () => t.MenuBarBg, v => t.MenuBarBg = v, () => d.MenuBarBg);
        c |= ColorRow("Modal Window Dim Bg", 14, () => t.ModalWindowDimBg, v => t.ModalWindowDimBg = v, () => d.ModalWindowDimBg);
        c |= ColorRow("Title Bg", 15, () => t.TitleBg, v => t.TitleBg = v, () => d.TitleBg);
        c |= ColorRow("Title Bg Active", 16, () => t.TitleBgActive, v => t.TitleBgActive = v, () => d.TitleBgActive);
        c |= ColorRow("Title Bg Collapsed", 17, () => t.TitleBgCollapsed, v => t.TitleBgCollapsed = v, () => d.TitleBgCollapsed);
        ImGui.EndTable();
        return c;
    }

    //  Group: Borders & Separators
    private static bool DrawGroupBorders(CustomThemeData t)
    {
        if (!ImGui.CollapsingHeader("Borders & Separators##grp_bdr")) return false;
        if (!BeginColorTable("##tbl_bdr")) return false;
        SetupColorColumns();
        var d = new DefaultTheme();
        bool c = false;
        c |= ColorRow("Border", 20, () => t.Border, v => t.Border = v, () => d.Border);
        c |= ColorRow("Border Shadow", 21, () => t.BorderShadow, v => t.BorderShadow = v, () => d.BorderShadow);
        c |= ColorRow("Separator", 22, () => t.Separator, v => t.Separator = v, () => d.Separator);
        c |= ColorRow("Separator Hovered", 23, () => t.SeparatorHovered, v => t.SeparatorHovered = v, () => d.SeparatorHovered);
        c |= ColorRow("Separator Active", 24, () => t.SeparatorActive, v => t.SeparatorActive = v, () => d.SeparatorActive);
        c |= ColorRow("Resize Grip", 25, () => t.ResizeGrip, v => t.ResizeGrip = v, () => d.ResizeGrip);
        c |= ColorRow("Resize Grip Hovered", 26, () => t.ResizeGripHovered, v => t.ResizeGripHovered = v, () => d.ResizeGripHovered);
        c |= ColorRow("Resize Grip Active", 27, () => t.ResizeGripActive, v => t.ResizeGripActive = v, () => d.ResizeGripActive);
        ImGui.EndTable();
        return c;
    }

    // Group: Inputs & Controls
    private static bool DrawGroupInputs(CustomThemeData t)
    {
        if (!ImGui.CollapsingHeader("Inputs & Controls##grp_inp")) return false;
        if (!BeginColorTable("##tbl_inp")) return false;
        SetupColorColumns();
        var d = new DefaultTheme();
        bool c = false;
        c |= ColorRow("Frame Bg", 30, () => t.FrameBg, v => t.FrameBg = v, () => d.FrameBg);
        c |= ColorRow("Frame Bg Hovered", 31, () => t.FrameBgHovered, v => t.FrameBgHovered = v, () => d.FrameBgHovered);
        c |= ColorRow("Frame Bg Active", 32, () => t.FrameBgActive, v => t.FrameBgActive = v, () => d.FrameBgActive);
        c |= ColorRow("Check Mark", 33, () => t.CheckMark, v => t.CheckMark = v, () => d.CheckMark);
        c |= ColorRow("Slider Grab", 34, () => t.SliderGrab, v => t.SliderGrab = v, () => d.SliderGrab);
        c |= ColorRow("Slider Grab Active", 35, () => t.SliderGrabActive, v => t.SliderGrabActive = v, () => d.SliderGrabActive);
        c |= ColorRow("Scrollbar Bg", 36, () => t.ScrollbarBg, v => t.ScrollbarBg = v, () => d.ScrollbarBg);
        c |= ColorRow("Scrollbar Grab", 37, () => t.ScrollbarGrab, v => t.ScrollbarGrab = v, () => d.ScrollbarGrab);
        c |= ColorRow("Scrollbar Hovered", 38, () => t.ScrollbarGrabHovered, v => t.ScrollbarGrabHovered = v, () => d.ScrollbarGrabHovered);
        c |= ColorRow("Scrollbar Active", 39, () => t.ScrollbarGrabActive, v => t.ScrollbarGrabActive = v, () => d.ScrollbarGrabActive);
        c |= ColorRow("Drag Drop Target", 40, () => t.DragDropTarget, v => t.DragDropTarget = v, () => d.DragDropTarget);
        ImGui.EndTable();
        return c;
    }

    // Group: Buttons & Headers
    private static bool DrawGroupButtons(CustomThemeData t)
    {
        if (!ImGui.CollapsingHeader("Buttons & Headers##grp_btn")) return false;
        if (!BeginColorTable("##tbl_btn")) return false;
        SetupColorColumns();
        var d = new DefaultTheme();
        bool c = false;
        c |= ColorRow("Button", 50, () => t.Button, v => t.Button = v, () => d.Button);
        c |= ColorRow("Button Hovered", 51, () => t.ButtonHovered, v => t.ButtonHovered = v, () => d.ButtonHovered);
        c |= ColorRow("Button Active", 52, () => t.ButtonActive, v => t.ButtonActive = v, () => d.ButtonActive);
        c |= ColorRow("Header", 53, () => t.Header, v => t.Header = v, () => d.Header);
        c |= ColorRow("Header Hovered", 54, () => t.HeaderHovered, v => t.HeaderHovered = v, () => d.HeaderHovered);
        c |= ColorRow("Header Active", 55, () => t.HeaderActive, v => t.HeaderActive = v, () => d.HeaderActive);
        ImGui.EndTable();
        return c;
    }

    // Group: Tabs & Tables
    private static bool DrawGroupTabsAndTables(CustomThemeData t)
    {
        if (!ImGui.CollapsingHeader("Tabs & Tables##grp_tab")) return false;
        if (!BeginColorTable("##tbl_tab")) return false;
        SetupColorColumns();
        var d = new DefaultTheme();
        bool c = false;
        c |= ColorRow("Tab", 60, () => t.Tab, v => t.Tab = v, () => d.Tab);
        c |= ColorRow("Tab Hovered", 61, () => t.TabHovered, v => t.TabHovered = v, () => d.TabHovered);
        c |= ColorRow("Tab Active", 62, () => t.TabActive, v => t.TabActive = v, () => d.TabActive);
        c |= ColorRow("Tab Unfocused", 63, () => t.TabUnfocused, v => t.TabUnfocused = v, () => d.TabUnfocused);
        c |= ColorRow("Tab Unfocused Active", 64, () => t.TabUnfocusedActive, v => t.TabUnfocusedActive = v, () => d.TabUnfocusedActive);
        c |= ColorRow("Table Header Bg", 65, () => t.TableHeaderBg, v => t.TableHeaderBg = v, () => d.TableHeaderBg);
        c |= ColorRow("Table Border Strong", 66, () => t.TableBorderStrong, v => t.TableBorderStrong = v, () => d.TableBorderStrong);
        c |= ColorRow("Table Border Light", 67, () => t.TableBorderLight, v => t.TableBorderLight = v, () => d.TableBorderLight);
        c |= ColorRow("Table Row Bg", 68, () => t.TableRowBg, v => t.TableRowBg = v, () => d.TableRowBg);
        c |= ColorRow("Table Row Bg Alt", 69, () => t.TableRowBgAlt, v => t.TableRowBgAlt = v, () => d.TableRowBgAlt);
        c |= ColorRow("Docking Preview", 70, () => t.DockingPreview, v => t.DockingPreview = v, () => d.DockingPreview);
        c |= ColorRow("Docking Empty Bg", 71, () => t.DockingEmptyBg, v => t.DockingEmptyBg = v, () => d.DockingEmptyBg);
        ImGui.EndTable();
        return c;
    }

    // Group: Navigation & Misc
    private static bool DrawGroupNavMisc(CustomThemeData t)
    {
        if (!ImGui.CollapsingHeader("Navigation & Misc##grp_nav")) return false;
        if (!BeginColorTable("##tbl_nav")) return false;
        SetupColorColumns();
        var d = new DefaultTheme();
        bool c = false;
        c |= ColorRow("Nav Highlight", 80, () => t.NavHighlight, v => t.NavHighlight = v, () => d.NavHighlight);
        c |= ColorRow("Nav Windowing Highlight", 81, () => t.NavWindowingHighlight, v => t.NavWindowingHighlight = v, () => d.NavWindowingHighlight);
        c |= ColorRow("Nav Windowing Dim Bg", 82, () => t.NavWindowingDimBg, v => t.NavWindowingDimBg = v, () => d.NavWindowingDimBg);
        c |= ColorRow("Plot Lines", 83, () => t.PlotLines, v => t.PlotLines = v, () => d.PlotLines);
        c |= ColorRow("Plot Lines Hovered", 84, () => t.PlotLinesHovered, v => t.PlotLinesHovered = v, () => d.PlotLinesHovered);
        c |= ColorRow("Plot Histogram", 85, () => t.PlotHistogram, v => t.PlotHistogram = v, () => d.PlotHistogram);
        c |= ColorRow("Plot Histogram Hovered", 86, () => t.PlotHistogramHovered, v => t.PlotHistogramHovered = v, () => d.PlotHistogramHovered);
        ImGui.EndTable();
        return c;
    }

    // Group: Rounding & Style
    private static bool DrawStyleGroup(CustomThemeData t)
    {
        if (!ImGui.CollapsingHeader("Rounding & Style##grp_style")) return false;

        bool c = false;
        c |= DrawNullableSlider("Window Rounding", ref t.WindowRounding, 0f, 12f, 5f);
        c |= DrawNullableSlider("Frame Rounding", ref t.FrameRounding, 0f, 12f, 2f);
        c |= DrawNullableSlider("Scrollbar Rounding", ref t.ScrollbarRounding, 0f, 12f, 0f);
        return c;
    }

    // NOTE: float? on a class field (not auto-property) is addressable via ref.
    private static bool DrawNullableSlider(string label, ref float? value, float min, float max, float defaultVal)
    {
        bool changed = false;
        bool enabled = value.HasValue;

        if (ImGui.Checkbox("##en_" + label, ref enabled))
        {
            value = enabled ? defaultVal : (float?)null;
            changed = true;
        }
        ImGuiUtil.ToolTip("Enable custom " + label.ToLowerInvariant());
        ImGui.SameLine();

        using var _ = ImRaii.Disabled(!enabled);
        float v = value ?? defaultVal;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat(label + "##sty", ref v, min, max))
        {
            value = v;
            changed = true;
        }
        return changed;
    }

    private static void ExportThemeToClipboard(CustomThemeData theme)
    {
        try
        {
            string encoded = theme.ExportToString();
            ImGui.SetClipboardText(encoded);
            ImGuiUtil.AddNotification(NotificationType.Success, $"Theme '{theme.Name}' exported to clipboard");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Failed to export theme to clipboard");
            ImGuiUtil.AddNotification(NotificationType.Error, "Failed to export theme");
        }
    }

    private void ImportThemeFromClipboard(Configuration cfg)
    {
        try
        {
            string text = ImGui.GetClipboardText();
            if (string.IsNullOrWhiteSpace(text))
            {
                ImGuiUtil.AddNotification(NotificationType.Error, "Clipboard is empty");
                return;
            }

            var imported = CustomThemeData.ImportFromString(text);
            cfg.CustomThemes.Add(imported);
            cfg.SelectedCustomThemeId = imported.Id;
            _editingName = imported.Name;
            _lastEditedId = imported.Id;
            ThemeManager.SetTheme(imported);
            Context.Plugin.Config.Save();
            ImGuiUtil.AddNotification(NotificationType.Success, $"Theme '{imported.Name}' imported");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Failed to import theme from clipboard");
            ImGuiUtil.AddNotification(NotificationType.Error, "Unable to import invalid theme");
        }
    }

    // Label cache init
    private static void EnsureLabelsValid()
    {
        if (_labelsCulture == Language.Culture) return;
        _labelsCulture = Language.Culture;
        _builtInLabels =
        [
            Language.theme_default, Language.theme_dark, Language.theme_modern_dark,
            Language.theme_light, Language.theme_ocean_fishing, Language.theme_deepblue,
            Language.theme_catnip, Language.theme_chocobo, Language.theme_dracula,
            Language.theme_neon, Language.theme_purple, Language.theme_wine,
            Language.theme_barbie_pink, Language.theme_cotton_candy, Language.theme_tropical,
            Language.theme_sunset, Language.theme_orange,
        ];
    }
}
