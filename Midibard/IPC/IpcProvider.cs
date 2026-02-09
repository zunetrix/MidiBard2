using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

using TinyIpc.IO;
using TinyIpc.Messaging;

using MidiBard.Managers;
using MidiBard.Util;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.Json;

namespace MidiBard.Ipc;

internal class IpcProvider : IDisposable
{
    private readonly TinyMessageBus MessageBus;
    private readonly ConcurrentQueue<(byte[] serialized, bool includeSelf)> MessageQueue = new();
    private readonly AutoResetEvent _autoResetEvent = new(false);
    private readonly Dictionary<IpcMessageType, Action<IpcMessage>> _ipcHandlers = new();
    private readonly bool _initFailed;
    private bool _messagesQueueRunning = true;

    private Plugin Plugin { get; }

    public IpcProvider(Plugin plugin)
    {
        Plugin = plugin;

        RegisterHandlersFromType(typeof(IpcHandlers), new IpcHandlers(plugin));

        try
        {
            const long maxFileSize = 1 << 24;
            // Use versionado name para evitar conflitos entre diferentes builds
            string ipcName = $"MidiBard.IPC.{Plugin.VersionString}";

            MessageBus = new TinyMessageBus(new TinyMemoryMappedFile(ipcName, maxFileSize), true);
            MessageBus.MessageReceived += OnMessageReceived;

            var thread = new Thread(ProcessMessageQueue) { IsBackground = true, Name = "MidiBard.IPC.Queue" };
            thread.Start();

            DalamudApi.PluginLog.Information($"IPC Provider initialized with: {ipcName}");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "TinyIpc init failed.");
            _initFailed = true;
        }
    }

    public void Dispose()
    {
        try
        {
            _messagesQueueRunning = false;
            _autoResetEvent.Set();
            Thread.Sleep(100); // Aguarda fila processar

            if (MessageBus != null)
            {
                MessageBus.MessageReceived -= OnMessageReceived;
                MessageBus.Dispose();
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, "Error disposing IPC");
        }
        finally
        {
            _autoResetEvent?.Dispose();
        }
    }

    private void RegisterHandlersFromType(Type type, object instance)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var attr = method.GetCustomAttribute<IpcHandleAttribute>();
            if (attr == null) continue;
            var del = (Action<IpcMessage>)Delegate.CreateDelegate(typeof(Action<IpcMessage>), instance, method);
            _ipcHandlers[attr.MessageType] = del;
        }
    }

    private void OnMessageReceived(object sender, TinyMessageReceivedEventArgs e)
    {
        if (_initFailed || e?.Message == null) return;
        try
        {
            var message = e.Message.ToArray<byte>().Decompress().ProtoDeserialize<IpcMessage>();
            if (_ipcHandlers.TryGetValue(message.MessageType, out var handler))
                handler(message);
            else
                DalamudApi.PluginLog.Warning($"No handler for {message.MessageType}");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error processing IPC message");
        }
    }

    private void ProcessMessageQueue()
    {
        DalamudApi.PluginLog.Information("IPC message queue worker started");
        while (_messagesQueueRunning)
        {
            try
            {
                while (MessageQueue.TryDequeue(out var dequeue))
                {
                    try
                    {
                        if (MessageBus != null && MessageBus.PublishAsync(dequeue.serialized).Wait(5000) && dequeue.includeSelf)
                            OnMessageReceived(null, new TinyMessageReceivedEventArgs(dequeue.serialized));
                    }
                    catch (Exception e)
                    {
                        DalamudApi.PluginLog.Warning(e, "Error publishing IPC");
                    }
                }
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Warning(e, "Error in message queue processing");
            }

            _autoResetEvent.WaitOne(1000); // Timeout para não ficar preso se Dispose for chamado
        }
        DalamudApi.PluginLog.Information("IPC message queue worker ended");
    }

    public void BroadCast(byte[] serialized, bool includeSelf = false)
    {
        if (_initFailed || !Plugin.Config.SyncClients) return;

        MessageQueue.Enqueue(new(serialized, includeSelf));
        _autoResetEvent.Set();
    }

    public void SyncAllSettings()
    {
        Plugin.Config.Save();
        var message = IpcMessage.Create(
            IpcMessageType.SyncAllSettings,
            Plugin.Config.JsonSerialize(),
            Plugin.Config.SaveConfigAfterSync.ToString()
        ).Serialize();
        BroadCast(message, includeSelf: false);
    }

    public void SyncPlaylist()
    {
        if (Plugin.PlaylistManager.CurrentContainer == null) return;

        var message = IpcMessage.Create(IpcMessageType.SyncPlaylist, Plugin.PlaylistManager.CurrentContainer.JsonSerialize()).Serialize();
        BroadCast(message);
    }

    public void RemoveTrackIndex(int songIndex)
    {
        var message = IpcMessage.Create(IpcMessageType.RemoveTrackIndex, songIndex).Serialize();
        BroadCast(message);
    }

    public void MoveSongToIndex(int songIndex, int targetIndex)
    {
        var message = IpcMessage.Create(IpcMessageType.MoveSongToIndex, (songIndex, targetIndex)).Serialize();
        BroadCast(message);
    }

    public void ChangeSongPlayedStatus(int songIndex, bool newStatus)
    {
        var message = IpcMessage.Create(IpcMessageType.ChangeSongPlayedStatus, (songIndex, newStatus)).Serialize();
        BroadCast(message);
    }

    public void ResetAllSongsPlayedStatus()
    {
        var message = IpcMessage.Create(IpcMessageType.ResetAllSongsPlayedStatus).Serialize();
        BroadCast(message);
    }

    public void ReloadLyrics(string lyricsFilePath)
    {
        var message = IpcMessage.Create(IpcMessageType.ReloadLyrics, lyricsFilePath).Serialize();
        BroadCast(message, includeSelf: true);
    }

    public void UpdateMidiFileConfig(MidiFileConfig midiFileConfig)
    {
        var message = IpcMessage.Create(IpcMessageType.UpdateMidiFileConfig, midiFileConfig.JsonSerialize()).Serialize();
        BroadCast(message, includeSelf: true);
    }

    public void LoadPlayback(int index, bool includeSelf = false)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || Plugin.Config.playOnMultipleDevices) return;

        var message = IpcMessage.Create(IpcMessageType.LoadPlaybackIndex, index).Serialize();
        BroadCast(message, includeSelf);
    }

    public void UpdateInstrument(bool takeout)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || Plugin.Config.playOnMultipleDevices) return;

        var message = IpcMessage.Create(IpcMessageType.SetInstrument, takeout).Serialize();
        BroadCast(message, includeSelf: true);
    }

    public void SetOption(string option, int value, bool includeSelf)
    {
        var message = IpcMessage.Create(IpcMessageType.SetOption, option, value.ToString()).Serialize();
        BroadCast(message, includeSelf: includeSelf);
    }

    public void ShowWindow(WindowsApi.nCmdShow option)
    {
        var message = IpcMessage.Create(IpcMessageType.ShowWindow, option).Serialize();
        BroadCast(message);
    }

    public void UpdateDefaultPerformer()
    {
        var message = IpcMessage.Create(IpcMessageType.UpdateDefaultPerformer, Plugin.MidiFileConfigManager.defaultPerformer.JsonSerialize()).Serialize();
        BroadCast(message, includeSelf: true);
    }

    public void PlaybackSpeed(float playbackSpeed)
    {
        if (!DalamudApi.PartyList.IsPartyLeader()) return;

        var message = IpcMessage.Create(IpcMessageType.PlaybackSpeed, playbackSpeed).Serialize();
        BroadCast(message);
    }

    public void GlobalTranspose(int transpose)
    {
        var message = IpcMessage.Create(IpcMessageType.GlobalTranspose, transpose).Serialize();
        BroadCast(message);
    }

    public void SetPlaybackTime(TimeSpan time)
    {
        var message = IpcMessage.Create(IpcMessageType.MoveToTime, time).Serialize();
        BroadCast(message);
    }

    public void ErrPlaybackNull(string characterName)
    {
        var message = IpcMessage.Create(IpcMessageType.ErrPlaybackNull, characterName).Serialize();
        BroadCast(message, includeSelf: true);
    }

    public void SendDownloadedSong(string filename, byte[] mididata)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || Plugin.Config.playOnMultipleDevices) return;

        var message = IpcMessage.Create(IpcMessageType.SendDownloadedSong, mididata).Serialize();
        BroadCast(message);
    }
}
