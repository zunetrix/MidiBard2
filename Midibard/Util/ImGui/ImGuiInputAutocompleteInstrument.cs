using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using MidiBard.Extensions.Dalamud;

namespace MidiBard.Util.ImGuiExt;

/// <summary>
/// Inline input text with a floating autocomplete dropdown.
/// Suggestions are filtered by substring as the user types.
/// Supports keyboard navigation (↑ ↓ Enter Esc).
/// </summary>
public class ImGuiInputAutocompleteInstrument<T>
{
    private bool _open = false;
    private int _selectedIndex = 0;
    private bool _scrollToSel = false;
    private string? _pendingSet = null;

    /// <summary>
    /// Draw the autocomplete input.
    /// Returns <c>true</c> when the user confirms with Enter while no dropdown is open.
    /// </summary>
    public bool Draw(
        string label,
        ref string input,
        IReadOnlyList<T> options,
        Func<T, string> getText,
        Func<T, uint> getIcon,
        int maxVisible = 8)
    {
        // Apply selection queued from the previous frame so InputText sees the new value
        if (_pendingSet != null)
        {
            input = _pendingSet;
            _pendingSet = null;
            _open = false;
        }

        ImGui.PushID(label);

        //  Input field
        ImGui.InputTextWithHint("##input", label, ref input, 128);
        var inputMin = ImGui.GetItemRectMin();
        var inputMax = ImGui.GetItemRectMax();
        var inputWidth = inputMax.X - inputMin.X;
        bool isActive = ImGui.IsItemActive();

        if (ImGui.IsItemActivated()) { _open = true; _selectedIndex = 0; }
        if (ImGui.IsItemEdited()) { _open = true; _selectedIndex = 0; }

        //  Build filtered list
        // Note: copy to local var — ref parameters can't be captured in lambdas
        var currentInput = input;
        var filtered = (string.IsNullOrEmpty(currentInput)
            ? options
            : options.Where(o => getText(o).Contains(currentInput, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        //  Keyboard navigation (processed while the InputText is focused)
        bool confirmed = false;
        if (isActive)
        {
            if (_open && filtered.Count > 0)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
                { _selectedIndex++; _scrollToSel = true; }
                if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
                { _selectedIndex--; _scrollToSel = true; }
                _selectedIndex = Math.Clamp(_selectedIndex, 0, filtered.Count - 1);

                if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                    _open = false;

                // Enter → pick highlighted suggestion
                if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
                {
                    if ((uint)_selectedIndex < (uint)filtered.Count)
                        _pendingSet = getText(filtered[_selectedIndex]);
                }
            }
            else
            {
                // Enter with no dropdown → confirm (let caller act on it)
                if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
                    confirmed = true;
                if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                    _open = false;
            }
        }

        //  Open popup
        if (_open && filtered.Count > 0)
            ImGui.OpenPopup("##AcDropdown");

        //  Dropdown popup
        ImGui.SetNextWindowPos(new Vector2(inputMin.X, inputMax.Y));
        var rowH = ImGui.GetFrameHeightWithSpacing();
        var rows = Math.Clamp(filtered.Count, 1, maxVisible);
        var pad = ImGui.GetStyle().WindowPadding;
        ImGui.SetNextWindowSize(new Vector2(inputWidth, rows * rowH + pad.Y * 2));

        var popupFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
                       | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings
                       | ImGuiWindowFlags.NoFocusOnAppearing;

        if (ImGui.BeginPopup("##AcDropdown", popupFlags))
        {
            if (!_open)
            {
                ImGui.CloseCurrentPopup();
            }
            else
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    ImGui.PushID(i);

                    var option = filtered[i];
                    var text = getText(option);
                    var icon = getIcon(option);

                    DalamudApi.TextureProvider.DrawIcon(
                        icon, ImGuiHelpers.ScaledVector2(ImGui.GetTextLineHeight()));
                    ImGui.SameLine();

                    bool isSel = i == _selectedIndex;
                    if (ImGui.Selectable(text, isSel))
                    {
                        _pendingSet = text;
                        ImGui.CloseCurrentPopup();
                    }

                    if (isSel)
                    {
                        ImGui.SetItemDefaultFocus();
                        if (_scrollToSel)
                        {
                            ImGui.SetScrollHereY(0.5f);
                            _scrollToSel = false;
                        }
                    }

                    ImGui.PopID();
                }
            }

            ImGui.EndPopup();
        }

        ImGui.PopID();
        return confirmed;
    }
}
