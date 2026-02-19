using System;

using Dalamud.Plugin.Ipc;

namespace MidiBard.Ipc;

internal class PluginIPC : IDisposable
{
    // TODO: remove when BTB updates
    public ICallGateProvider<string, object> MidiBardPlayingFileNamePub;
    public ICallGateProvider<(string FileName, string Duration), object> MidiBardPlayingInfoPub;

    public PluginIPC()
    {
        MidiBardPlayingFileNamePub = DalamudApi.PluginInterface.GetIpcProvider<string, object>("MidiBard.CurrentPlayingFileName");
        MidiBardPlayingInfoPub = DalamudApi.PluginInterface.GetIpcProvider<(string, string), object>("MidiBard.PlayingInfo");
    }

    private void ReleaseUnmanagedResources()
    {
        MidiBardPlayingInfoPub.UnregisterAction();
        MidiBardPlayingFileNamePub.UnregisterAction();
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~PluginIPC()
    {
        ReleaseUnmanagedResources();
    }
}
