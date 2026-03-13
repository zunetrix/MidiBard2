using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.Json;
using MidiBard.Managers;
using MidiBard.Util;

namespace MidiBard.Ipc;

internal partial class IpcProvider
{
    public void SyncAllSettings()
    {
        Plugin.Config.Save();
        if (!DalamudApi.PartyList.IsPartyLeader()) return;
        var message = IpcMessage.Create(
            IpcMessageType.SyncAllSettings,
            Plugin.Config.JsonSerialize(),
            Plugin.Config.SaveConfigAfterSync.ToString()
        ).Serialize();
        BroadCast(message, includeSelf: false);
    }

    [IpcHandle(IpcMessageType.SyncAllSettings)]
    private void HandleSyncAllSettings(IpcMessage message)
    {
        Plugin.Config.UpdateFromJson(message.StringData[0]);
        ThemeManager.SetTheme(Plugin.Config.CurrentTheme);
        if (bool.TryParse(message.StringData[1], out var save) && save)
            Plugin.Config.Save();

        // Invalidate the compensation cache so new InstrumentCompensationOverrides take effect immediately.
        Plugin.EnsembleManager.InvalidateCompensationCache();
    }

    public void UpdateDefaultPerformer()
    {
        var message = IpcMessage.Create(IpcMessageType.UpdateDefaultPerformer, Plugin.MidiFileConfigManager.defaultPerformer.JsonSerialize()).Serialize();
        BroadCast(message, includeSelf: true);
    }

    [IpcHandle(IpcMessageType.UpdateDefaultPerformer)]
    private void HandleUpdateDefaultPerformer(IpcMessage message)
    {
        Plugin.MidiFileConfigManager.defaultPerformer = message.StringData[0].JsonDeserialize<DefaultPerformer>();
        if (Plugin.CurrentBardPlayback.IsLoaded)
            Plugin.CurrentBardPlayback.MidiFileConfig = Plugin.CurrentBardPlayback.ReloadMidiFileConfig(Plugin.CurrentBardPlayback.MidiFileConfig);
    }

    public void SetOption(string option, int value, bool includeSelf)
    {
        var message = IpcMessage.Create(IpcMessageType.SetOption, option, value.ToString()).Serialize();
        BroadCast(message, includeSelf: includeSelf);
    }

    [IpcHandle(IpcMessageType.SetOption)]
    private void HandleSetOption(IpcMessage message)
    {
        DalamudApi.GameConfig.System.Set(message.StringData[0], uint.Parse(message.StringData[1]));
    }

    public void ShowWindow(WindowsApi.nCmdShow option)
    {
        var message = IpcMessage.Create(IpcMessageType.ShowWindow, option).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.ShowWindow)]
    private void HandleShowWindow(IpcMessage message)
    {
        var nCmdShow = message.DataStruct<WindowsApi.nCmdShow>();
        var hWnd = DalamudApi.PluginInterface.UiBuilder.WindowHandlePtr;
        var isIconic = WindowsApi.IsIconic(hWnd);
        switch (nCmdShow)
        {
            case WindowsApi.nCmdShow.SW_RESTORE when isIconic:
                Plugin.Ui.MainWindow.IsOpen = true;
                WindowsApi.ShowWindow(hWnd, nCmdShow);
                break;
            case WindowsApi.nCmdShow.SW_MINIMIZE when !isIconic:
                Plugin.Ui.MainWindow.IsOpen = false;
                WindowsApi.ShowWindow(hWnd, nCmdShow);
                break;
        }
    }
}
