using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;

namespace MidiBard;

internal static class OutputChannelCombo
{
    private static readonly (XivChatType Type, string Label)[] Channels =
    [
        (XivChatType.None,            "Current"),
        (XivChatType.Say,             "Say (/s)"),
        (XivChatType.Yell,            "Yell (/y)"),
        (XivChatType.Shout,           "Shout (/sh)"),
        (XivChatType.Party,           "Party (/p)"),
        (XivChatType.FreeCompany,     "Free Company (/fc)"),
        (XivChatType.Echo,            "Echo (/echo)"),
        (XivChatType.Ls1,             "Linkshell 1 (/l1)"),
        (XivChatType.Ls2,             "Linkshell 2 (/l2)"),
        (XivChatType.Ls3,             "Linkshell 3 (/l3)"),
        (XivChatType.Ls4,             "Linkshell 4 (/l4)"),
        (XivChatType.Ls5,             "Linkshell 5 (/l5)"),
        (XivChatType.Ls6,             "Linkshell 6 (/l6)"),
        (XivChatType.Ls7,             "Linkshell 7 (/l7)"),
        (XivChatType.Ls8,             "Linkshell 8 (/l8)"),
        (XivChatType.CrossLinkShell1, "CWLS 1 (/cwl1)"),
        (XivChatType.CrossLinkShell2, "CWLS 2 (/cwl2)"),
        (XivChatType.CrossLinkShell3, "CWLS 3 (/cwl3)"),
        (XivChatType.CrossLinkShell4, "CWLS 4 (/cwl4)"),
        (XivChatType.CrossLinkShell5, "CWLS 5 (/cwl5)"),
        (XivChatType.CrossLinkShell6, "CWLS 6 (/cwl6)"),
        (XivChatType.CrossLinkShell7, "CWLS 7 (/cwl7)"),
        (XivChatType.CrossLinkShell8, "CWLS 8 (/cwl8)"),
    ];

    /// <summary>
    /// Renders a combo box for selecting a chat output channel.
    /// Returns true when the value changes.
    /// </summary>
    public static bool Draw(string id, ref XivChatType current, float width = 200f)
    {
        var selectedLabel = LabelOf(current);
        var changed = false;

        ImGui.SetNextItemWidth(width);
        using var combo = Dalamud.Interface.Utility.Raii.ImRaii.Combo(id, selectedLabel);
        if (!combo) return false;

        foreach (var (type, label) in Channels)
        {
            var isSelected = type == current;
            if (ImGui.Selectable(label, isSelected))
            {
                current = type;
                changed = true;
            }
            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }

        return changed;
    }

    private static string LabelOf(XivChatType type)
    {
        foreach (var (t, label) in Channels)
            if (t == type) return label;
        return type.ToString();
    }
}
