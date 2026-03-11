namespace MidiBard.Ipc;

internal partial class IpcProvider
{
    public void BroadcastDisconnectDatabase()
    {
        var message = IpcMessage.Create(IpcMessageType.DisconnectDatabase).Serialize();
        BroadCast(message, includeSelf: true);
    }

    [IpcHandle(IpcMessageType.DisconnectDatabase)]
    private void HandleDisconnectDatabase(IpcMessage message)
    {
        Plugin.CloseDatabase();
    }

    public void BroadcastReconnectDatabase()
    {
        var message = IpcMessage.Create(IpcMessageType.ReconnectDatabase).Serialize();
        BroadCast(message, includeSelf: true);
    }

    [IpcHandle(IpcMessageType.ReconnectDatabase)]
    private void HandleReconnectDatabase(IpcMessage message)
    {
        Plugin.ReopenDatabase();
    }
}
