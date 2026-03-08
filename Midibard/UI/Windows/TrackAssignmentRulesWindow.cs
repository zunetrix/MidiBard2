using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Extensions.List;
using MidiBard.Resources;

namespace MidiBard;

public class TrackAssignmentRulesWindow : Window
{
    private Plugin Plugin { get; }

    private EnsembleMemberConfig? _member;

    private int _editingIndex = -1;
    private string _editLabel = string.Empty;
    private string _editPattern = string.Empty;
    private bool _editIgnoreCase = true;
    private string _editPatternError = string.Empty;

    private string _testInput = string.Empty;
    private List<(int ruleIndex, string ruleLabel)>? _testResults;
    private string _testError = string.Empty;

    public TrackAssignmentRulesWindow(Plugin plugin)
        : base($"{Plugin.Name} Track Assignment Rules###TrackAssignmentRulesWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(520, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(380, 280),
        };
    }

    public void OpenForMember(EnsembleMemberConfig member)
    {
        _member = member;
        WindowName = $"{Plugin.Name}: {member.Name} — Track Rules###TrackAssignmentRulesWindow";
        _editingIndex = -1;
        _testResults = null;
        _testError = string.Empty;

        IsOpen = true;
    }

    public override void Draw()
    {
        if (_member == null)
        {
            ImGui.TextDisabled("No member selected.");
            return;
        }

        DrawMemberHeader();
        ImGuiHelpers.ScaledDummy(0, 4);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 4);
        DrawPreviewSection();
        ImGuiHelpers.ScaledDummy(0, 4);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 4);
        DrawToolbar();
        ImGuiHelpers.ScaledDummy(0, 2);
        DrawRulesTable();
        DrawEditRulePopup();
    }

    private void DrawMemberHeader()
    {
        bool enabled = _member.TrackAssignmentEnabled;
        if (ImGui.Checkbox($"Enable rules for {_member.Name}##TAEnabled", ref enabled))
        {
            _member.TrackAssignmentEnabled = enabled;
            Plugin.IpcProvider.SyncAllSettings();
        }
    }

    private void DrawPreviewSection()
    {
        ImGui.Text("Preview");

        var inputWidth = ImGui.GetContentRegionAvail().X - 70f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.InputTextWithHint("##TAPreviewInput", "e.g. Violin 1", ref _testInput, 200);

        ImGui.SameLine();
        if (ImGui.Button("Test##TAPreviewBtn", ImGuiHelpers.ScaledVector2(60, 0)))
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
        if (ImGui.BeginTable("##TAPreviewResults", 2, tableFlags))
        {
            ImGui.TableSetupColumn("Rule #", ImGuiTableColumnFlags.WidthFixed, 50f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Rule Label / Pattern", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var (ruleIndex, label) in _testResults)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(Style.Colors.Violet, $"{ruleIndex + 1:00}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(label);
            }

            ImGui.EndTable();
        }
    }

    private void DrawToolbar()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##TAAddRuleBtn", "Add New Rule", size: Style.Dimensions.ButtonLarge))
        {
            BeginAddRule();
            ImGui.OpenPopup("##TAEditRulePopup");
        }
    }

    private void DrawRulesTable()
    {
        var rules = _member?.TrackRules;
        if (rules == null) return;

        if (rules.Count == 0)
        {
            ImGui.TextDisabled("No rules defined. Click + to add one.");
            return;
        }

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                         ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                         ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("##TATable", 5, tableFlags))
            return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 28f * ImGuiHelpers.GlobalScale);
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

            ImGui.TableNextColumn();
            ImGui.Selectable($"{i + 1:00}##TArow", false,
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap);

            if (ImGui.BeginDragDropSource())
            {
                unsafe
                {
                    ImGui.SetDragDropPayload("DND_TA_RULES", new ReadOnlySpan<byte>(&i, sizeof(int)), ImGuiCond.None);
                }
                var dragLabel = string.IsNullOrWhiteSpace(rule.Label) ? rule.Pattern : rule.Label;
                ImGui.Text($"({i + 1}) {dragLabel}");
                ImGui.EndDragDropSource();
            }

            ImGui.PushStyleColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget);
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("DND_TA_RULES");
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
            ImGui.PopStyleColor();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(rule.Label);

            ImGui.TableNextColumn();
            var patternText = string.IsNullOrWhiteSpace(rule.Pattern) ? "(no pattern)" : $"/{rule.Pattern}/";
            ImGui.TextUnformatted(patternText);

            ImGui.TableNextColumn();
            var enabled = rule.Enabled;
            if (ImGui.Checkbox("##TAEnabled", ref enabled))
            {
                rule.Enabled = enabled;
                Plugin.IpcProvider.SyncAllSettings();
            }

            ImGui.TableNextColumn();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##TAEditRule_{i}", "Edit"))
            {
                BeginEditRule(i, rule);
                ImGui.OpenPopup("##TAEditRulePopup");
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##TADelRule_{i}", Language.ConfirmInstructionTooltip))
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
        ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(360, 0));
        using var popup = ImRaii.Popup("##TAEditRulePopup");
        if (!popup) return;

        var isNew = _editingIndex < 0;
        ImGui.Text(isNew ? "Add Track Assignment Rule" : "Edit Track Assignment Rule");
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 2);

        ImGui.Text("Label");
        ImGuiUtil.HelpMarker("Short description shown in the rules list. Optional.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##TAEL", "e.g. Violin", ref _editLabel, 100);

        ImGuiHelpers.ScaledDummy(0, 4);

        ImGui.Text("Regex Pattern");
        ImGuiUtil.HelpMarker("""
            A regex pattern matched against the track name.
            All tracks that match will be assigned to this member.

            Examples:
              \(B\)       → tracks containing "(B)" e.g. "Piano (B)"
              \(1\)       → tracks containing "(1)" e.g. "Violin (1)"
              ^Violin     → tracks starting with "Violin"
              Violin|Viola → tracks containing "Violin" or "Viola"
              ^\d+\s      → tracks starting with a number e.g. "1 Flute"

            Tip: parentheses ( ) and dots . are special regex
            characters — prefix them with \ to match literally.
            """);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##TAEP", @"e.g. ^Violin|^Viola", ref _editPattern, 300);
        if (!string.IsNullOrEmpty(_editPatternError))
            ImGui.TextColored(Style.Colors.Red, _editPatternError);

        ImGuiHelpers.ScaledDummy(0, 4);

        ImGui.Checkbox("Ignore Case##TAIC", ref _editIgnoreCase);

        ImGuiHelpers.ScaledDummy(0, 6);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 4);

        var btnW = ImGuiHelpers.ScaledVector2(90, 0);
        if (ImGui.Button(isNew ? "Add##TASR" : "Save##TASR", btnW))
        {
            _editPatternError = string.Empty;
            if (!ValidatePattern(_editPattern, out var err))
            {
                _editPatternError = err;
            }
            else
            {
                CommitEditRule();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##TACR", btnW))
            ImGui.CloseCurrentPopup();
    }

    private void BeginAddRule()
    {
        _editingIndex = -1;
        _editLabel = string.Empty;
        _editPattern = string.Empty;
        _editIgnoreCase = true;
        _editPatternError = string.Empty;
    }

    private void BeginEditRule(int index, TrackAssignmentRule rule)
    {
        _editingIndex = index;
        _editLabel = rule.Label ?? string.Empty;
        _editPattern = rule.Pattern ?? string.Empty;
        _editIgnoreCase = rule.IgnoreCase;
        _editPatternError = string.Empty;
    }

    private void CommitEditRule()
    {
        var rules = _member!.TrackRules;

        if (_editingIndex < 0)
        {
            rules.Add(new TrackAssignmentRule
            {
                Enabled = true,
                Label = _editLabel.Trim(),
                Pattern = _editPattern.Trim(),
                IgnoreCase = _editIgnoreCase,
            });
        }
        else
        {
            var rule = rules[_editingIndex];
            rule.Label = _editLabel.Trim();
            rule.Pattern = _editPattern.Trim();
            rule.IgnoreCase = _editIgnoreCase;
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

    private void RunPreview()
    {
        _testResults = null;
        _testError = string.Empty;

        var input = _testInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            _testError = "Enter a track name first.";
            _testResults = new();
            return;
        }

        var rules = _member?.TrackRules;
        if (rules == null || rules.Count == 0)
        {
            _testResults = new();
            return;
        }

        var results = new List<(int, string)>();

        try
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Enabled || string.IsNullOrEmpty(rule.Pattern)) continue;

                var opts = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                if (Regex.IsMatch(input, rule.Pattern, opts))
                {
                    var label = string.IsNullOrWhiteSpace(rule.Label) ? $"/{rule.Pattern}/" : rule.Label;
                    results.Add((i, label));
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
