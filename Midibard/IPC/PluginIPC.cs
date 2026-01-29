using System;

using Dalamud.Plugin.Ipc;

namespace MidiBard.Ipc;

internal class PluginIPC : IDisposable
{
    public ICallGateProvider<string, object> MidiBardPlayingFileNamePub;

    public PluginIPC()
    {
        MidiBardPlayingFileNamePub = DalamudApi.PluginInterface.GetIpcProvider<string, object>("MidiBard.CurrentPlayingFileName");
    }

    private void ReleaseUnmanagedResources()
    {
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

