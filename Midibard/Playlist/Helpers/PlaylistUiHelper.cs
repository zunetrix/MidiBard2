using System;
using System.Linq;
using System.Threading.Tasks;

using MidiBard.Control.MidiControl;
using MidiBard.Extensions.Dalamud;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Util;

namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Manages UI-related operations: getting data for display, searching, formatting, and playback loading.
/// Provides all methods needed for UI rendering and user interaction.
/// </summary>
internal class PlaylistUiHelper
{
    private readonly CurrentSongController _songController;
    private readonly Plugin _plugin;

    // Callback for loading playback internally
    private readonly Func<int, Task<bool>> _onLoadPlaybackCallback;

    public PlaylistUiHelper(
        CurrentSongController songController,
        Plugin plugin,
        Func<int, Task<bool>> onLoadPlaybackCallback)
    {
        _songController = songController;
        _plugin = plugin;
        _onLoadPlaybackCallback = onLoadPlaybackCallback;
    }

    /// <summary>
    /// Get all songs from a playlist for UI display.
    /// </summary>
    public async Task<System.Collections.Generic.List<Song>> GetPlaylistSongsAsync(int playlistId)
    {
        try
        {
            var playlist = await ServiceContainer.PlaylistService.GetByIdAsync(playlistId);
            if (playlist?.Songs == null)
                return new();

            // Return songs from playlist (already loaded via service)
            return playlist.Songs
                .Where(ps => ps.Song != null)
                .Select(ps => ps.Song)
                .ToList()!;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistUiHelper] Error getting playlist songs for {playlistId}");
            return new();
        }
    }

    /// <summary>
    /// Get playlist by ID for UI display.
    /// </summary>
    public async Task<Playlist?> GetPlaylistByIdAsync(int playlistId)
    {
        try
        {
            return await ServiceContainer.PlaylistService.GetByIdAsync(playlistId);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistUiHelper] Error getting playlist {playlistId}");
            return null;
        }
    }

    /// <summary>
    /// Find song index by name within the current playlist.
    /// Case-insensitive search using song name or file name.
    /// Returns -1 if not found.
    /// </summary>
    public int FindSongIndex(string songName, Playlist? currentPlaylist)
    {
        if (string.IsNullOrWhiteSpace(songName) || currentPlaylist == null)
            return -1;

        return currentPlaylist.Songs.FindIndex(ps =>
            (ps.Song?.Name ?? System.IO.Path.GetFileName(ps.Song?.FilePath ?? ""))
                .Contains(songName, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>
    /// Get the formatted name of the currently playing song using configured extraction rules.
    /// Uses identity-based song reference instead of index for stability across playlist mutations.
    /// </summary>
    public string GetPostSongName()
    {
        var song = _songController.CurrentPlayingSong?.Song;
        if (song == null) return string.Empty;

        return FormatSongName(song);
    }

    /// <summary>
    /// Get the formatted name for a specific playlist song at given index.
    /// Used when you need formatting for a song other than the currently playing one.
    /// </summary>
    public string GetPostSongName(int songIndex, Playlist? currentPlaylist)
    {
        if (currentPlaylist == null || !currentPlaylist.IsValidSongIndex(songIndex))
            return string.Empty;

        var song = currentPlaylist.Songs[songIndex].Song;
        if (song == null) return string.Empty;

        return FormatSongName(song);
    }

    private string FormatSongName(Song song)
    {
        var cfg = _plugin.Config.PostSong;
        return cfg.Mode switch
        {
            PostSongMode.DatabaseTemplate => FormatTemplate(song, cfg),
            PostSongMode.FilepathRegex => FormatRegex(song, cfg),
            _ => string.Empty,
        };
    }

    private static string FormatTemplate(Song song, PostSongConfig cfg)
    {
        var result = song.FormatFromTemplate(cfg.Template);
        if (!string.IsNullOrEmpty(cfg.FindRegex))
        {
            try { result = System.Text.RegularExpressions.Regex.Replace(result, cfg.FindRegex, cfg.Replacement); }
            catch { /* invalid regex - leave result as-is */ }
        }
        return result;
    }

    private static string FormatRegex(Song song, PostSongConfig cfg)
    {
        return song.GetFormattedName(
            cfg.CaptureRegex,
            cfg.OutputFormat,
            cfg.FindRegex,
            cfg.Replacement);
    }

    /// <summary>
    /// Send currently playing song name to in-game chat (if party leader or not in party).
    /// </summary>
    public void SendSongToChat(int songIndex, Playlist? currentPlaylist)
    {
        if (DalamudApi.PartyList.IsInParty() && !DalamudApi.PartyList.IsPartyLeader())
            return;

        if (currentPlaylist == null || !currentPlaylist.IsValidSongIndex(songIndex))
            return;

        if (_plugin.MidiPlayerControl._status != MidiPlayerControl.MidiPlayerStatus.Paused)
        {
            var songName = GetPostSongName(songIndex, currentPlaylist);
            if (songName == "")
                return;

            var chatCommand = _plugin.Config.PostSong.ChatTarget.ToChatCommand();
            var chatText = $"{chatCommand}{songName}";
            Chat.SendMessage(chatText);
        }
    }

    /// <summary>
    /// Load playback for a song (with optional auto-play and IPC sync).
    /// Returns true if playback loaded successfully, false otherwise.
    /// </summary>
    public async Task<bool> LoadPlaybackAsync(int? index = null, bool startPlaying = false, bool sync = true, Playlist? currentPlaylist = null)
    {
        int? playbackIndex = index;

        if (index is int songIndex)
        {
            // Use index directly
            _songController.SetCurrentSongByIndex(songIndex, currentPlaylist);
            playbackIndex = songIndex;
        }
        else if (_songController.CurrentPlayingSong != null && currentPlaylist != null)
        {
            // Calculate index once from identity reference
            playbackIndex = currentPlaylist.Songs.IndexOf(_songController.CurrentPlayingSong);
        }

        if (sync && playbackIndex >= 0)
        {
            // Sync with other clients via IPC
            _plugin.IpcProvider.LoadPlayback(playbackIndex.Value);
        }

        try
        {
            // Load playback via callback
            if (await _onLoadPlaybackCallback(playbackIndex ?? -1))
            {
                if (startPlaying)
                {
                    _plugin.MidiPlayerControl.DoPlay();
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistUiHelper] Error loading playback");
        }

        return false;
    }

}
