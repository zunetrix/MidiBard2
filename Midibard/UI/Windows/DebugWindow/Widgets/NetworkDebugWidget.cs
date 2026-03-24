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
        float detailHeight = _selectedPacket >= 0 && _selectedPacket < log.Count ? 160f * ImGuiHelpers.GlobalScale : 0f;
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
                ImGui.Text($"0x{pkt.SourceId:X8}");

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
                        ImGui.TextColored(col, $"0x{p.ActorId:X}({notes}♪)");
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
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.3f, 1f),
                $"Packet #{_selectedPacket}  —  {sel.Timestamp:HH:mm:ss.fff}  source=0x{sel.SourceId:X8}  performers={sel.Performers.Length}");
            ImGui.Separator();

            if (ImGui.BeginChild("##NetDbgDetail", new Vector2(-1, detailHeight - ImGui.GetFrameHeightWithSpacing()), false))
            {
                foreach (var p in sel.Performers)
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.9f, 1f, 1f), $"  0x{p.ActorId:X8}");
                    ImGui.SameLine(130 * ImGuiHelpers.GlobalScale);
                    DrawNoteBar(p.Notes);
                }

                if (sel.Performers.Length == 0)
                    ImGui.TextDisabled("  No valid performer slots.");
            }
            ImGui.EndChild();
        }
    }

    // Draws a compact inline strip representing the 60 note slots (colored if active).
    private static void DrawNoteBar(byte[] notes)
    {
        if (notes.Length == 0) { ImGui.TextDisabled("(no data)"); return; }

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float cellW = 6 * ImGuiHelpers.GlobalScale;
        float cellH = 12 * ImGuiHelpers.GlobalScale;
        float gap = 1 * ImGuiHelpers.GlobalScale;

        for (int i = 0; i < notes.Length && i < 60; i++)
        {
            bool active = notes[i] != 0xFF;
            uint color = active
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.9f, 0.4f, 1f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.25f, 0.8f));

            var min = new Vector2(pos.X + i * (cellW + gap), pos.Y);
            var max = new Vector2(min.X + cellW, min.Y + cellH);
            drawList.AddRectFilled(min, max, color, 1f);

            if (active && ImGui.IsMouseHoveringRect(min, max))
                ImGui.SetTooltip($"slot {i}: note {notes[i]}");
        }

        // Advance cursor past the drawn cells
        ImGui.Dummy(new Vector2(60 * (cellW + gap), cellH));
    }
}
