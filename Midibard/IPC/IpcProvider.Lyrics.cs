using Dalamud.Interface.ImGuiNotification;

namespace MidiBard.Ipc;

internal partial class IpcProvider
{
    public void ReloadLyrics(string lyricsFilePath)
    {
        var message = IpcMessage.Create(IpcMessageType.ReloadLyrics, lyricsFilePath).Serialize();
        BroadCast(message, includeSelf: true);
    }

    [IpcHandle(IpcMessageType.ReloadLyrics)]
    private void HandleReloadLyrics(IpcMessage message)
    {
        var lrcPath = message.StringData[0];
        try
        {
            Plugin.LyricsPlayer.LoadLyrics(lrcPath);
            ImGuiUtil.AddNotification(NotificationType.Info, "Lrc Reloaded " + lrcPath);
        }
        catch
        {
            ImGuiUtil.AddNotification(NotificationType.Error, "Error when reloading Lrc " + lrcPath);
        }
    }
}
