namespace MidiBard.Ipc;

internal partial class IpcProvider
{
    /// <summary>
    /// Relays a remote-sync protocol message (JSON) to the other MidiBard clients on
    /// this machine. Never includes self: the gateway already applied it locally.
    /// </summary>
    public void RemoteSyncEnvelope(string protocolJson)
    {
        var message = IpcMessage.Create(IpcMessageType.RemoteSyncEnvelope, protocolJson).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.RemoteSyncEnvelope)]
    private void HandleRemoteSyncEnvelope(IpcMessage message)
    {
        Plugin.RemoteSyncManager.HandleRelayedEnvelope(message.StringData[0]);
    }
}
