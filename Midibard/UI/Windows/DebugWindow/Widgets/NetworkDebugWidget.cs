using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Managers;
using MidiBard.Util.NetworkCapture;

namespace MidiBard;

public sealed class NetworkDebugWidget : Widget
{
    public override string Title => "Network";

    private bool _autoScroll = true;
    private int _selectedPacket = -1;

    // Recorder state
    private string? _lastExportedPath;
    private string? _exportError;

    public NetworkDebugWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("##NetDbgTabs");
        if (!tabs)
            return;

        using (var tab = ImRaii.TabItem("Ensemble##NetDbgEnsembleTab"))
        {
            if (tab)
                DrawEnsembleTab();
        }

        using (var tab = ImRaii.TabItem("Solo##NetDbgSoloTab"))
        {
            if (tab)
                DrawSoloTab();
        }
    }

    private void DrawEnsembleTab()
    {
        var em = Context.Plugin.EnsembleManager;

        // Controls row - monitor checkbox
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

        // Recorder row
        DrawRecorderRow(em);

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

                    ImGui.SameLine(350 * ImGuiHelpers.GlobalScale);
                    DrawNoteBar(p.Notes, p.Tones);
                }

                if (sel.Performers.Length == 0)
                    ImGui.TextDisabled("  No valid performer slots.");
            }
            ImGui.EndChild();
        }
    }

    // Recorder UI
    private void DrawRecorderRow(EnsembleManager em)
    {
        bool recording = em.NetworkRecordEnabled;

        if (recording)
        {
            // Blinking indicator
            float t = (float)(ImGui.GetTime() % 1.0);
            float alpha = t < 0.5f ? 1f : 0.2f;
            ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, alpha), "●");
            ImGui.SameLine();

            // Elapsed time
            var elapsed = DateTime.Now - em.RecordStartTime;
            int captured;
            lock (em.RecordLog)
                captured = em.RecordLog.Count;
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.1f, 1f),
                $"Recording  {elapsed:mm\\:ss}  -  {captured} packets");

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Stop, "##NetRecordStop", "Stop recording and export MIDI"))
                DoStopAndExport(em);
        }
        else
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Circle, "##NetRecordStart", "Start recording packets to MIDI"))
            {
                _lastExportedPath = null;
                _exportError = null;
                em.StartNetworkRecord();
            }

            ImGui.SameLine();
            ImGui.TextDisabled("Record packets → export .mid");
        }

        // Show last export result
        if (_lastExportedPath != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "✓ Exported:");
            ImGui.SameLine();
            ImGui.TextUnformatted(_lastExportedPath);

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##NetRecordOpenFolder", "Open containing folder"))
            {
                var dir = Path.GetDirectoryName(_lastExportedPath);
                if (dir != null && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }

        if (_exportError != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), $"Export error: {_exportError}");
        }
    }

    private void DoStopAndExport(EnsembleManager em)
    {
        var packets = em.StopNetworkRecord();
        var recordStart = em.RecordStartTime;

        if (packets.Count == 0)
        {
            _exportError = "No packets were captured.";
            return;
        }

        try
        {
            var capturesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher", "pluginConfigs", "MidiBard2", "NetworkCaptures");

            var fileName = $"capture_{recordStart:yyyyMMdd_HHmmss}.mid";
            var outputPath = Path.Combine(capturesDir, fileName);

            _lastExportedPath = NetworkPacketMidiExporter.ExportToFile(
                packets,
                recordStart,
                outputPath,
                resolveNameFunc: ResolveSourceName);

            _exportError = null;
            DalamudApi.PluginLog.Information(
                $"[NetworkRecorder] Exported {packets.Count} packets ({CountDistinctPerformers(packets)} performers) to: {_lastExportedPath}");
        }
        catch (Exception ex)
        {
            _exportError = ex.Message;
            _lastExportedPath = null;
            DalamudApi.PluginLog.Error(ex, "[NetworkRecorder] Export failed");
        }
    }

    private static int CountDistinctPerformers(IReadOnlyList<EnsembleManager.PerformancePacketSnapshot> packets)
        => packets.SelectMany(p => p.Performers).Select(p => p.EntityId).Distinct().Count();

    // Solo packet monitor (validates the recovered solo send/receive signatures)
    private bool _soloAutoScroll = true;

    private void DrawSoloTab()
    {
        var monitor = Context.Plugin.SoloPerformanceMonitor;

        DrawSignatureStatus("Send", monitor.SendSignatureFound, Offsets.SoloSendHandler);
        DrawSignatureStatus("Receive", monitor.ReceiveSignatureFound, Offsets.SoloReceivedHandler);

        ImGui.Spacing();

        using (ImRaii.Disabled(!monitor.SendSignatureFound))
        {
            var send = monitor.SendMonitorEnabled;
            if (ImGui.Checkbox("Monitor sent packets##SoloDbgSend", ref send))
                monitor.SendMonitorEnabled = send;
        }
        ImGuiUtil.ToolTip(
            "Hooks this client's solo performance packet (10 slots, ~50 ms each).\n" +
            "Unverified signature: enable it, play a few notes solo and check that\n" +
            "the slots match what you played and packets arrive every ~500 ms.");

        ImGui.SameLine();
        using (ImRaii.Disabled(!monitor.ReceiveSignatureFound))
        {
            var receive = monitor.ReceiveMonitorEnabled;
            if (ImGui.Checkbox("Monitor received packets##SoloDbgRecv", ref receive))
                monitor.ReceiveMonitorEnabled = receive;
        }
        ImGuiUtil.ToolTip(
            "Hooks other players' solo performance packets.\n" +
            "This signature is a generic prologue and may point to the wrong function:\n" +
            "validate the Send hook first.");

        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll##SoloDbgAutoScroll", ref _soloAutoScroll);

        var log = monitor.GetLog();

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##SoloDbgClear", "Clear log"))
            monitor.Clear();

        ImGui.SameLine();
        using (ImRaii.Disabled(log.Count == 0))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Copy, "##SoloDbgCopy", "Copy log as text"))
                ImGui.SetClipboardText(FormatSoloLog(log));
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"{log.Count} / 200 entries  |  keys logged {monitor.KeysRecorded}, skipped {monitor.KeysSkipped}");
        ImGuiUtil.ToolTip(
            "Keys logged: MidiBard key presses/releases recorded as KEY+/KEY- rows.\n" +
            "Skipped: key events while 'Monitor sent packets' was off.");

        DrawSoloSendStats(log);

        ImGui.Separator();

        if (log.Count == 0)
        {
            ImGui.TextDisabled(monitor.SendMonitorEnabled || monitor.ReceiveMonitorEnabled
                ? "Waiting for packets... play some notes in solo performance."
                : "Enable a monitor to capture packets.");
            return;
        }

        if (!ImGui.BeginTable("##SoloDbgTable", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
                new Vector2(-1, ImGui.GetContentRegionAvail().Y)))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 85 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Dir", ImGuiTableColumnFlags.WidthFixed, 40 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 110 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Delta ms", ImGuiTableColumnFlags.WidthFixed, 60 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Phase", ImGuiTableColumnFlags.WidthFixed, 70 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Slots", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var pkt in log)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(pkt.Timestamp.ToString("HH:mm:ss.fff"));
            if (!pkt.IsKey && ImGui.IsItemHovered())
                ImGui.SetTooltip($"raw: {Convert.ToHexString(pkt.Raw)}");

            ImGui.TableNextColumn();
            if (pkt.IsKey)
            {
                ImGui.TextDisabled(pkt.Kind == SoloPerformanceMonitor.EntryKind.KeyDown ? "KEY+" : "KEY-");

                ImGui.TableNextColumn();
                ImGui.TextDisabled("(self)");
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.TextDisabled($"{pkt.SyncMs % 500,3} / {pkt.SyncMs % 50,2}");
                ImGui.TableNextColumn();
                ImGui.TextDisabled($"key {pkt.Notes[0]} (0x{pkt.Notes[0]:X2})");
                continue;
            }

            if (pkt.IsSend)
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "SEND");
            else
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "RECV");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(pkt.IsSend ? "(self)" : ResolveSourceName(pkt.SourceId));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(pkt.DeltaMs < 0 ? "-" : pkt.DeltaMs.ToString());

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{pkt.SyncMs % 500,3} / {pkt.SyncMs % 50,2}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Packet time modulo 500 ms (packet period) / 50 ms (slot).\nStable values = fixed grid phase for this client.");

            ImGui.TableNextColumn();
            DrawNoteBar(pkt.Notes, pkt.Tones);
        }

        if (_soloAutoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4)
            ImGui.SetScrollHereY(1f);

        ImGui.EndTable();
    }

    private static void DrawSignatureStatus(string label, bool found, nint address)
    {
        ImGui.TextUnformatted($"Solo {label} signature:");
        ImGui.SameLine();
        if (found)
        {
            var offset = address - DalamudApi.SigScanner.Module.BaseAddress;
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), $"found +{offset:X}");
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "not found");
        }
    }

    // Period/phase of this client's sent packets: the data needed for slot-grid alignment.
    private static void DrawSoloSendStats(List<SoloPerformanceMonitor.SoloPacketSnapshot> log)
    {
        var deltas = log.Where(p => p.IsSend && p.DeltaMs > 0).Select(p => p.DeltaMs).TakeLast(20).ToList();
        if (deltas.Count == 0)
            return;

        var phases = log.Where(p => p.IsSend).TakeLast(20).Select(p => p.SyncMs % 500).ToList();
        ImGui.TextUnformatted(
            $"Send period (last {deltas.Count}): avg {deltas.Average():0.0} ms, min {deltas.Min()} ms, max {deltas.Max()} ms   " +
            $"phase mod 500: {phases.Min()}..{phases.Max()} ms");
    }

    private string FormatSoloLog(List<SoloPerformanceMonitor.SoloPacketSnapshot> log)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("time\tdir\tsource\tdeltaMs\tsyncMs\tmod500\tnotes\ttones\traw");
        foreach (var p in log)
        {
            sb.Append(p.Timestamp.ToString("HH:mm:ss.fff")).Append('\t')
              .Append(p.Kind switch
              {
                  SoloPerformanceMonitor.EntryKind.Send => "SEND",
                  SoloPerformanceMonitor.EntryKind.Receive => "RECV",
                  SoloPerformanceMonitor.EntryKind.KeyDown => "KEY+",
                  _ => "KEY-",
              }).Append('\t')
              .Append(p.Kind == SoloPerformanceMonitor.EntryKind.Receive ? ResolveSourceName(p.SourceId) : "self").Append('\t')
              .Append(p.DeltaMs).Append('\t')
              .Append(p.SyncMs).Append('\t')
              .Append(p.SyncMs % 500).Append('\t')
              .Append(Convert.ToHexString(p.Notes)).Append('\t')
              .Append(Convert.ToHexString(p.Tones)).Append('\t')
              .AppendLine(Convert.ToHexString(p.Raw));
        }

        return sb.ToString();
    }

    //  Note bar drawing

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private static string MidiNoteName(int midi)
    {
        // Octave 0 starts at C-1 (midi 0) in standard convention; C3 = midi 48
        int octave = (midi / 12) - 1;
        return $"{NoteNames[midi % 12]}{octave}";
    }

    // Draws a compact inline strip representing the 60 note slots (colored if active).
    // Active notes in the MIDI 48-84 range show their note letter inside the rect.
    private static void DrawNoteBar(byte[] notes, byte[] tones)
    {
        if (notes.Length == 0) { ImGui.TextDisabled("(no data)"); return; }

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float cellW = 14 * ImGuiHelpers.GlobalScale;
        float cellH = 28 * ImGuiHelpers.GlobalScale;
        float gap = 1 * ImGuiHelpers.GlobalScale;
        float fontSz = ImGui.GetFontSize() * 0.55f;

        int count = Math.Min(notes.Length, 60);
        for (int i = 0; i < count; i++)
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
            bool release = notes[i] == 0xFE;

            uint color = release
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.15f, 0.15f, 0.9f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.25f, 0.8f));
            if (active)
            {
                byte tone = tones[i];
                color = tone switch
                {
                    0 => ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.9f, 0.4f, 1f)), // Overdriven / Default
                    1 => ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.5f, 0.9f, 1f)), // Clean
                    2 => ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.6f, 0.2f, 1f)), // Muted
                    3 => ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.5f, 0.1f, 1f)), // Power
                    4 => ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.2f, 0.9f, 1f)), // Special
                    _ => ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.9f, 0.4f, 1f))  // Default
                };
            }

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
                ImGui.SetTooltip(release ? $"slot {i}: release (0xFE)" : $"slot {i}: silent");
            }
        }

        // Advance cursor past the drawn cells
        ImGui.Dummy(new Vector2(count * (cellW + gap) - gap, cellH));
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
