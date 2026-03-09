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
    private bool _isGlobalMode;

    private int _editingIndex = -1;
    private string _editLabel = string.Empty;
    private string _editPattern = string.Empty;
    private bool _editIgnoreCase = true;
    private TrackGroupMode _editMode = TrackGroupMode.GroupByCapture;
    private string _editPatternError = string.Empty;

    private string _testInput = string.Empty;
    private List<(int ruleIndex, string ruleLabel)>? _testResults;
    private string _testError = string.Empty;

    private static readonly string[] ModeNames = { "Group by Capture", "One Track per Player" };

    public TrackAssignmentRulesWindow(Plugin plugin)
        : base($"{Plugin.Name} Track Assignment Rules###TrackAssignmentRulesWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(540, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(400, 280),
        };
    }

    public void OpenForMember(EnsembleMemberConfig member)
    {
        _member = member;
        _isGlobalMode = false;
        WindowName = $"{Plugin.Name}: {member.Name} — Track Rules###TrackAssignmentRulesWindow";
        ResetEditState();
        IsOpen = true;
    }

    public void OpenForGlobalRules()
    {
        _member = null;
        _isGlobalMode = true;
        WindowName = $"{Plugin.Name}: Global Capture Rules###TrackAssignmentRulesWindow";
        ResetEditState();
        IsOpen = true;
    }

    private List<TrackAssignmentRule>? ActiveRules =>
        _isGlobalMode
            ? Plugin.Config.TrackAssignment.CaptureRules
            : _member?.TrackRules;

    public override void Draw()
    {
        if (!_isGlobalMode && _member == null)
        {
            ImGui.TextDisabled("No member selected.");
            return;
        }

        DrawHeader();
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

    private void DrawHeader()
    {
        if (_isGlobalMode)
        {
            ImGui.TextColored(Style.Colors.GrassGreen, "Global Capture Rules");
            ImGui.SameLine();
            ImGuiUtil.HelpMarker("""
                Global rules run after per-member rules and use capture groups
                to dynamically group tracks by a shared key (e.g. a letter suffix).

                Each distinct captured value gets its own player slot in the order
                it first appears in the track list:
                  First captured value  → EnsembleMember[0]
                  Second captured value → EnsembleMember[1]
                  ...

                Use Group by Capture with a pattern like \s([a-z])$ to group
                all tracks ending in the same letter to the same player.
                """);
        }
        else
        {
            bool enabled = _member!.TrackAssignmentEnabled;
            if (ImGui.Checkbox($"Enable rules for {_member.Name}##TAEnabled", ref enabled))
            {
                _member.TrackAssignmentEnabled = enabled;
                Plugin.IpcProvider.SyncAllSettings();
            }
        }
    }

    private void DrawPreviewSection()
    {
        ImGui.Text("Preview");

        var inputWidth = ImGui.GetContentRegionAvail().X - 70f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.InputTextWithHint("##TAPreviewInput", "e.g. ElectricGuitarOverdriven a", ref _testInput, 200);

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

        int cols = _isGlobalMode ? 3 : 2;
        if (ImGui.BeginTable("##TAPreviewResults", cols, tableFlags))
        {
            ImGui.TableSetupColumn("Rule #", ImGuiTableColumnFlags.WidthFixed, 50f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Rule Label / Pattern", ImGuiTableColumnFlags.WidthStretch);
            if (_isGlobalMode)
                ImGui.TableSetupColumn("Captured", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var (ruleIndex, label) in _testResults)
            {
                var rule = ActiveRules![ruleIndex];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(Style.Colors.Violet, $"{ruleIndex + 1:00}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(label);
                if (_isGlobalMode)
                {
                    ImGui.TableNextColumn();
                    try
                    {
                        var opts = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                        var m = Regex.Match(_testInput.Trim(), rule.Pattern, opts);
                        var captured = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                        ImGui.TextColored(Style.Colors.Violet, captured);
                    }
                    catch { }
                }
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
        var rules = ActiveRules;
        if (rules == null) return;

        if (rules.Count == 0)
        {
            ImGui.TextDisabled("No rules defined. Click + to add one.");
            return;
        }

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                         ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                         ImGuiTableFlags.ScrollY;

        int cols = _isGlobalMode ? 6 : 5;
        if (!ImGui.BeginTable("##TATable", cols, tableFlags))
            return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 28f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pattern", ImGuiTableColumnFlags.WidthStretch);
        if (_isGlobalMode)
            ImGui.TableSetupColumn("Mode", ImGuiTableColumnFlags.WidthFixed, 80f * ImGuiHelpers.GlobalScale);
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

            if (_isGlobalMode)
            {
                ImGui.TableNextColumn();
                ImGui.TextDisabled(rule.Mode == TrackGroupMode.GroupByCapture ? "By Capture" : "One Each");
            }

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
        ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(380, 0));
        using var popup = ImRaii.Popup("##TAEditRulePopup");
        if (!popup) return;

        var isNew = _editingIndex < 0;
        ImGui.Text(isNew ? "Add Rule" : "Edit Rule");
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 2);

        ImGui.Text("Label");
        ImGui.SameLine();
        ImGuiUtil.HelpMarker("Short description shown in the rules list. Optional.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##TAEL", "e.g. Letter group", ref _editLabel, 100);

        ImGuiHelpers.ScaledDummy(0, 4);

        ImGui.Text("Regex Pattern");
        ImGui.SameLine();
        if (_isGlobalMode)
        {
            ImGuiUtil.HelpMarker("""
                Pattern matched against the track name.

                For Group by Capture, use a capture group ( ) to extract
                the grouping key. Tracks sharing the same captured value
                are assigned to the same player slot.

                Examples:
                  \s([a-z])$     → letter at end: "Guitar a" → key "a"
                  \(([A-Z])\)    → letter in parens: "Piano (B)" → key "B"
                  ^(\w+)\s       → first word: "Violin 1" → key "Violin"

                Without a capture group the full match is used as key.
                """);
        }
        else
        {
            ImGuiUtil.HelpMarker("""
                Pattern matched against the track name.
                All matching tracks are assigned to this member.

                Examples:
                  \(B\)       → tracks containing "(B)"
                  ^Violin     → tracks starting with "Violin"
                  Violin|Viola → tracks containing "Violin" or "Viola"

                Tip: ( ) and . are special — prefix with \ to match literally.
                """);
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##TAEP",
            _isGlobalMode ? @"e.g. \s([a-z])$" : @"e.g. ^Violin|^Viola",
            ref _editPattern, 300);
        if (!string.IsNullOrEmpty(_editPatternError))
            ImGui.TextColored(Style.Colors.Red, _editPatternError);

        ImGuiHelpers.ScaledDummy(0, 4);

        ImGui.Checkbox("Ignore Case##TAIC", ref _editIgnoreCase);

        if (_isGlobalMode)
        {
            ImGuiHelpers.ScaledDummy(0, 4);
            ImGui.Text("Mode");
            ImGui.SameLine();
            ImGuiUtil.HelpMarker("""
                Group by Capture: tracks sharing the same captured value
                (e.g. the same letter) are assigned to the same player slot.
                Player slots are allocated in order of first appearance.

                One Track per Player: each matched track gets its own slot.
                """);
            var modeIdx = (int)_editMode;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##TAMode", ref modeIdx, ModeNames, ModeNames.Length))
                _editMode = (TrackGroupMode)modeIdx;
        }

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

    private void ResetEditState()
    {
        _editingIndex = -1;
        _testResults = null;
        _testError = string.Empty;
    }

    private void BeginAddRule()
    {
        _editingIndex = -1;
        _editLabel = string.Empty;
        _editPattern = string.Empty;
        _editIgnoreCase = true;
        _editMode = TrackGroupMode.GroupByCapture;
        _editPatternError = string.Empty;
    }

    private void BeginEditRule(int index, TrackAssignmentRule rule)
    {
        _editingIndex = index;
        _editLabel = rule.Label ?? string.Empty;
        _editPattern = rule.Pattern ?? string.Empty;
        _editIgnoreCase = rule.IgnoreCase;
        _editMode = rule.Mode;
        _editPatternError = string.Empty;
    }

    private void CommitEditRule()
    {
        var rules = ActiveRules!;

        if (_editingIndex < 0)
        {
            rules.Add(new TrackAssignmentRule
            {
                Enabled = true,
                Label = _editLabel.Trim(),
                Pattern = _editPattern.Trim(),
                IgnoreCase = _editIgnoreCase,
                Mode = _editMode,
            });
        }
        else
        {
            var rule = rules[_editingIndex];
            rule.Label = _editLabel.Trim();
            rule.Pattern = _editPattern.Trim();
            rule.IgnoreCase = _editIgnoreCase;
            rule.Mode = _editMode;
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

        var rules = ActiveRules;
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
