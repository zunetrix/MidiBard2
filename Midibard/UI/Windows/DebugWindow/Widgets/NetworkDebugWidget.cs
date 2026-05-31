using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Managers;

namespace MidiBard;

public sealed class NetworkDebugWidget : Widget
{
    public override string Title => "Network";

    private bool _autoScroll = true;
    private int _selectedPacket = -1;

    public NetworkDebugWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var em = Context.Plugin.EnsembleManager;

        //  Controls row
        ImGui.Checkbox("Monitor Performance Packets##NetDbgEnable", ref em.NetworkDebugEnabled);
        ImGuiUtil.ToolTip(
            "Captures the game's ~3-second performance broadcast packet.\n" +
            "Each packet contains up to 8 performer slots with note data.\n" +
            "Sent to ALL players in the same zone (not party-only).");

        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll##NetDbgAutoScroll", ref _autoScroll);

        ImGui.SameLine();
        using (ImRaii.Disabled(!em.NetworkDebugEnabled && em.NetworkDebugLog.Count == 0))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##NetDbgClear", "Clear log"))
            {
                em.ClearNetworkDebugLog();
                _selectedPacket = -1;
            }
        }

        IReadOnlyList<EnsembleManager.PerformancePacketSnapshot> log;
        lock (em.NetworkDebugLog)
            log = em.NetworkDebugLog.ToList();

        ImGui.SameLine();
        var countColor = em.NetworkDebugEnabled
            ? new Vector4(0.2f, 1f, 0.2f, 1f)
            : new Vector4(0.6f, 0.6f, 0.6f, 1f);
        ImGui.TextColored(countColor, $"{log.Count} / 100 packets");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (log.Count == 0)
        {
            ImGui.TextDisabled(em.NetworkDebugEnabled
                ? "Waiting for packets..."
                : "Enable monitoring to capture packets.");
            return;
        }

        //  Packet list
        float detailHeight = _selectedPacket >= 0 && _selectedPacket < log.Count ? 300f * ImGuiHelpers.GlobalScale : 0f;
        float listHeight = ImGui.GetContentRegionAvail().Y - detailHeight - (detailHeight > 0 ? ImGui.GetStyle().ItemSpacing.Y * 2 : 0);

        if (ImGui.BeginTable("##NetDbgTable", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg |
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
            new Vector2(-1, listHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 80 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("SourceId", ImGuiTableColumnFlags.WidthFixed, 100 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Performers", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            for (int i = 0; i < log.Count; i++)
            {
                var pkt = log[i];
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                bool isSelected = _selectedPacket == i;
                if (ImGui.Selectable(pkt.Timestamp.ToString("HH:mm:ss.ff"),
                    isSelected,
                    ImGuiSelectableFlags.SpanAllColumns))
                {
                    _selectedPacket = isSelected ? -1 : i;
                }

                ImGui.TableSetColumnIndex(1);
                var srcName = ResolveSourceName(pkt.SourceId);
                ImGui.TextWrapped(srcName);
                if (srcName != $"0x{pkt.SourceId:X}" && ImGui.IsItemHovered())
                    ImGui.SetTooltip($"0x{pkt.SourceId:X}");

                ImGui.TableSetColumnIndex(2);
                if (pkt.Performers.Length == 0)
                {
                    ImGui.TextDisabled("(no valid slots)");
                }
                else
                {
                    ImGui.Text($"[{pkt.Performers.Length}]  ");
                    foreach (var p in pkt.Performers)
                    {
                        ImGui.SameLine(0, 6 * ImGuiHelpers.GlobalScale);
                        int notes = p.ActiveNoteCount;
                        var col = notes > 0
                            ? new Vector4(0.2f, 0.9f, 0.4f, 1f)
                            : new Vector4(0.55f, 0.55f, 0.55f, 1f);
                        var actorName = ResolveSourceName(p.EntityId);
                        ImGui.TextColored(col, $"{actorName}({notes}♪)");
                        if (actorName != $"0x{p.EntityId:X}" && ImGui.IsItemHovered())
                            ImGui.SetTooltip($"0x{p.EntityId:X}");
                    }
                }
            }

            if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4)
                ImGui.SetScrollHereY(1f);

            ImGui.EndTable();
        }

        //  Detail view for selected packet
        if (_selectedPacket >= 0 && _selectedPacket < log.Count)
        {
            ImGui.Spacing();
            var sel = log[_selectedPacket];
            var selName = ResolveSourceName(sel.SourceId);
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.3f, 1f),
                $"Packet #{_selectedPacket}  -  {sel.Timestamp:HH:mm:ss.fff}  source={selName}  performers={sel.Performers.Length}");
            ImGui.Separator();

            if (ImGui.BeginChild("##NetDbgDetail", new Vector2(-1, detailHeight - ImGui.GetFrameHeightWithSpacing()), false))
            {
                foreach (var p in sel.Performers)
                {
                    var detailName = ResolveSourceName(p.EntityId);
                    ImGui.TextColored(new Vector4(0.6f, 0.9f, 1f, 1f), $"  {detailName}");
                    if (detailName != $"0x{p.EntityId:X}" && ImGui.IsItemHovered())
                        ImGui.SetTooltip($"0x{p.EntityId:X8}");
                    ImGui.SameLine(200 * ImGuiHelpers.GlobalScale);
                    DrawNoteBar(p.Notes);
                }

                if (sel.Performers.Length == 0)
                    ImGui.TextDisabled("  No valid performer slots.");
            }
            ImGui.EndChild();
        }
    }

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private static string MidiNoteName(int midi)
    {
        // Octave 0 starts at C-1 (midi 0) in standard convention; C3 = midi 48
        int octave = (midi / 12) - 1;
        return $"{NoteNames[midi % 12]}{octave}";
    }

    // Draws a compact inline strip representing the 60 note slots (colored if active).
    // Active notes in the MIDI 48-84 range show their note letter inside the rect.
    private static void DrawNoteBar(byte[] notes)
    {
        if (notes.Length == 0) { ImGui.TextDisabled("(no data)"); return; }

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float cellW = 14 * ImGuiHelpers.GlobalScale;
        float cellH = 28 * ImGuiHelpers.GlobalScale;
        float gap = 1 * ImGuiHelpers.GlobalScale;
        float fontSz = ImGui.GetFontSize() * 0.55f;

        for (int i = 0; i < notes.Length && i < 60; i++)
        {
            if (i > 0 && i % 20 == 0)
            {
                var lineX = pos.X + i * (cellW + gap) - (gap / 2);
                drawList.AddLine(
                    new Vector2(lineX, pos.Y - 2 * ImGuiHelpers.GlobalScale),
                    new Vector2(lineX, pos.Y + cellH + 2 * ImGuiHelpers.GlobalScale),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 1f)),
                    2f * ImGuiHelpers.GlobalScale);
            }

            bool active = notes[i] != 0xFF && notes[i] != 0xFE;
            uint color = active
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.9f, 0.4f, 1f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.25f, 0.8f));

            var min = new Vector2(pos.X + i * (cellW + gap), pos.Y);
            var max = new Vector2(min.X + cellW, min.Y + cellH);
            drawList.AddRectFilled(min, max, color, 1.5f);

            if (active)
            {
                // Packet note 24 corresponds to MIDI note 48 (C3)
                int midiNote = notes[i] + 24;
                string noteName = MidiNoteName(midiNote);          // e.g. "C#4"
                string pitchPart = NoteNames[midiNote % 12];        // e.g. "C#"
                string octavePart = ((midiNote / 12) - 1).ToString(); // e.g. "4"

                uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.9f));

                // Top: pitch letter(s)
                float pitchW = pitchPart.Length * fontSz * 0.52f;
                var pitchPos = new Vector2(
                    min.X + (cellW - pitchW) * 0.5f,
                    min.Y + 2 * ImGuiHelpers.GlobalScale);
                drawList.AddText(ImGui.GetFont(), fontSz, pitchPos, textColor, pitchPart);

                // Bottom: octave digit
                float octaveW = fontSz * 0.52f;
                var octavePos = new Vector2(
                    min.X + (cellW - octaveW) * 0.5f,
                    min.Y + cellH - fontSz - 2 * ImGuiHelpers.GlobalScale);
                drawList.AddText(ImGui.GetFont(), fontSz, octavePos, textColor, octavePart);

                if (ImGui.IsMouseHoveringRect(min, max))
                    ImGui.SetTooltip($"{noteName}  (slot {i}, raw {notes[i]})");
            }
            else if (ImGui.IsMouseHoveringRect(min, max))
            {
                ImGui.SetTooltip($"slot {i}: silent");
            }
        }

        // Advance cursor past the drawn cells
        ImGui.Dummy(new Vector2(60 * (cellW + gap) - gap, cellH));
    }

    private string ResolveSourceName(uint entityId)
    {
        if (entityId == 0 || entityId == 0xE0000000u)
            return $"0x{entityId:X}";

        // 1. Try party list: EntityId → ContentId → configured name
        var partyMember = DalamudApi.PartyList.FirstOrDefault(p => p.EntityId == entityId);
        if (partyMember != null)
        {
            var configs = Context.Plugin.Config.EnsembleMemberConfigs;
            var cfgMatch = configs.FirstOrDefault(c => c.Cid == partyMember.ContentId);
            if (cfgMatch?.Name is { Length: > 0 } cfgName)
                return cfgName;

            // Check linked sub-members
            foreach (var cfg in configs)
            {
                var linked = cfg.LinkedEnsembleMembers?.FirstOrDefault(m => m.Cid == partyMember.ContentId);
                if (linked?.Name is { Length: > 0 } lname)
                    return lname;
            }

            // Fallback to live party member name
            var partyName = partyMember.Name.ToString();
            if (!string.IsNullOrEmpty(partyName))
                return partyName;
        }

        // 2. Try object table for non-party zone characters
        foreach (var obj in DalamudApi.ObjectTable.PlayerObjects)
        {
            if (obj.EntityId == entityId)
            {
                var objName = obj.Name.ToString();
                if (!string.IsNullOrEmpty(objName))
                    return objName;
            }
        }

        return $"0x{entityId:X}";
    }
}
