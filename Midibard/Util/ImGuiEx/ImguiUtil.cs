// Copyright (C) 2022 akira0245
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see https://github.com/akira0245/MidiBard/blob/master/LICENSE.
//
// This code is written by akira0245 and was originally used in the MidiBard project. Any usage of this code must prominently credit the author, akira0245, and indicate that it was originally used in the MidiBard project.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using ImGuiNET;

using static Dalamud.api;
using static ImGuiNET.ImGui;

namespace MidiBard;

public static class ImGuiUtil
{
    public static bool EnumCombo<TEnum>(string label, ref TEnum @enum, string[] toolTips, ImGuiComboFlags flags = ImGuiComboFlags.None, bool showValue = false) where TEnum : struct, Enum
    {
        var ret = false;
        var previewValue = showValue ? $"{@enum} ({Convert.ChangeType(@enum, @enum.GetTypeCode())})" : @enum.ToString();
        if (BeginCombo(label, previewValue, flags))
        {
            var values = Enum.GetValues<TEnum>();
            for (var i = 0; i < values.Length; i++)
                try
                {
                    PushID(i);
                    var s = showValue
                        ? $"{values[i].ToString()} ({Convert.ChangeType(values[i], values[i].GetTypeCode())})"
                        : values[i].ToString();
                    if (Selectable(s, values[i].Equals(@enum)))
                    {
                        ret = true;
                        @enum = values[i];
                    }

                    if (IsItemHovered())
                    {
                        try
                        {
                            ToolTip(toolTips[i]);
                        }
                        catch (Exception e)
                        {
                            //
                        }
                    }

                    PopID();
                }
                catch (Exception e)
                {
                    PluginLog.Error(e.ToString());
                }

            EndCombo();
        }

        return ret;
    }
    public static bool EnumCombo<TEnum>(string label, ref TEnum @enum, ImGuiComboFlags flags = ImGuiComboFlags.None, bool showValue = false) where TEnum : struct, Enum
    {
        var ret = false;
        var previewValue = showValue ? $"{@enum} ({Convert.ChangeType(@enum, @enum.GetTypeCode())})" : @enum.ToString();
        if (BeginCombo(label, previewValue, flags))
        {
            var values = Enum.GetValues<TEnum>();
            for (var i = 0; i < values.Length; i++)
                try
                {
                    PushID(i);
                    var s = showValue
                        ? $"{values[i]} ({Convert.ChangeType(values[i], values[i].GetTypeCode())})"
                        : values[i].ToString();
                    if (Selectable(s, values[i].Equals(@enum)))
                    {
                        ret = true;
                        @enum = values[i];
                    }

                    PopID();
                }
                catch (Exception e)
                {
                    PluginLog.Error(e.ToString());
                }

            EndCombo();
        }

        return ret;
    }
    public static void HelpMarker(string desc, bool sameline = true)
    {
        if (sameline) SameLine();
        //ImGui.PushFont(UiBuilder.IconFont);
        TextDisabled("(?)");
        //ImGui.PopFont();
        if (IsItemHovered())
        {
            PushFont(UiBuilder.DefaultFont);
            BeginTooltip();
            PushTextWrapPos(GetFontSize() * 35.0f);
            TextUnformatted(desc);
            PopTextWrapPos();
            EndTooltip();
            PopFont();
        }
    }

    public static Stack<Vector2> IconButtonSize = new Stack<Vector2>();

    public static void PushIconButtonSize(Vector2 size) => IconButtonSize.Push(size);
    public static void PopIconButtonSize() => IconButtonSize.TryPop(out _);

    public static Vector2 GetIconButtonSize(FontAwesomeIcon icon)
    {
        PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(icon.ToIconString());
        PopFont();
        return size;
    }

    public static bool IconButton(FontAwesomeIcon icon, string? id = null, string tooltip = null, Vector4? color = null)
    {
        PushFont(UiBuilder.IconFont);
        try
        {
            if (color != null) ImGui.PushStyleColor(ImGuiCol.Text, (Vector4)color);
            if (IconButtonSize.TryPeek(out var result))
            {
                return Button($"{icon.ToIconString()}##{id}{tooltip}", result);
            }
            else
            {
                return Button($"{icon.ToIconString()}##{id}{tooltip}");
            }
        }
        finally
        {
            PopFont();
            if (color != null) PopStyleColor();
            if (tooltip != null) ToolTip(tooltip);
        }
    }

    public static void HelpMarker(string description)
    {
        ImGui.SameLine();
        ImGuiUtil.DrawFontawesomeIconOutlined(FontAwesomeIcon.InfoCircle, Theme.Colors.Black, Theme.Current.TooltipBorderColor);
        ImGuiUtil.ToolTip(description);
    }

    public static void IconButtonWithText(FontAwesomeIcon icon, string text, Vector2 size)
    {
        ImGuiComponents.IconButtonWithText(icon, text, size);
    }

    public static void Spacing(int amount = 1)
    {
        for (int i = 0; i < amount; i++)
        {
            ImGui.Spacing();
        }
    }

    public static bool ToggleButton(string id, ref bool v)
    {
        var colors = ImGui.GetStyle().Colors;
        var p = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var height = ImGui.GetFrameHeight();
        var width = height * 1.55f;
        var radius = height * 0.50f;

        // TODO: animate

        var changed = false;
        ImGui.InvisibleButton(id, new Vector2(width, height));
        if (ImGui.IsItemClicked())
        {
            v = !v;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(p, new Vector2(p.X + width, p.Y + height), ImGui.GetColorU32(!v ? colors[(int)ImGuiCol.ButtonActive] : new Vector4(0.78f, 0.78f, 0.78f, 1.0f)), height * 0.5f);
        }
        else
        {
            drawList.AddRectFilled(p, new Vector2(p.X + width, p.Y + height), ImGui.GetColorU32(!v ? colors[(int)ImGuiCol.Button] * 0.6f : new Vector4(0.35f, 0.35f, 0.35f, 1.0f)), height * 0.50f);
        }

        drawList.AddCircleFilled(new Vector2(p.X + radius + ((v ? 1 : 0) * (width - (radius * 2.0f))), p.Y + radius), radius - 1.5f, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)));

        return changed;
    }

    public static void ToolTip(string desc, int wrap = 400, bool showBorder = true)
    {
        if (IsItemHovered())
        {
            if (showBorder)
            {
                ImGui.PushStyleColor(ImGuiCol.Border, Theme.Current.TooltipBorderColor);
                ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1);
            }
            PushFont(UiBuilder.DefaultFont);
            BeginTooltip();
            PushTextWrapPos(ImGuiHelpers.GlobalScale * wrap);
            TextUnformatted(desc);
            PopTextWrapPos();
            EndTooltip();
            PopFont();
            if (showBorder)
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
            }
        }
    }

    public static unsafe void DrawColoredBanner(uint color, string content)
    {
        DrawColoredBanner(ImGui.ColorConvertU32ToFloat4(color), content);
    }

    public static unsafe void DrawColoredBanner(Vector4 color, string content)
    {
        PushStyleColor(ImGuiCol.Button, color);
        PushStyleColor(ImGuiCol.ButtonHovered, color);
        Button(content, new Vector2(-1, GetFrameHeight()));
        PopStyleColor(2);
    }

    /// <summary>ColorPicker with palette with color picker options.</summary>
    /// <param name="id">Id for the color picker.</param>
    /// <param name="description">The description of the color picker.</param>
    /// <param name="originalColor">The current color.</param>
    /// <param name="flags">Flags to customize color picker.</param>
    /// <returns>Selected color.</returns>

    public static void ColorPickerWithPalette(int id, string description, ref Vector4 originalColor, ImGuiColorEditFlags flags)
    {
        Vector4 col = originalColor;
        List<Vector4> vector4List = ImGuiHelpers.DefaultColorPalette(36);
        if (ColorButton(string.Format("{0}###ColorPickerButton{1}", (object)description, (object)id), originalColor, flags))
            OpenPopup(string.Format("###ColorPickerPopup{0}", (object)id));
        if (BeginPopup(string.Format("###ColorPickerPopup{0}", (object)id)))
        {
            if (ColorPicker4(string.Format("###ColorPicker{0}", (object)id), ref col, flags))
            {
                originalColor = col;
            }
            for (int index1 = 0; index1 < 4; ++index1)
            {
                ImGui.Spacing();
                for (int index2 = index1 * 9; index2 < index1 * 9 + 9; ++index2)
                {
                    if (ColorButton(string.Format("###ColorPickerSwatch{0}{1}{2}", (object)id, (object)index1, (object)index2), vector4List[index2]))
                    {
                        originalColor = vector4List[index2];
                        CloseCurrentPopup();
                        EndPopup();
                        return;
                    }
                    SameLine();
                }
            }
            EndPopup();
        }
    }

    public static void ColorPicker(int id, string description, ref Vector4 originalColor, ImGuiColorEditFlags flags)
    {
        Vector4 col = originalColor;
        if (ColorButton($"{description}###ColorPickerButton{id}", originalColor, flags))
            OpenPopup($"###ColorPickerPopup{id}");
        if (BeginPopup($"###ColorPickerPopup{id}"))
        {
            if (ColorPicker4($"###ColorPicker{id}", ref col, flags))
            {
                originalColor = col;
            }
            EndPopup();
        }
    }

    public static void ColorPickerButton(int id, string description, ref Vector4 originalColor, ImGuiColorEditFlags flags)
    {
        Vector4 col = originalColor;
        if (Button($"{description}###ColorPickerButton{id}"))
            OpenPopup($"###ColorPickerPopup{id}");
        if (BeginPopup($"###ColorPickerPopup{id}"))
        {
            if (ColorPicker4($"###ColorPicker{id}", ref col, flags))
            {
                originalColor = col;
            }
            EndPopup();
        }
    }

    public static void AddNotification(NotificationType type, string content)
    {
        PluginLog.Debug($"[Notification] {type}:{content}");
        Dalamud.api.ShowNotification(content, type, 5000);
    }

    public static void PushStyleColors(bool pushNew, uint color, params ImGuiCol[] colors)
    {
        if (pushNew)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                PushStyleColor(colors[i], color);
            }
        }
        else
        {
            for (int i = 0; i < colors.Length; i++)
            {
                PushStyleColor(colors[i], GetColorU32(colors[i]));
            }
        }
    }

    public static void PushStyleColors(bool pushNew, Vector4 color, params ImGuiCol[] colors)
    {
        if (pushNew)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                PushStyleColor(colors[i], color);
            }
        }
        else
        {
            for (int i = 0; i < colors.Length; i++)
            {
                PushStyleColor(colors[i], GetColorU32(colors[i]));
            }
        }
    }

    public static bool InputIntWithReset(string label, ref int num, int step, Func<int> getDefaultValue)
    {
        var b = InputInt(label, ref num, step);
        if (IsItemClicked(ImGuiMouseButton.Right))
        {
            num = getDefaultValue();
            b = true;
        }

        return b;
    }

    public static float GetWindowContentRegionWidth() => GetWindowContentRegionMax().X - GetWindowContentRegionMin().X;

    public static float GetWindowContentRegionHeight() => GetWindowContentRegionMax().Y - GetWindowContentRegionMin().Y;

    public static Vector2 GetWindowContentRegion() => GetWindowContentRegionMax() - GetWindowContentRegionMin();

    //https://github.com/UnknownX7/DalamudRepoBrowser/blob/master/PluginUI.cs#L20
    public static bool AddHeaderIcon(string id, string icon, string tooltip = null)
    {
        if (IsWindowCollapsed()) return false;
        var nodeco = GetWindowContentRegionMin() == GetStyle().WindowPadding;
        var prevCursorPos = GetCursorPos();
        var height = GetTextLineHeightWithSpacing() * 0.95f;
        var textLineHeight = new Vector2(height);
        var buttonPos = new Vector2(GetWindowWidth() - (nodeco ? 1.05f : 2.85f) * height, (GetFrameHeight() - height) / 2);
        SetCursorPos(buttonPos);
        var drawList = GetWindowDrawList();
        drawList.PushClipRectFullScreen();

        var pressed = false;
        InvisibleButton(id, textLineHeight);
        var itemMin = GetItemRectMin();
        var itemMax = GetItemRectMax();
        var halfSize = GetItemRectSize() / 2;
        var center = itemMin + halfSize;
        if (IsWindowHovered() && IsMouseHoveringRect(itemMin, itemMax, false))
        {
            GetWindowDrawList().AddCircleFilled(center, halfSize.X, GetColorU32(IsMouseDown(ImGuiMouseButton.Left) ? ImGuiCol.ButtonActive : ImGuiCol.ButtonHovered));
            if (IsMouseReleased(ImGuiMouseButton.Left))
                pressed = true;

            if (tooltip != null)
            {
                BeginTooltip();
                TextUnformatted(tooltip);
                EndTooltip();
            }
        }

        SetCursorPos(buttonPos);
        PushFont(UiBuilder.IconFont);
        drawList.AddText(UiBuilder.IconFont, GetFontSize(), center - CalcTextSize(icon) / 2, GetColorU32(ImGuiCol.Text), icon);
        PopFont();

        PopClipRect();
        SetCursorPos(prevCursorPos);

        return pressed;
    }

    public static void TextCopyable(string text)
    {
        ImGui.TextUnformatted(text);

        if (!ImGui.IsItemHovered()) return;
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(text);
            ImGuiUtil.AddNotification(NotificationType.Info, "copied to clipboard");
        }
    }

    public static void DrawFontawesomeIconOutlined(FontAwesomeIcon icon, Vector4 outline, Vector4 iconColor)
    {
        var positionOffset = ImGuiHelpers.ScaledVector2(0.0f, 1.0f);
        var cursorStart = ImGui.GetCursorPos() + positionOffset;
        ImGui.PushFont(UiBuilder.IconFont);

        ImGui.PushStyleColor(ImGuiCol.Text, outline);
        foreach (var x in Enumerable.Range(-1, 3))
        {
            foreach (var y in Enumerable.Range(-1, 3))
            {
                if (x is 0 && y is 0) continue;

                ImGui.SetCursorPos(cursorStart + new Vector2(x, y));
                ImGui.Text(icon.ToIconString());
            }
        }

        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, iconColor);
        ImGui.SetCursorPos(cursorStart);
        ImGui.Text(icon.ToIconString());
        ImGui.PopStyleColor();

        ImGui.PopFont();

        ImGui.SetCursorPos(ImGui.GetCursorPos() - positionOffset);
    }

    //https://git.annaclemens.io/ascclemens/ChatTwo/src/commit/b63d007f15a825b669523a78945dc872e663c348/ChatTwo/Util/ImGuiUtil.cs#L215
    internal static bool BeginComboVertical(string label, string previewValue, ImGuiComboFlags flags = ImGuiComboFlags.None)
    {
        TextUnformatted(label);
        SetNextItemWidth(-1);
        return BeginCombo($"##{label}", previewValue, flags);
    }

    internal static bool DragFloatVertical(string label, ref float value, float vSpeed = 1.0f, float vMin = float.MinValue, float vMax = float.MaxValue, string? format = null, ImGuiSliderFlags flags = ImGuiSliderFlags.None)
    {
        TextUnformatted(label);
        SetNextItemWidth(-1);
        return DragFloat($"##{label}", ref value, vSpeed, vMin, vMax, format, flags);
    }

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]

    public static extern unsafe void igClearActiveID();
}
