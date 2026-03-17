using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using MidiBard.Extensions.Dalamud;

namespace MidiBard.Util.ImGuiExt;

/// <summary>
/// Combo widget with an editable filter input and a filtered suggestion list.
/// The filter input IS the value - any arbitrary text is accepted.
/// Selecting from the list sets the value to the selected item.
/// Supports keyboard navigation (↑ ↓ Enter Esc).
///
/// Usage: call <see cref="Draw"/> directly in your window/table cell.
/// No separate DrawPopup() needed.
/// </summary>
public class ImGuiInputAutocompleteInstrument<T>
{
    private int _selectedIndex = 0;
    private bool _scrollToSel = false;
    private bool _wantsOpen = false;

    /// <summary>
    /// Call before <see cref="Draw"/> to programmatically open the dropdown on the next frame.
    /// </summary>
    public void RequestOpen() => _wantsOpen = true;

    /// <summary>
    /// Renders the combo widget. Returns <c>true</c> when the user confirms
    /// (Enter key while filter is focused, or click on a list item).
    /// </summary>
    public bool Draw(
        string label,
        ref string input,
        IReadOnlyList<T> options,
        Func<T, string> getText,
        Func<T, uint> getIcon,
        int maxVisible = 8)
    {
        bool confirmed = false;

        ImGui.PushID(label);

        if (_wantsOpen)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            _wantsOpen = false;
        }

        if (ImGui.BeginCombo("##combo", input, ImGuiComboFlags.HeightLarge))
        {
            // Auto-focus the filter input when the dropdown first opens
            if (ImGui.IsWindowAppearing())
            {
                _selectedIndex = 0;
                ImGui.SetKeyboardFocusHere();
            }

            // The filter input directly edits `input` → arbitrary values allowed
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##filter", "Track name", ref input, 128);
            bool filterActive = ImGui.IsItemActive();

            // Confirm with Enter
            if (filterActive && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter)))
            {
                confirmed = true;
                ImGui.CloseCurrentPopup();
            }

            // Arrow key navigation (handled while filter input is focused)
            if (filterActive)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) { _selectedIndex++; _scrollToSel = true; }
                if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) { _selectedIndex--; _scrollToSel = true; }
            }

            // Build filtered list
            var inputCopy = input;
            var filtered = (string.IsNullOrEmpty(input)
                ? options.AsEnumerable()
                : options
                    .Where(o => getText(o).Contains(inputCopy, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(o => !getText(o).StartsWith(inputCopy, StringComparison.OrdinalIgnoreCase))
                ).ToList();

            _selectedIndex = filtered.Count > 0
                ? Math.Clamp(_selectedIndex, 0, filtered.Count - 1)
                : 0;

            ImGui.Separator();

            for (int i = 0; i < filtered.Count; i++)
            {
                ImGui.PushID(i);

                var text = getText(filtered[i]);
                var icon = getIcon(filtered[i]);

                DalamudApi.TextureProvider.DrawIcon(icon, ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight()));
                ImGui.SameLine();

                bool isSel = i == _selectedIndex;
                if (ImGui.Selectable(text, isSel))
                {
                    input = text;
                    confirmed = true;
                    ImGui.CloseCurrentPopup();
                }

                if (isSel)
                {
                    ImGui.SetItemDefaultFocus();
                    if (_scrollToSel) { ImGui.SetScrollHereY(0.5f); _scrollToSel = false; }
                }

                ImGui.PopID();
            }

            ImGui.EndCombo();
        }

        ImGui.PopID();
        return confirmed;
    }
}
