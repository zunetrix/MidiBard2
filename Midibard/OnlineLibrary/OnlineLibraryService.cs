using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Dalamud.Interface.ImGuiNotification;

using MidiBard.Extensions.Dalamud.Party;

namespace MidiBard.OnlineLibrary;

/// <summary>
/// Entry point to the online MIDI libraries for the rest of the plugin. The HTTP client is only created on first use,
/// so a user who never opens the browser pays nothing. Disposing aborts every request and download in flight.
/// </summary>
/// <remarks>
/// Network and disk work run on the thread pool. Only the final hand-off to the playlist or the playback,
/// which expects the framework thread, is posted there; it starts async work and returns at once.
/// </remarks>
internal sealed class OnlineLibraryService : IDisposable
{
    private readonly Plugin _plugin;
    private readonly Lazy<MidiLibraryClient> _client;
    private readonly CancellationTokenSource _lifetime = new();
    private volatile bool _disposed;

    public OnlineLibraryService(Plugin plugin)
    {
        _plugin = plugin;
        _client = new Lazy<MidiLibraryClient>(
            () => new MidiLibraryClient(Endpoints, $"MidiBard2/{Plugin.Version?.ToString()}"),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public MidiLibraryEndpoints Endpoints => MidiLibraryEndpoints.Default;

    public MidiLibraryClient Client
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _client.Value;
        }
    }

    /// <summary>Downloads a file and loads it into the playback. The party leader also sends it to the local clients over IPC.</summary>
    public void DownloadToPlayback(Uri url) => Run("Download", async ct =>
    {
        var midi = await Client.DownloadAsync(url, ct).ConfigureAwait(false);

        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            if (ct.IsCancellationRequested) return;
            if (DalamudApi.PartyList.IsPartyLeader())
                _plugin.IpcProvider.SendDownloadedSong(midi.FileName, midi.Data);
            _ = _plugin.FilePlayback.LoadPlayback(midi.FileName, new MemoryStream(midi.Data));
        }).ConfigureAwait(false);
    });

    /// <summary>Downloads a file into the download folder and adds it to the current playlist.</summary>
    public void DownloadToPlaylist(Uri url) => Run("Download", async ct =>
    {
        var midi = await Client.DownloadAsync(url, ct).ConfigureAwait(false);
        var path = await SaveAsync(midi, ct).ConfigureAwait(false);

        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            if (ct.IsCancellationRequested) return;
            _ = _plugin.PlaylistManager.AddSongsAsync([path]);
        }).ConfigureAwait(false);

        ImGuiUtil.AddNotification(NotificationType.Success, $"Downloaded {Path.GetFileName(path)}");
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Cancelled but not disposed: running tasks may still read its token.
        _lifetime.Cancel();
        if (_client.IsValueCreated)
            _client.Value.Dispose();
    }

    /// <summary>
    /// Writes the file to the download folder without overwriting another file: an identical file is reused,
    /// a different file with the same name gets a " (n)" suffix.
    /// </summary>
    private async Task<string> SaveAsync(DownloadedMidi midi, CancellationToken ct)
    {
        var folder = _plugin.Config.OnlineLibraryDownloadFolder;
        if (string.IsNullOrWhiteSpace(folder))
            folder = Configuration.DefaultOnlineLibraryDownloadFolder;
        Directory.CreateDirectory(folder);

        var name = Path.GetFileNameWithoutExtension(midi.FileName);
        var ext = Path.GetExtension(midi.FileName);

        for (var i = 0; ; i++)
        {
            var path = Path.Combine(folder, i == 0 ? midi.FileName : $"{name} ({i}){ext}");
            if (!File.Exists(path))
            {
                await File.WriteAllBytesAsync(path, midi.Data, ct).ConfigureAwait(false);
                return path;
            }
            if ((await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false)).SequenceEqual(midi.Data))
                return path;
        }
    }

    private void Run(string operation, Func<CancellationToken, Task> work)
    {
        if (_disposed) return;
        var ct = _lifetime.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await work(ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                // Plugin is unloading.
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Warning(e, "[OnlineLibrary] {0} failed", operation);
                ImGuiUtil.AddNotification(NotificationType.Error, $"{operation} failed: {e.Message}");
            }
        }, ct);
    }
}
