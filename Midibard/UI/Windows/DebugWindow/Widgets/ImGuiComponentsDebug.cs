using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Control;
using MidiBard.Util;
using MidiBard.Util.ImGuiExt;

namespace MidiBard;

public sealed class ImGuiComponentsDebug : Widget
{
    public override string Title => "ImGui Components";

    // Demo Enum for EnumCombo
    public enum DemoEnum
    {
        OptionAlpha,
        OptionBeta,
        OptionGamma,
        OptionDelta
    }

    // Modal data model
    public class ModalDemoData
    {
        public string Name = "Sample Item";
        public int Count = 42;
        public bool Enabled = true;
    }

    // 1. Autocomplete
    private readonly ImGuiInputAutocompleteInstrument<Instrument> _trackNameAutocomplete = new();
    private string _instrumentInput = "Violin";

    // 2. Combo Search
    private readonly ImGuiComboSearch _comboSearch = new();
    private string _selectedComboItem = "Piano";
    private readonly List<string> _comboOptions = new()
    {
        "Acoustic Guitar",
        "Bass Guitar",
        "Cello",
        "Clarinet",
        "Drums",
        "Electric Guitar",
        "Flute",
        "Harp",
        "Oboe",
        "Organ",
        "Piano",
        "Saxophone",
        "Trombone",
        "Trumpet",
        "Tuba",
        "Violin"
    };

    // 3. Message Display
    private readonly ImGuiMessageDisplay _messageDisplay = new(4000);

    // 4. Modal Editor
    private readonly ImGuiModalEditor<ModalDemoData> _modalEditor = new("##DemoModalEditor");
    private ModalDemoData _currentData = new();
    private string _lastSavedInfo = "None";

    // 5. ImguiUtil widgets state
    private DemoEnum _selectedEnum = DemoEnum.OptionAlpha;
    private bool _switchToggle = true;
    private bool _iconToggle = false;
    private int _intWithReset = 12;
    private float _spinnerRadius = 12f;
    private float _spinnerThickness = 3f;

    public ImGuiComponentsDebug(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        // Render any active message banner at the top
        _messageDisplay.Draw();

        // Render modal editor popup if open
        _modalEditor.Draw();

        DrawGroupPanelDemo();
        ImGui.Spacing();

        DrawComboSearchDemo();
        ImGui.Spacing();

        DrawAutocompleteDemo();
        ImGui.Spacing();

        DrawMessageDisplayDemo();
        ImGui.Spacing();

        DrawModalEditorDemo();
        ImGui.Spacing();

        DrawImguiUtilControlsDemo();
    }

    // --- 1. ImGuiGroupPanel ---
    private void DrawGroupPanelDemo()
    {
        if (!ImGui.CollapsingHeader("1. ImGuiGroupPanel", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(10f);
        ImGui.TextDisabled("Draws a bordered box with the title embedded into the top border line.");

        using (ImGuiGroupPanel.BeginGroupPanel("Audio Settings Panel"))
        {
            ImGui.Text("Content inside the first group panel:");
            ImGui.SameLine();
            ImGuiUtil.HelpMarker("This is an ImGuiGroupPanel container.");

            ImGui.Button("Sample Action A");
            ImGui.SameLine();
            ImGui.Button("Sample Action B");
            ImGui.TextDisabled("Group panels automatically adjust their width and draw custom border strokes.");
        }

        ImGui.Spacing();

        using (ImGuiGroupPanel.BeginGroupPanel("Fixed Size Panel", new Vector2(320, 60)))
        {
            ImGui.Text("Fixed width group panel (320px)");
        }
    }

    // --- 2. ImGuiComboSearch ---
    private void DrawComboSearchDemo()
    {
        if (!ImGui.CollapsingHeader("2. ImGuiComboSearch", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(10f);
        ImGui.TextDisabled("Combo box with an embedded search/filter box inside the popup list.");

        ImGui.SetNextItemWidth(260f);
        if (_comboSearch.Draw("Searchable Instrument Combo", _comboOptions, ref _selectedComboItem))
        {
            ImGuiUtil.AddNotification(NotificationType.Info, $"Selected: {_selectedComboItem}");
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"Selected value: {_selectedComboItem}");
    }

    // --- 3. ImGuiInputAutocompleteInstrument ---
    private void DrawAutocompleteDemo()
    {
        if (!ImGui.CollapsingHeader("3. ImGuiInputAutocompleteInstrument", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(10f);
        ImGui.TextDisabled("Text input with popup suggestion list matching instruments and showing icons.");

        ImGui.SetNextItemWidth(300f);
        _trackNameAutocomplete.Draw(
            "Instrument Autocomplete",
            ref _instrumentInput,
            InstrumentHelper.Instruments,
            x => x.InstrumentString,
            x => x.IconId);

        ImGui.SameLine();
        ImGui.TextDisabled($"Current input: '{_instrumentInput}'");
    }

    // --- 4. ImGuiMessageDisplay ---
    private void DrawMessageDisplayDemo()
    {
        if (!ImGui.CollapsingHeader("4. ImGuiMessageDisplay", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(10f);
        ImGui.TextDisabled("Displays colored notification banners at the top of the window that auto-expire.");

        if (ImGui.Button("Show Success"))
            _messageDisplay.ShowSuccess("Operation completed successfully!");

        ImGui.SameLine();
        if (ImGui.Button("Show Warning"))
            _messageDisplay.ShowWarning("Please check the track configuration.");

        ImGui.SameLine();
        if (ImGui.Button("Show Error"))
            _messageDisplay.ShowError("Failed to synchronize with ensemble client.");

        ImGui.SameLine();
        if (ImGui.Button("Show Info"))
            _messageDisplay.Show("Custom info message displayed.", Style.Colors.Cyan);

        ImGui.SameLine();
        if (ImGui.Button("Clear Message"))
            _messageDisplay.Clear();

        ImGui.TextDisabled($"Has active message: {_messageDisplay.HasMessage}");
    }

    // --- 5. ImGuiModalEditor ---
    private void DrawModalEditorDemo()
    {
        if (!ImGui.CollapsingHeader("5. ImGuiModalEditor", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(10f);
        ImGui.TextDisabled("Generic modal dialog with centered Save/Cancel buttons and a customizable layout.");

        ImGui.Text($"Current Data: Name = '{_currentData.Name}', Count = {_currentData.Count}, Enabled = {_currentData.Enabled}");
        ImGui.TextDisabled($"Last Saved at: {_lastSavedInfo}");

        if (ImGui.Button("Open Modal Editor Dialog"))
        {
            // Clone or edit data
            var editCopy = new ModalDemoData
            {
                Name = _currentData.Name,
                Count = _currentData.Count,
                Enabled = _currentData.Enabled
            };

            _modalEditor.Show(
                "Edit Item Settings",
                editCopy,
                ImGuiHelpers.ScaledVector2(380, 140),
                (editor, data) =>
                {
                    ImGui.Text("Modify item attributes below:");
                    ImGui.Spacing();

                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText("##EditName", ref data.Name, 128);

                    ImGui.InputInt("Count", ref data.Count, 1);
                    ImGui.Checkbox("Enabled", ref data.Enabled);
                },
                savedData =>
                {
                    _currentData = savedData;
                    _lastSavedInfo = $"{DateTime.Now:HH:mm:ss} - '{_currentData.Name}' ({_currentData.Count})";
                    _messageDisplay.ShowSuccess($"Saved: {_currentData.Name}");
                }
            );
        }
    }

    // --- 6. ImguiUtil Controls ---
    private void DrawImguiUtilControlsDemo()
    {
        if (!ImGui.CollapsingHeader("6. ImguiUtil Components & Helpers", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(10f);

        // EnumCombo
        ImGui.Text("EnumCombo<T>:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f);
        ImGuiUtil.EnumCombo("##DemoEnumCombo", ref _selectedEnum, showValue: true);
        ImGui.SameLine();
        ImGui.TextDisabled($"Value: {_selectedEnum}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Toggles
        ImGui.Text("Toggle Switch & Icon Toggle:");
        
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Switch Toggle:");
        ImGui.SameLine();
        if (ImGuiUtil.ToggleButton("##DemoSwitchToggle", ref _switchToggle))
        {
            ImGuiUtil.AddNotification(NotificationType.Info, $"Switch is now: {_switchToggle}");
        }
        ImGui.SameLine();
        ImGui.TextDisabled(_switchToggle ? "ON" : "OFF");

        ImGui.SameLine(220f);
        ImGui.Text("Icon Toggle:");
        ImGui.SameLine();
        ImGuiUtil.IconButtonToggle(
            "##DemoIconToggle", 
            ref _iconToggle, 
            FontAwesomeIcon.Play, 
            FontAwesomeIcon.Pause, 
            "Toggle Play/Pause");
        ImGui.SameLine();
        ImGui.TextDisabled(_iconToggle ? "Play (Active)" : "Pause (Inactive)");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Buttons variants
        ImGui.Text("Colored Buttons & Icon Buttons (from Style.Components):");

        ImGuiUtil.PrimaryButton("Primary");
        ImGui.SameLine();
        ImGuiUtil.SuccessButton("Success");
        ImGui.SameLine();
        ImGuiUtil.DangerButton("Danger");

        ImGui.SameLine();
        ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.Play, "##btnPrimaryIcon", "Primary Icon Button");
        ImGui.SameLine();
        ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Check, "##btnSuccessIcon", "Success Icon Button");
        ImGui.SameLine();
        ImGuiUtil.DangerIconButton(FontAwesomeIcon.Trash, "##btnDangerIcon", "Danger Icon Button");
        ImGui.SameLine();
        ImGuiUtil.InfoIconButton(FontAwesomeIcon.InfoCircle, "##btnInfoIcon", "Info Icon Button");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // InputIntWithReset
        ImGui.Text("InputIntWithReset (Right-click input to reset to default 12):");
        ImGui.SetNextItemWidth(150f);
        ImGuiUtil.InputIntWithReset("##InputResetDemo", ref _intWithReset, 1, () => 12);
        ImGui.SameLine();
        ImGui.TextDisabled($"Current: {_intWithReset} (Right click resets to 12)");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // TextCopyable & HelpMarker & ToolTip
        ImGui.Text("TextCopyable (Click to copy to clipboard):");
        ImGui.SameLine();
        ImGuiUtil.TextCopyable("Click here to copy this text!");

        ImGui.Text("HelpMarker:");
        ImGuiUtil.HelpMarker("This is a standard help marker with tooltip text.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // DrawFontawesomeIconOutlined
        ImGui.Text("DrawFontawesomeIconOutlined:");
        ImGui.SameLine();
        ImGuiUtil.DrawFontawesomeIconOutlined(FontAwesomeIcon.Music, Style.Colors.Black, Style.Colors.Orange);
        ImGui.SameLine();
        ImGuiUtil.DrawFontawesomeIconOutlined(FontAwesomeIcon.Heart, Style.Colors.White, Style.Colors.RedVivid);
        ImGui.SameLine();
        ImGuiUtil.DrawFontawesomeIconOutlined(FontAwesomeIcon.Star, Style.Colors.Black, Style.Colors.Yellow);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Spinner
        ImGui.Text("Animated Spinner:");
        ImGui.SameLine();
        ImGuiUtil.Spinner("##DemoSpinner", _spinnerRadius, _spinnerThickness, Style.Colors.Cyan);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        ImGui.SliderFloat("Radius##Spin", ref _spinnerRadius, 8f, 24f, "%.0f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        ImGui.SliderFloat("Thickness##Spin", ref _spinnerThickness, 1f, 6f, "%.0f");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // DrawColoredBanner
        ImGui.Text("DrawColoredBanner:");
        ImGuiUtil.DrawColoredBanner("Custom Announcement Banner", Style.Components.ButtonBlueHovered);
    }
}
