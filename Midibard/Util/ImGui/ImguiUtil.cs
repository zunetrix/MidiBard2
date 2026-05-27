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

    public static bool IconButton(FontAwesomeIcon icon, string? id = null, string? tooltip = null, Vector4? color = null, Vector2? size = null)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, color ?? Vector4.One, color != null))
        {
            var iconButtonSize = ImGui.CalcTextSize(icon.ToIconString()) + ImGui.GetStyle().FramePadding * 2;
            var buttonSize = size ?? iconButtonSize;
            var result = ImGui.Button($"{icon.ToIconString()}##{id}", buttonSize);
            if (tooltip != null) ToolTip(tooltip);
            return result;
        }
    }

    public static void TextIcon(FontAwesomeIcon icon, Vector4? color = null)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (color.HasValue)
                ImGui.TextColored(color.Value, icon.ToIconString());
            else
                ImGui.Text(icon.ToIconString());
        }
    }


    public static bool IconButtonToggle(string id, ref bool btnValue, FontAwesomeIcon iconOn, FontAwesomeIcon iconOff, string? tooltip = null)
    {
        var showHideIcon = btnValue ? iconOn : iconOff;
        var changed = false;

        using (ImRaii.PushColor(ImGuiCol.Button, btnValue ? Style.Components.ButtonSuccessNormal : Style.Components.ButtonDangerNormal)
        .Push(ImGuiCol.ButtonHovered, btnValue ? Style.Components.ButtonSuccessHovered : Style.Components.ButtonDangerHovered)
        .Push(ImGuiCol.ButtonActive, btnValue ? Style.Components.ButtonSuccessActive : Style.Components.ButtonDangerActive)
        )
        {
            if (ImGuiUtil.IconButton(showHideIcon, id, tooltip))
            {
                btnValue = !btnValue;
                changed = true;
            }
        }
        return changed;
    }

    // Colored button components
    // Each variant pushes the three button color slots (normal/hovered/active) from
    // Style.Components so that color changes only need to be made in one place.

    public static bool DangerIconButton(FontAwesomeIcon icon, string? id = null, string? tooltip = null, Vector2? size = null)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
        {
            return IconButton(icon, id, tooltip, size: size);
        }
    }

    public static bool SuccessIconButton(FontAwesomeIcon icon, string? id = null, string? tooltip = null, Vector2? size = null)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonSuccessNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonSuccessHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonSuccessActive))
        {
            return IconButton(icon, id, tooltip, size: size);
        }
    }

    public static bool PrimaryIconButton(FontAwesomeIcon icon, string? id = null, string? tooltip = null, Vector2? size = null)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonBlueActive))
        {
            return IconButton(icon, id, tooltip, size: size);
        }
    }

    public static bool InfoIconButton(FontAwesomeIcon icon, string? id = null, string? tooltip = null, Vector2? size = null)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonInfoHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonInfoActive))
        {
            return IconButton(icon, id, tooltip, size: size);
        }
    }

    public static bool DangerButton(string label, Vector2? size = null)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
        {
            return ImGui.Button(label, size ?? Vector2.Zero);
        }
    }

    public static bool SuccessButton(string label, Vector2? size = null)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonSuccessNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonSuccessHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonSuccessActive))
        {
            return ImGui.Button(label, size ?? Vector2.Zero);
        }
    }

    public static bool PrimaryButton(string label, Vector2? size = null)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonBlueActive))
        {
            return ImGui.Button(label, size ?? Vector2.Zero);
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
            using (ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor, showBorder))
            {
                using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1, showBorder))
                {
                    ImGui.PushFont(UiBuilder.DefaultFont);
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(ImGuiHelpers.GlobalScale * wrap);
                    ImGui.Text(desc);
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                    ImGui.PopFont();
                }
            }
        }
    }

    public static void HelpMarker(string description)
    {
        ImGui.SameLine();
        ImGuiUtil.DrawFontawesomeIconOutlined(FontAwesomeIcon.InfoCircle, Style.Colors.Black, Style.Components.TooltipBorderColor);
        ImGuiUtil.ToolTip(description);
    }

    public static void Spinner(string label, float radius, float thickness, Vector4 color)
    {
        var style = ImGui.GetStyle();
        ImGui.PushID(label);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + style.FramePadding.Y);
        var size = new Vector2(radius * 2, radius * 2);

        ImGuiHelpers.ScaledDummy(size);

        var dummyPos = ImGui.GetItemRectMin();
        var dummySize = ImGui.GetItemRectSize();
        var center = new Vector2(
            dummyPos.X + (dummySize.X / 2),
            dummyPos.Y + (dummySize.Y / 2)
        );

        // Render
        ImGui.GetWindowDrawList().PathClear();

        var numSegments = 30;
        var start = Math.Abs(Math.Sin(ImGui.GetTime() * 1.8f) * (numSegments - 5));

        var aMin = Math.PI * 2.0f * ((float)start / numSegments);
        var aMax = Math.PI * 2.0f * (((float)numSegments - 3) / numSegments);

        for (var i = 0; i < numSegments; ++i)
        {
            var a = aMin + i / (float)numSegments * (aMax - aMin);
            ImGui.GetWindowDrawList().PathLineTo(
                new Vector2(
                    center.X + (float)Math.Cos(a + (float)ImGui.GetTime() * 8) * (radius - thickness / 2),
                    center.Y + (float)Math.Sin(a + (float)ImGui.GetTime() * 8) * (radius - thickness / 2)
                )
            );
        }

        ImGui.GetWindowDrawList().PathStroke(ColorUtil.Vector4ToUint(color), ImDrawFlags.None, thickness);

        ImGui.PopID();
    }

    // spinner usage
    // private void DrawSpinner(string id)
    // {
    //     var spinnerLabel = $"##Spinner_{id}";
    //     // var spinnerRadius = ImGui.GetTextLineHeight() / 4;
    //     var spinnerRadius = ImGui.GetTextLineHeight();
    //     var spinnerThickness = 5 * ImGuiHelpers.GlobalScale;
    //     ImGui.SetCursorPosY(ImGui.GetCursorPosY() + spinnerRadius);
    //     ImGuiUtil.Spinner(spinnerLabel, spinnerRadius, spinnerThickness, Style.Colors.Blue);
    // }

    public static void DrawColoredBanner(string content, Vector4 color)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, color)
        .Push(ImGuiCol.ButtonHovered, color)
        .Push(ImGuiCol.ButtonActive, color))
        {
            ImGui.Button(content, new Vector2(-1, ImGui.GetFrameHeight()));
        }
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

        using (ImRaii.PushColor(ImGuiCol.Text, outline))
        {
            foreach (var x in Enumerable.Range(-1, 3))
            {
                foreach (var y in Enumerable.Range(-1, 3))
                {
                    if (x is 0 && y is 0) continue;

                    ImGui.SetCursorPos(cursorStart + new Vector2(x, y));
                    ImGui.Text(icon.ToIconString());
                }
            }
        }

        using (ImRaii.PushColor(ImGuiCol.Text, iconColor))
        {
            ImGui.SetCursorPos(cursorStart);
            ImGui.Text(icon.ToIconString());
        }

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
