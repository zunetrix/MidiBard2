using MidiBard.Structs;
using System.Diagnostics;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

using MidiBard.Managers;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace MidiBard;

public sealed class MiscDebugWidget : Widget
{
    public override string Title => "Misc";

    public int configIndex = 0;
    public int configValue = 0;
    public string filter = string.Empty;

    public MiscDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public unsafe override void Draw()
    {
        if (ImGui.Button("Show Keyboard")) AgentPerformanceMode.Instance()->AgentInterface.Show();

        ImGui.SameLine();
        if (ImGui.Button("Hide Keyboard")) AgentPerformanceMode.Instance()->AgentInterface.Hide();

        ImGui.SameLine();
        if (ImGui.Button("Scale Keyboard"))
        {
            var agent = AgentPerformanceMode.Instance();
            if (agent->AgentInterface.IsAddonShown())
            {
                var addon = RaptureAtkModule.Instance()->RaptureAtkUnitManager
                    .GetAddonById((ushort)agent->AgentInterface.GetAddonId());
                if (addon != null && addon->IsReady)
                    addon->SetScale(0.3f, true);
            }
        }

        if (ImGui.Button("showMetronome")) AgentPerformanceMetronome.Instance()->AgentInterface.Show();

        ImGui.SameLine();
        if (ImGui.Button("hideMetronome")) AgentPerformanceMetronome.Instance()->AgentInterface.Hide();
        ImGui.Checkbox("lazyReleaseKey", ref Context.Plugin.Config.lazyNoteRelease);

        //var systemConfig = &(FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->SystemConfig);
        //var CommonSystemConfig = &(FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->SystemConfig.CommonSystemConfig);
        //var ConfigBase = &(FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->SystemConfig.CommonSystemConfig.ConfigBase);
        //Text($"{(long)systemConfig:X}");
        //Text($"{(long)CommonSystemConfig:X}");
        //Text($"{(long)ConfigBase:X}");

        /*
        ConfigModule* configModule = Framework.Instance()->UIModule->GetConfigModule();
        var offset = (long)Testhooks.Instance.SetoptionHook.Address -
                        (long)Process.GetCurrentProcess().MainModule.BaseAddress;
        ImGui.Button(offset.ToString("X"));
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Clipboard, "c")) ImGui.SetClipboardText(offset).ToString("X"));
        ImGui.Button(((long)configModule).ToString("X"));
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Clipboard, "c")) ImGui.SetClipboardText((long)configModule).ToString("X"));
        ImGui.InputInt("configIndex", ref configIndex);
        ImGui.InputInt("configValue", ref configValue);
        */

        if (ImGui.Button("SetConfig"))
        {
            //Testhooks.Instance.SetoptionHook.Original((IntPtr)configModule, (ulong)configIndex, (ulong)configValue, 2);
        }

        ImGui.SameLine();
        if (ImGui.Button("ToggleConfig"))
        {
            //var v = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule->GetConfigModule()->GetValue((uint)configIndex)->Value;
            //var idv = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule->GetConfigModule()->GetValueById((short)configIndex)->Value;
            //DalamudApi.PluginLog.Information($"{configIndex}: byId:{idv}");
            //Testhooks.Instance.SetoptionHook.Original((IntPtr)configModule, (ulong)configIndex, (ulong)(configValue == 1 ? 0 : 1), 2);
            configValue = configValue == 1 ? 0 : 1;
        }

        ImGui.Dummy(Vector2.Zero);

        // ImGui.InputText("", ref filter, 10000);
        // foreach (var agentInterface in AgentManager.Instance.AgentTable)
        // {
        //     var text = agentInterface.ToString();
        //     if (!string.IsNullOrWhiteSpace(filter))
        //     {
        //         if (text.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
        //         {
        //             ImGui.Text(text);
        //         }
        //     }
        //     else
        //     {
        //         ImGui.Text(text);
        //     }

        // }
    }
}

