using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public static class ImGuiUtil
{
    public static bool EnumCombo<TEnum>(
    string label,
    ref TEnum @enum,
    ImGuiComboFlags flags = ImGuiComboFlags.None,
    bool showValue = false,
    string[]? toolTips = null,
    string[]? labelsOverride = null
    // Func<TEnum, object>? orderBy = null,
) where TEnum : struct, Enum
    {
        var ret = false;
        var enumValues = Enum.GetValues<TEnum>();
        var enumIndex = Array.IndexOf(enumValues, @enum);

        // preview text
        string selectedValue = labelsOverride != null && enumIndex >= 0 && enumIndex < labelsOverride.Length
            ? labelsOverride[enumIndex]
            : (showValue
                ? $"{@enum} ({Convert.ChangeType(@enum, @enum.GetTypeCode())})"
                : @enum.ToString());

        if (ImGui.BeginCombo(label, selectedValue, flags))
        {
            var values = enumValues;

            // if (orderBy != null)
            //     values = values.OrderBy(orderBy).ToArray();

            for (var i = 0; i < values.Length; i++)
            {
                try
                {
                    ImGui.PushID(i);

                    // Label
                    string itemLabel = labelsOverride != null && i < labelsOverride.Length
                        ? labelsOverride[i]
                        : (showValue
                            ? $"{values[i]} ({Convert.ChangeType(values[i], values[i].GetTypeCode())})"
                            : values[i].ToString());

                    if (ImGui.Selectable(itemLabel, values[i].Equals(@enum)))
                    {
                        ret = true;
                        @enum = values[i];
                    }

                    // Tooltip
                    if (toolTips != null && i < toolTips.Length && toolTips[i] != null && ImGui.IsItemHovered())
                    {
                        ToolTip(toolTips[i]);
                    }

                    ImGui.PopID();
                }
                catch (Exception e)
                {
                    DalamudApi.PluginLog.Error(e.ToString());
                }
            }

            ImGui.EndCombo();
        }

        return ret;
    }

    public static Vector2 GetIconButtonSize(FontAwesomeIcon icon)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(icon.ToIconString());
        ImGui.PopFont();
        return size;
    }

    public static bool IconButton(FontAwesomeIcon icon, string? id = null, string tooltip = null, Vector4? color = null, Vector2? size = null)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            try
            {
                var iconButtonSize = ImGui.CalcTextSize(icon.ToIconString()) + ImGui.GetStyle().FramePadding * 2;
                if (color != null) ImGui.PushStyleColor(ImGuiCol.Text, (Vector4)color);
                var buttonSize = size != null ? size.Value : iconButtonSize;
                return ImGui.Button($"{icon.ToIconString()}##{id}", buttonSize);
            }
            finally
            {
                if (color != null) ImGui.PopStyleColor();
                if (tooltip != null) ToolTip(tooltip);
            }
        }
    }
    public static bool IconButtonToggle(string id, ref bool btnValue, FontAwesomeIcon iconOn, FontAwesomeIcon iconOff, string? tooltip = null)
    {
        var showHideIcon = btnValue ? iconOn : iconOff;
        ImGui.PushStyleColor(ImGuiCol.Button, btnValue ? Style.Components.ButtonSuccessNormal : Style.Components.ButtonDangerNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, btnValue ? Style.Components.ButtonSuccessHovered : Style.Components.ButtonDangerHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, btnValue ? Style.Components.ButtonSuccessActive : Style.Components.ButtonDangerActive);

        var changed = false;
        if (ImGuiUtil.IconButton(showHideIcon, id, tooltip))
        {
            btnValue = !btnValue;
            changed = true;
        }
        ImGui.PopStyleColor(3);

        return changed;
    }

    public static bool ToggleButton(string id, ref bool v)
    {
        var colors = ImGui.GetStyle().Colors;
        var p = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var height = ImGui.GetFrameHeight();
        var width = height * 1.55f;
        var radius = height * 0.50f;

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

    public static void ToolTip(string desc, int wrap = 400, bool showBorder = true)
    {
        if (ImGui.IsItemHovered())
        {
            if (showBorder)
            {
                ImGui.PushStyleColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
                ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1);
            }
            ImGui.PushFont(UiBuilder.DefaultFont);
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGuiHelpers.GlobalScale * wrap);
            ImGui.Text(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
            ImGui.PopFont();
            if (showBorder)
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
            }
        }
    }

    public static void HelpMarker(string description)
    {
        ImGui.SameLine();
        ImGuiUtil.DrawFontawesomeIconOutlined(FontAwesomeIcon.InfoCircle, Style.Colors.Black, Style.Components.TooltipBorderColor);
        ImGuiUtil.ToolTip(description);
    }

    public static void DrawColoredBanner(string content, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, color);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, color);
        ImGui.Button(content, new Vector2(-1, ImGui.GetFrameHeight()));
        ImGui.PopStyleColor(3);
    }

    /// <summary>ColorPicker with palette with color picker options.</summary>
    /// <param name="id">Id for the color picker.</param>
    /// <param name="description">The description of the color picker.</param>
    /// <param name="originalColor">The current color.</param>
    /// <param name="flags">Flags to customize color picker.</param>
    /// <returns>Selected color.</returns>


    public static void AddNotification(NotificationType type, string content)
    {
        DalamudApi.PluginLog.Debug($"[Notification] {type}:{content}");
        DalamudApi.ShowNotification(content, type, 5000);
    }

    public static bool InputIntWithReset(string label, ref int num, int step, Func<int> getDefaultValue)
    {
        var b = ImGui.InputInt(label, ref num, step);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            num = getDefaultValue();
            b = true;
        }

        return b;
    }

    public static float GetWindowContentRegionWidth() => ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;

    public static float GetWindowContentRegionHeight() => ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y;

    public static Vector2 GetWindowContentRegion() => ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();

    public static void TextCopyable(string text)
    {
        ImGui.Text(text);

        if (!ImGui.IsItemHovered()) return;
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(text);
            ImGuiUtil.AddNotification(NotificationType.Info, "Copied to clipboard");
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
        ImGui.Text(label);
        ImGui.SetNextItemWidth(-1);
        return ImGui.BeginCombo($"##{label}", previewValue, flags);
    }

    internal static bool DragFloatVertical(string label, ref float value, float vSpeed = 1.0f, float vMin = float.MinValue, float vMax = float.MaxValue, string? format = null, ImGuiSliderFlags flags = ImGuiSliderFlags.None)
    {
        ImGui.Text(label);
        ImGui.SetNextItemWidth(-1);
        return ImGui.DragFloat($"##{label}", ref value, vSpeed, vMin, vMax, format, flags);
    }
}
