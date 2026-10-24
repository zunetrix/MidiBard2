using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Managers;

namespace MidiBard;

public sealed class GeneralDebugWidget : Widget
{
    public override string Title => "General";

    public GeneralDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        DrawPartyCacheSection();
    }

    private void DrawPartyCacheSection()
    {
        if (!ImGui.CollapsingHeader("Party & Cache Information", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(10f);

        // Status bar / Overview
        var isInParty = PartyWatcher.IsInParty;
        var isLeader = PartyWatcher.IsPartyLeader;
        var cachedLeaderCid = PartyWatcher.CachedPartyLeaderCID;
        var cachedCids = PartyWatcher.PartyMemberCIDs;
        var partyListCount = DalamudApi.PartyList.Length;

        ImGui.Text("Party Status:");
        ImGui.SameLine();
        if (isInParty)
            ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1f), "In Party");
        else
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Solo / Not in Party");

        ImGui.SameLine(220f);
        ImGui.Text("Role:");
        ImGui.SameLine();
        if (isLeader)
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), "Leader");
        else
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), "Member");

        ImGui.Text($"Cached Leader CID: {cachedLeaderCid:X16} ({cachedLeaderCid})");
        ImGui.Text($"PartyList Length: {partyListCount} | PartyWatcher Cache Count: {cachedCids.Length}");

        ImGui.SameLine();
        if (ImGui.Button("Force Refresh Cache##PartyWatcher"))
        {
            PartyWatcher.RequestRefresh();
            ImGuiUtil.AddNotification(NotificationType.Info, "PartyWatcher refresh requested");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Dalamud Party List vs PartyWatcher Cache table
        ImGui.TextDisabled("Current Party Members (Dalamud PartyList vs Watcher Cache):");

        if (partyListCount == 0)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No members in PartyList.");
        }
        else
        {
            using var table = ImRaii.Table("##PartyMembersDebugTable", 7, 
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);

            if (table)
            {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 30f);
                ImGui.TableSetupColumn("Name & World", ImGuiTableColumnFlags.WidthStretch, 1.5f);
                ImGui.TableSetupColumn("ContentId (CID)", ImGuiTableColumnFlags.WidthFixed, 140f);
                ImGui.TableSetupColumn("EntityId", ImGuiTableColumnFlags.WidthFixed, 80f);
                ImGui.TableSetupColumn("GameObj", ImGuiTableColumnFlags.WidthFixed, 70f);
                ImGui.TableSetupColumn("Leader", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("In Cache?", ImGuiTableColumnFlags.WidthFixed, 70f);
                ImGui.TableHeadersRow();

                for (int i = 0; i < partyListCount; i++)
                {
                    var member = DalamudApi.PartyList[i];
                    if (member == null)
                        continue;

                    ImGui.TableNextRow();

                    // Column 0: Index
                    ImGui.TableNextColumn();
                    ImGui.Text($"[{i}]");

                    // Column 1: Name & World
                    ImGui.TableNextColumn();
                    var nameAndWorld = member.GetNameAndWorld();
                    ImGui.Text(string.IsNullOrEmpty(nameAndWorld) ? "???" : nameAndWorld);

                    // Column 2: ContentId
                    ImGui.TableNextColumn();
                    ImGui.Text($"{member.ContentId}");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        ImGui.SetClipboardText(member.ContentId.ToString());
                        ImGuiUtil.AddNotification(NotificationType.Success, "CID copied");
                    }

                    // Column 3: EntityId
                    ImGui.TableNextColumn();
                    ImGui.Text($"0x{member.EntityId:X8}");

                    // Column 4: GameObject Valid
                    ImGui.TableNextColumn();
                    var goValid = member.GameObject != null && member.GameObject.IsValid();
                    if (goValid)
                        ImGui.TextColored(new Vector4(0.2f, 0.9f, 0.2f, 1f), "Valid");
                    else
                        ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1f), "Null/Inv");

                    // Column 5: Is Leader
                    ImGui.TableNextColumn();
                    if (member.IsPartyLeader())
                        ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), "★");
                    else
                        ImGui.TextDisabled("-");

                    // Column 6: In Watcher Cache?
                    ImGui.TableNextColumn();
                    var inCache = cachedCids.Contains(member.ContentId);
                    if (inCache)
                        ImGui.TextColored(new Vector4(0.2f, 0.9f, 0.2f, 1f), "[YES]");
                    else
                        ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, 1f), "[NO]");
                }
            }
        }

        // Raw CIDs in cache
        if (cachedCids.Length > 0)
        {
            ImGui.Spacing();
            if (ImGui.TreeNode("Raw PartyMemberCIDs in Cache"))
            {
                for (int i = 0; i < cachedCids.Length; i++)
                {
                    var cid = cachedCids[i];
                    ImGui.BulletText($"[{i}] {cid} (0x{cid:X16})");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Copy##cid_{i}"))
                    {
                        ImGui.SetClipboardText(cid.ToString());
                    }
                }
                ImGui.TreePop();
            }
        }
    }
}
