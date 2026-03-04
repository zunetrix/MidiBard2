using System;
using System.Reflection;

using Dalamud.Bindings.ImGui;

using MidiBard.Managers;

namespace MidiBard;

public sealed class OffsetsDebugWidget : Widget
{
    public override string Title => "Offsets";

    public OffsetsDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        try
        {
            var type = typeof(Offsets);

            foreach (var i in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = i.GetValue(null);
                string variable;
                if (value is IntPtr ptr)
                {
                    var relaive = ptr.ToInt64() - (long)DalamudApi.SigScanner.Module.BaseAddress;
                    variable = $"{i.Name} +{relaive:X}";
                    ImGui.Text(variable);
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"C##{i.Name}"))
                    {
                        ImGui.SetClipboardText(ptr.ToInt64().ToString("X"));
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"CR##{i.Name}"))
                    {
                        ImGui.SetClipboardText($"HEADER+{relaive:X}");
                    }
                }
                else
                {
                    variable = $"{i.Name} {value}";
                    ImGui.Text(variable);
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"C##{i.Name}"))
                        ImGui.SetClipboardText(variable);
                }

            }
        }
        catch (Exception e)
        {
            ImGui.TextColored(Style.Colors.RedVivid, e.ToString());
        }

    }
}

