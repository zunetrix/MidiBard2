using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Extensions.List;
using MidiBard.Playlist.Helpers;
using MidiBard.Resources;

namespace MidiBard;

public class ExtractionRulesWindow : Window
{
    private Plugin Plugin { get; }

    // Edit/Add form state
    private int _editingIndex = -1; // -1 = adding new rule
    private ExtractionField _editField = ExtractionField.SongName;
    private string _editLabel = string.Empty;
    private string _editPattern = string.Empty;
    private string _editOutputFormat = "$1";
    private bool _editIgnoreCase = true;
    private string _editSeparator = string.Empty;
    private string _editSanitizePattern = string.Empty;
    private string _editSanitizeReplacement = string.Empty;
    private string _editPatternError = string.Empty;
    private string _editSanitizePatternError = string.Empty;

    // Preview state
    private string _testInput = string.Empty;
    private List<(ExtractionField Field, string Value, string RuleLabel)>? _testResults;
    private string _testError = string.Empty;

    private readonly ImGuiMessageDisplay _messageDisplay = new();

    private static readonly ExtractionField[] FieldValues = Enum.GetValues<ExtractionField>();
    private static readonly string[] FieldNames = Enum.GetNames<ExtractionField>();

    public ExtractionRulesWindow(Plugin plugin) : base($"{Plugin.Name} Extraction Rules###ExtractionRulesWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(600, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(450, 320),
        };
    }

    public override void OnOpen()
    {
        _editingIndex = -1;
        _testResults = null;
        _testError = string.Empty;
        base.OnOpen();
    }

    public override void Draw()
    {
        _messageDisplay.Draw();

        DrawPreviewSection();
        ImGuiHelpers.ScaledDummy(0, 4);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 4);
        DrawToolbar();
        ImGuiHelpers.ScaledDummy(0, 2);
        DrawRulesTable();

        DrawEditRulePopup();
    }

    private void DrawPreviewSection()
    {
        ImGui.Text("Preview");

        var inputWidth = ImGui.GetContentRegionAvail().X - 70f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.InputTextWithHint(
            "##PreviewInput",
            "e.g. Beethoven - Moonlight Sonata _ classical 1801",
            ref _testInput, 300);

        ImGui.SameLine();
        if (ImGui.Button("Test##PreviewBtn", ImGuiHelpers.ScaledVector2(60, 0)))
            RunPreview();

        if (_testResults == null) return;

        ImGuiHelpers.ScaledDummy(0, 3);

        if (!string.IsNullOrEmpty(_testError))
        {
            ImGui.TextColored(Style.Colors.Red, _testError);
            return;
        }

        if (_testResults.Count == 0)
        {
            ImGui.TextDisabled("No rules matched.");
            return;
        }

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV |
                         ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("##PreviewResults", 3, tableFlags))
        {
            ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 60f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Matched Rule", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var (field, value, ruleLabel) in _testResults)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(Style.Colors.Violet, field.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(value);
                ImGui.TableNextColumn();
                ImGui.TextDisabled(ruleLabel);
            }

            ImGui.EndTable();
        }
    }

    private void DrawToolbar()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##AddRuleBtn", "Add New Rule", size: Style.Dimensions.ButtonLarge))
        {
            BeginAddRule();
            ImGui.OpenPopup("##EditRulePopup");
        }
    }

    private void DrawRulesTable()
    {
        var rules = Plugin.Config.ExtractionRules;

        if (rules.Count == 0)
        {
            ImGui.TextDisabled("No rules defined. Click + to add one.");
            return;
        }

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                         ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                         ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("##ExtractionRulesTable", 6, tableFlags))
            return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 28f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 65f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pattern", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 24f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 60f * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            ImGui.PushID(i);
            ImGui.TableNextRow();

            // # column - Selectable spans all columns, drag source/target live here
            ImGui.TableNextColumn();
            ImGui.Selectable($"{i + 1:00}##row", false,
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap);

            if (ImGui.BeginDragDropSource())
            {
                unsafe
                {
                    ImGui.SetDragDropPayload("DND_EXTRACTION_RULES", new ReadOnlySpan<byte>(&i, sizeof(int)), ImGuiCond.None);
                }
                var dragLabel = string.IsNullOrWhiteSpace(rule.Label) ? rule.Field.ToString() : rule.Label;
                ImGui.Text($"({i + 1}) {dragLabel}");
                ImGui.EndDragDropSource();
            }

            using (ImRaii.PushColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget))
            {
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("DND_EXTRACTION_RULES");
                    bool dropping;
                    unsafe { dropping = !payload.IsNull; }

                    if (dropping && payload.IsDelivery())
                    {
                        int original;
                        unsafe { original = *(int*)payload.Data; }
                        var offset = i - original;
                        if (offset != 0 && original + offset >= 0)
                        {
                            rules.MoveItemToIndex(original, original + offset);
                            Plugin.IpcProvider.SyncAllSettings();
                        }
                    }
                    ImGui.EndDragDropTarget();
                }
            }

            // Field name
            ImGui.TableNextColumn();
            ImGui.TextColored(Style.Colors.Violet, rule.Field.ToString());

            // Label
            ImGui.TableNextColumn();
            ImGui.Text(rule.Label);

            // Pattern
            ImGui.TableNextColumn();
            var patternText = string.IsNullOrWhiteSpace(rule.RegexPattern) ? "(no pattern)" : $"/{rule.RegexPattern}/";
            ImGui.Text(patternText);

            // Enabled toggle
            ImGui.TableNextColumn();
            var enabled = rule.Enabled;
            if (ImGui.Checkbox($"##EnabledRuleCheckbox", ref enabled))
            {
                rule.Enabled = enabled;
                Plugin.IpcProvider.SyncAllSettings();
            }

            // Actions
            ImGui.TableNextColumn();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditRule_{i}", "Edit"))
            {
                BeginEditRule(i, rule);
                ImGui.OpenPopup("##EditRulePopup");
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DelRule_{i}", Language.ConfirmInstructionTooltip))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    rules.SafeRemoveAt(i);
                    Plugin.IpcProvider.SyncAllSettings();
                    _testResults = null;
                    ImGui.PopID();
                    break;
                }
            }

            DrawEditRulePopup();
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawEditRulePopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(400, 0));
        using var popup = ImRaii.Popup("##EditRulePopup");
        if (!popup) return;

        var isNew = _editingIndex < 0;
        ImGui.Text(isNew ? "Add Extraction Rule" : "Edit Extraction Rule");
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 2);

        // Field combo
        ImGui.Text("Field");
        var currentIdx = Array.IndexOf(FieldValues, _editField);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##EF", ref currentIdx, FieldNames, FieldNames.Length))
            _editField = FieldValues[currentIdx];

        ImGuiHelpers.ScaledDummy(0, 4);

        // Label
        ImGui.Text("Label");
        ImGuiUtil.HelpMarker("Short description shown in the rules list. Optional.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##EL", "e.g. Artist before dash", ref _editLabel, 100);

        ImGuiHelpers.ScaledDummy(0, 4);

        // Pattern
        ImGui.Text("Regex Pattern");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##EP", @"e.g. ^(.+?)\s*-", ref _editPattern, 300);
        if (!string.IsNullOrEmpty(_editPatternError))
        {
            ImGui.TextColored(Style.Colors.Red, _editPatternError);
        }

        ImGuiHelpers.ScaledDummy(0, 4);

        // Output format
        ImGui.Text("Output Format");
        ImGuiUtil.HelpMarker("Regex replacement: $1 = first capture group, $0 = full match.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##EO", "$1", ref _editOutputFormat, 50);

        ImGuiHelpers.ScaledDummy(0, 4);

        // Ignore case
        ImGui.Checkbox("Ignore Case##EIC", ref _editIgnoreCase);

        // Separator (Tags only)
        if (_editField == ExtractionField.Tags)
        {
            ImGuiHelpers.ScaledDummy(0, 4);
            ImGui.Text("Separator");
            ImGuiUtil.HelpMarker("Split the result into multiple tag names by this string. Leave empty for a single tag.");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##ES", @"e.g. "","" or "" """, ref _editSeparator, 10);
        }

        // Sanitize
        ImGuiHelpers.ScaledDummy(0, 4);
        ImGui.Text("Sanitize Find");
        ImGuiUtil.HelpMarker("Optional regex find pattern applied to the captured value.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ESN", @"e.g. \s*-\s*v\d+$", ref _editSanitizePattern, 200);
        if (!string.IsNullOrEmpty(_editSanitizePatternError))
        {
            ImGui.TextColored(Style.Colors.Red, _editSanitizePatternError);
        }

        ImGuiHelpers.ScaledDummy(0, 4);
        ImGui.Text("Sanitize Replace By");
        ImGuiUtil.HelpMarker("Replacement text for Sanitize Find. Leave empty to remove matches.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ESR", @"e.g. "" """, ref _editSanitizeReplacement, 50);

        ImGuiHelpers.ScaledDummy(0, 6);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 4);

        var btnW = ImGuiHelpers.ScaledVector2(90, 0);
        if (ImGui.Button(isNew ? "Add##SR" : "Save##SR", btnW))
        {
            _editPatternError = string.Empty;
            _editSanitizePatternError = string.Empty;
            if (!ValidatePattern(_editPattern, out var err))
            {
                _editPatternError = err;
            }
            else if (!ValidateOptionalPattern(_editSanitizePattern, out var sanitizeErr))
            {
                _editSanitizePatternError = sanitizeErr;
            }
            else
            {
                CommitEditRule();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##CR", btnW))
            ImGui.CloseCurrentPopup();
    }

    private void BeginAddRule()
    {
        _editingIndex = -1;
        _editField = ExtractionField.SongName;
        _editLabel = string.Empty;
        _editPattern = string.Empty;
        _editOutputFormat = "$1";
        _editIgnoreCase = true;
        _editSeparator = string.Empty;
        _editSanitizePattern = string.Empty;
        _editSanitizeReplacement = string.Empty;
        _editPatternError = string.Empty;
        _editSanitizePatternError = string.Empty;
    }

    private void BeginEditRule(int index, ExtractionRule rule)
    {
        _editingIndex = index;
        _editField = rule.Field;
        _editLabel = rule.Label ?? string.Empty;
        _editPattern = rule.RegexPattern ?? string.Empty;
        _editOutputFormat = rule.OutputFormat ?? "$1";
        _editIgnoreCase = rule.IgnoreCase;
        _editSeparator = rule.Separator ?? string.Empty;
        _editSanitizePattern = rule.SanitizePattern ?? string.Empty;
        _editSanitizeReplacement = rule.SanitizeReplacement ?? string.Empty;
        _editPatternError = string.Empty;
        _editSanitizePatternError = string.Empty;
    }

    private void CommitEditRule()
    {
        var rules = Plugin.Config.ExtractionRules;
        var separator = string.IsNullOrEmpty(_editSeparator) ? null : _editSeparator;
        var sanitize = string.IsNullOrEmpty(_editSanitizePattern) ? null : _editSanitizePattern.Trim();
        var sanitizeReplacement = string.IsNullOrEmpty(_editSanitizeReplacement) ? null : _editSanitizeReplacement;

        if (_editingIndex < 0)
        {
            rules.Add(new ExtractionRule
            {
                Field = _editField,
                Enabled = true,
                Label = _editLabel.Trim(),
                RegexPattern = _editPattern.Trim(),
                OutputFormat = _editOutputFormat.Trim(),
                IgnoreCase = _editIgnoreCase,
                Separator = separator,
                SanitizePattern = sanitize,
                SanitizeReplacement = sanitizeReplacement,
            });
        }
        else
        {
            var rule = rules[_editingIndex];
            rule.Field = _editField;
            rule.Label = _editLabel.Trim();
            rule.RegexPattern = _editPattern.Trim();
            rule.OutputFormat = _editOutputFormat.Trim();
            rule.IgnoreCase = _editIgnoreCase;
            rule.Separator = separator;
            rule.SanitizePattern = sanitize;
            rule.SanitizeReplacement = sanitizeReplacement;
        }

        Plugin.IpcProvider.SyncAllSettings();
        _testResults = null;
    }

    private static bool ValidatePattern(string pattern, out string error)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "Pattern cannot be empty.";
            return false;
        }

        try
        {
            _ = new Regex(pattern);
            error = string.Empty;
            return true;
        }
        catch (RegexParseException ex)
        {
            error = $"Invalid regex: {ex.Message}";
            return false;
        }
    }

    private static bool ValidateOptionalPattern(string pattern, out string error)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = string.Empty;
            return true;
        }

        try
        {
            _ = new Regex(pattern);
            error = string.Empty;
            return true;
        }
        catch (RegexParseException ex)
        {
            error = $"Invalid sanitize regex: {ex.Message}";
            return false;
        }
    }


    private void RunPreview()
    {
        _testResults = null;
        _testError = string.Empty;

        var input = _testInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            _testError = "Enter a filename first.";
            _testResults = new();
            return;
        }

        var results = new List<(ExtractionField, string, string)>();

        try
        {
            foreach (var field in FieldValues)
            {
                var fieldRules = Plugin.Config.ExtractionRules
                    .Where(r => r.Enabled && r.Field == field && !string.IsNullOrWhiteSpace(r.RegexPattern));

                if (field == ExtractionField.Tags)
                {
                    var tagValues = new List<string>();
                    var tagLabels = new List<string>();

                    foreach (var rule in fieldRules)
                    {
                        var opts = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                        var m = Regex.Match(input, rule.RegexPattern, opts);
                        if (!m.Success) continue;

                        var raw = SongMetadataExtractor.Sanitize(m.Result(rule.OutputFormat ?? "$1"), rule);
                        var label = string.IsNullOrWhiteSpace(rule.Label) ? rule.RegexPattern : rule.Label;

                        if (!string.IsNullOrEmpty(rule.Separator))
                        {
                            tagValues.AddRange(
                                raw.Split(rule.Separator, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(p => p.Trim())
                                   .Where(p => p.Length > 0));
                        }
                        else
                        {
                            tagValues.Add(raw.Trim());
                        }

                        tagLabels.Add(label);
                    }

                    if (tagValues.Count > 0)
                        results.Add((ExtractionField.Tags,
                                     string.Join(", ", tagValues),
                                     string.Join(", ", tagLabels)));
                }
                else
                {
                    // first enabled rule that matches wins
                    foreach (var rule in fieldRules)
                    {
                        var opts = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                        var m = Regex.Match(input, rule.RegexPattern, opts);
                        if (!m.Success) continue;

                        var value = SongMetadataExtractor.Sanitize(m.Result(rule.OutputFormat ?? "$1"), rule);
                        var label = string.IsNullOrWhiteSpace(rule.Label) ? rule.RegexPattern : rule.Label;
                        results.Add((field, value, label));
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _testError = $"Error: {ex.Message}";
        }

        _testResults = results;
    }
}
