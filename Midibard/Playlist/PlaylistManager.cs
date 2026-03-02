using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Dalamud.Interface.ImGuiNotification;

using Melanchall.DryWetMidi.Core;

using MidiBard.Control.MidiControl;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Playlist;
using MidiBard.Playlist.Services;
using MidiBard.Util;

namespace MidiBard;

internal class PlaylistManager
{
    private Plugin Plugin { get; }
    internal readonly ReadingSettings readingSettings;
    private readonly ISongRepository _songRepository;
    private readonly IPlaylistRepository _playlistRepository;

    // NEW: Service layer dependencies
    private IPlaylistService? _playlistService;
    private IPlaylistSongService? _playlistSongService;
    private ISongService? _songService;
    private IMidiFileService? _midiFileService;

    // Database-based playlist state
    private Playlist.Playlist? _currentPlaylist;
    private List<Song> _currentSongs = new();
    private int _currentSongIndex = -1;
    private int _currentPlaylistId = -1;

    // TODO:refactor compatibility
    public List<SongEntry> FilePathList => _currentSongs.Select((song, index) => new SongEntry
    {
        FilePath = song.FilePath,
        SongLength = song.Duration,
        IsFilePlayed = index < (_currentPlaylist?.Songs.Count ?? 0) && _currentPlaylist.Songs[index].IsPlayed
    }).ToList();

    // Compatibility property for existing code

    public Playlist.Playlist? CurrentPlaylist
    {
        get => _currentPlaylist;
        set
        {
            _currentPlaylist = value;
            Plugin.IpcProvider.LoadPlaylist(_currentPlaylistId);
        }
    }

    public int CurrentSongIndex
    {
        get => _currentSongIndex;
        set => _currentSongIndex = value;
    }

    public PlaylistManager(Plugin plugin)
    {
        Plugin = plugin;
        _songRepository = ServiceContainer.GetService<ISongRepository>();
        _playlistRepository = ServiceContainer.GetService<IPlaylistRepository>();

        // Auto-resolve services from registry
        _playlistService = ServiceContainer.GetService<IPlaylistService>();
        _playlistSongService = ServiceContainer.GetService<IPlaylistSongService>();
        _songService = ServiceContainer.GetService<ISongService>();
        _midiFileService = ServiceContainer.GetService<IMidiFileService>();

        readingSettings = new ReadingSettings
        {
            NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
            NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
            InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.ReadValid,
            InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
            InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
            MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
            UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
            ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read,
            UnknownChunkIdPolicy = UnknownChunkIdPolicy.ReadAsUnknownChunk,
            SilentNoteOnPolicy = SilentNoteOnPolicy.NoteOff,
            TextEncoding = Plugin.Config.UiLanguage == "zh-Hans" || Plugin.Config.UiLanguage == "zh-Hant"
            ? Encoding.GetEncoding("gb18030")
            : Encoding.Default,
            InvalidSystemCommonEventParameterValuePolicy = InvalidSystemCommonEventParameterValuePolicy.SnapToLimits
        };

        DalamudApi.PluginLog.Debug("[PlaylistManager] Services auto-resolved from ServiceContainer");

        // Load last used playlist
        _ = LoadLastPlaylistAsync();
    }

    /// <summary>
    /// Reload the current playlist from database
    /// </summary>
    public async Task ReloadAsync()
    {
        if (_currentPlaylist != null)
        {
            await LoadPlaylistByIdAsync(_currentPlaylist.Id);
        }
    }

    /// <summary>
    /// Load last used playlist from database
    /// </summary>
    private async Task LoadLastPlaylistAsync()
    {
        if (_playlistRepository == null || _songRepository == null)
        {
            // Fallback: create default playlist
            _currentPlaylist = new Playlist.Playlist { Name = "Default" };
            if (_playlistRepository != null)
                await _playlistRepository.CreateAsync(_currentPlaylist);
            if (_currentPlaylist != null)
                _currentPlaylistId = _currentPlaylist.Id;
            return;
        }

        var playlists = await _playlistRepository.GetAllAsync();

        if (playlists.Count == 0)
        {
            // Create default playlist
            _currentPlaylist = new Playlist.Playlist { Name = "Default" };
            await _playlistRepository.CreateAsync(_currentPlaylist);
            _currentPlaylistId = _currentPlaylist.Id;
        }
        else
        {
            // Load first playlist (or implement last used logic)
            await LoadPlaylistByIdAsync(playlists[0].Id);
        }
    }

    /// <summary>
    /// Load playlist by ID from database
    /// </summary>
    public async Task LoadPlaylistByIdAsync(int playlistId)
    {
        DalamudApi.PluginLog.Warning($"LoadPlaylistByIdAsync({playlistId})");

        if (_playlistRepository == null || _songRepository == null) return;

        _currentPlaylist = await _playlistRepository.GetByIdAsync(playlistId);
        if (_currentPlaylist != null)
        {
            _currentPlaylistId = _currentPlaylist.Id;
            await LoadSongsForCurrentPlaylistAsync();
        }
    }

    private async Task LoadSongsForCurrentPlaylistAsync()
    {
        if (_songRepository == null || _currentPlaylist == null) return;

        _currentSongs = new List<Song>();
        var unloadedSongIds = new List<int>();

        // Songs are already ordered by array position, collect those that need to be loaded
        foreach (var ps in _currentPlaylist.Songs)
        {
            var song = ps.Song;
            if (song != null)
            {
                _currentSongs.Add(song);
            }
            else if (ps.Song?.Id > 0)
            {
                unloadedSongIds.Add(ps.Song.Id);
            }
        }

        // Batch load unloaded songs (if any)
        if (unloadedSongIds.Count > 0)
        {
            var unloadedSongs = await _songRepository.GetSongsByIdsAsync(unloadedSongIds);
            if (unloadedSongs.Count > 0)
            {
                _currentSongs.AddRange(unloadedSongs);
            }
        }
    }

    /// <summary>
    /// Switch to a different playlist
    /// </summary>
    public async Task SwitchToPlaylistAsync(int playlistId)
    {
        await LoadPlaylistByIdAsync(playlistId);
        _currentSongIndex = -1;
        Plugin.IpcProvider.LoadPlaylist(_currentPlaylistId);
    }

    /// <summary>
    /// Get all playlists (for UI)
    /// </summary>
    public async Task<List<Playlist.Playlist>> GetAllPlaylistsAsync()
    {
        return await _playlistRepository.GetAllAsync();
    }

    /// <summary>
    /// Get songs for a specific playlist (for UI)
    /// </summary>
    public async Task<List<Song>> GetPlaylistSongsAsync(int playlistId)
    {
        var playlist = await _playlistRepository.GetByIdAsync(playlistId);
        if (playlist == null) return new List<Song>();

        var songs = new List<Song>();
        var unloadedSongIds = new List<int>();

        // Collect songs and track unloaded ones
        foreach (var ps in playlist.Songs)
        {
            var song = ps.Song;
            if (song != null)
            {
                songs.Add(song);
            }
            else if (ps.Song?.Id > 0)
            {
                unloadedSongIds.Add(ps.Song.Id);
            }
        }

        // Batch load unloaded songs (if any)
        if (unloadedSongIds.Count > 0)
        {
            var unloadedSongs = await _songRepository.GetSongsByIdsAsync(unloadedSongIds);
            if (unloadedSongs.Count > 0)
            {
                songs.AddRange(unloadedSongs);
            }
        }

        return songs;
    }

    /// <summary>
    /// Create a new playlist
    /// </summary>
    public async Task<Playlist.Playlist> CreatePlaylistAsync(string name)
    {
        var playlist = new Playlist.Playlist { Name = name };
        await _playlistRepository.CreateAsync(playlist);
        return playlist;
    }

    /// <summary>
    /// Add songs from folder to playlist
    /// </summary>
    public async Task AddSongsFromFolderAsync(int playlistId, string folderPath)
    {
        var playlist = await _playlistRepository.GetByIdAsync(playlistId);
        if (playlist == null) return;

        var midiFiles = Directory.GetFiles(folderPath, "*.mid", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folderPath, "*.midi", SearchOption.AllDirectories))
            .ToList();

        foreach (var filePath in midiFiles)
        {
            try
            {
                var duration = TimeSpan.Zero;
                var midiFile = LoadSongFile(filePath);
                if (midiFile != null)
                {
                    duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                }

                var song = await _songRepository.CreateOrGetSongAsync(
                    filePath,
                    Path.GetFileNameWithoutExtension(filePath),
                    "", // Artist
                    0,  // ReleaseYear
                    duration
                );

                // Set file last modified date directly on the object
                if (File.Exists(filePath))
                {
                    try
                    {
                        song.FileLastModifiedAt = File.GetLastWriteTimeUtc(filePath);
                    }
                    catch { /* ignore */ }
                }

                var order = playlist.Songs.Count;
                await _playlistRepository.AddSongToPlaylistAsync(playlistId, song.Id, order);
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Warning(e, $"Error adding song: {filePath}");
            }
        }
    }


    /// <summary>
    /// Delete a playlist
    /// </summary>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await _playlistRepository.DeleteAsync(playlistId);
    }

    /// <summary>
    /// Update a song
    /// </summary>
    public async Task UpdateSongAsync(Song song)
    {
        await _songRepository.UpdateAsync(song);
    }

    /// <summary>
    /// Update playlist song (PlaylistSong contains Song and IsPlayed)
    /// </summary>
    public async Task UpdatePlaylistSongAsync(PlaylistSong playlistSong, int playlistId)
    {
        if (playlistSong.Song == null) return;

        // Debug logging
        DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistId: {playlistId}");
        DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.Song.Id: {playlistSong.Song.Id}");
        DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.Song.Name: {playlistSong.Song.Name}");
        DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.Song.Artist: {playlistSong.Song.Artist}");
        DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.Song.Rating: {playlistSong.Song.Rating}");
        DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.IsPlayed: {playlistSong.IsPlayed}");

        // Update the Song first
        await _songRepository.UpdateAsync(playlistSong.Song);

        // Then update PlaylistSong - find by playlistId and songId directly
        if (_playlistRepository != null)
        {
            var songId = playlistSong.Song.Id;
            var playlist = await _playlistRepository.GetByIdAsync(playlistId);

            DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlist found: {playlist?.Name}");

            if (playlist?.Songs != null)
            {
                DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlist.Songs.Count: {playlist.Songs.Count}");

                var existingPs = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == songId);
                if (existingPs != null)
                {
                    DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] existingPs found, updating IsPlayed from {existingPs.IsPlayed} to {playlistSong.IsPlayed}");

                    // Update the PlaylistSong in the embedded list
                    existingPs.IsPlayed = playlistSong.IsPlayed;
                    await _playlistRepository.UpdateAsync(playlist);

                    DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlist updated successfully");
                }
                else
                {
                    DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] existingPs NOT FOUND for songId: {songId}");
                }
            }
        }
    }

    /// <summary>
    /// Add tag to a song
    /// </summary>
    public async Task AddTagToSongAsync(int songId, string tag)
    {
        await _songRepository.AddTagAsync(songId, tag);
    }

    /// <summary>
    /// Remove tag from a song
    /// </summary>
    public async Task RemoveTagFromSongAsync(int songId, string tag)
    {
        await _songRepository.RemoveTagAsync(songId, tag);
    }

    /// <summary>
    /// Remove tag from a song by tag ID - more efficient
    /// </summary>
    public async Task RemoveTagFromSongByIdAsync(int songId, int tagId)
    {
        await _songRepository.RemoveTagByIdAsync(songId, tagId);
    }

    /// <summary>
    /// Remove song from playlist
    /// </summary>
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        await _playlistRepository.RemoveSongFromPlaylistAsync(playlistId, songId);
    }

    /// <summary>
    /// Get song by ID
    /// </summary>
    public async Task<Song?> GetSongByIdAsync(int songId)
    {
        return await _songRepository.GetSongByIdAsync(songId);
    }

    /// <summary>
    /// Get playlist by ID
    /// </summary>
    public async Task<Playlist.Playlist?> GetPlaylistByIdAsync(int playlistId)
    {
        return await _playlistRepository.GetByIdAsync(playlistId);
    }

    /// <summary>
    /// Load a playlist as the current playlist (for UI button)
    /// </summary>
    public async Task LoadPlaylistToCurrentAsync(int playlistId)
    {
        if (_playlistRepository == null) return;

        _currentPlaylist = await _playlistRepository.GetByIdAsync(playlistId);
        if (_currentPlaylist != null)
        {
            _currentPlaylistId = _currentPlaylist.Id;
            await LoadSongsForCurrentPlaylistAsync();
            Plugin.IpcProvider.LoadPlaylist(playlistId);
        }
    }

    /// <summary>
    /// Clear all songs from a playlist
    /// </summary>
    public async Task ClearPlaylistAsync(int playlistId)
    {
        if (_playlistRepository == null) return;

        // Use batch delete - clears all songs in a single operation
        await _playlistRepository.RemoveAllSongsAsync(playlistId);

        // If this is the current playlist, reload it
        if (_currentPlaylist?.Id == playlistId)
        {
            await LoadPlaylistByIdAsync(playlistId);
            Plugin.IpcProvider.LoadPlaylist(playlistId);
        }
    }

    // ==================== Compatibility Methods for Sync Calls ====================

    /// <summary>
    /// Compatibility method - calls async version
    /// </summary>
    public void RemoveSync(int songIndex)
    {
        _ = RemoveSongAsync(songIndex);
    }

    /// <summary>
    /// Compatibility method - calls async version
    /// </summary>
    public void MoveSongToIndexSync(int songIndex, int targetIndex)
    {
        _ = MoveSongToIndexAsync(songIndex, targetIndex);
    }

    /// <summary>
    /// Compatibility method - calls async version
    /// </summary>
    public void ChangeSongPlayedStatusSync(int songIndex, bool isFilePlayed)
    {
        _ = ChangeSongPlayedStatusAsync(songIndex, isFilePlayed);
    }

    /// <summary>
    /// Compatibility method - calls async version
    /// </summary>
    public void ResetAllSongsPlayedStatusSync()
    {
        _ = ResetAllSongsPlayedStatusAsync();
    }

    /// <summary>
    /// Compatibility method - load last playlist (was sync, now async)
    /// </summary>
    public void LoadLastPlaylist()
    {
        _ = ReloadAsync();
    }

    public void SortBy<TKey>(Func<SongEntry, TKey>? orderBy = null, bool descending = false) where TKey : IComparable
    {
        if (orderBy == null || _currentPlaylist == null) return;
        if (_currentPlaylist.Songs == null || _currentPlaylist.Songs.Count == 0) return;

        // Get songs for sorting - use FilePathList to match original behavior
        var songs = FilePathList.Select((entry, index) => new { entry, index }).ToList();

        var sorted = descending
            ? songs.OrderBy(x => orderBy(x.entry)).ToList()
            : songs.OrderByDescending(x => orderBy(x.entry)).ToList();

        // Collect all song IDs in the new order - batch operation
        var songIdsInOrder = new List<int>();
        foreach (var item in sorted)
        {
            var oldIndex = item.index;
            if (oldIndex < _currentPlaylist.Songs.Count)
            {
                var ps = _currentPlaylist.Songs[oldIndex];
                var songId = ps.Song?.Id ?? 0;
                if (songId > 0)
                {
                    songIdsInOrder.Add(songId);
                }
            }
        }

        // Batch reorder all songs in a single database operation
        if (songIdsInOrder.Count > 0)
        {
            _ = _playlistRepository.ReorderAllSongsAsync(_currentPlaylist.Id, songIdsInOrder);
        }

        // Reload from database - this will refresh UI with correct order
        _ = LoadPlaylistByIdAsync(_currentPlaylistId);
    }





    public void Clear()
    {
        _currentSongs.Clear();
        _currentSongIndex = -1;
        Plugin.IpcProvider.LoadPlaylist(_currentPlaylistId);
    }

    public async Task RemoveSongAsync(int songIndex)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(songIndex))
            return;

        var pmdUseChatPlaylistSync = Plugin.Config.playOnMultipleDevices && Plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            Plugin.PartyChatCommand.SendRemoveSong(songIndex);
            return;
        }

        try
        {
            // 1. Local modification
            if (!_currentPlaylist.RemoveSongAt(songIndex))
                return;

            // Adjust current index if needed
            if (_currentSongIndex >= _currentPlaylist.Songs.Count)
                _currentSongIndex = Math.Max(-1, _currentPlaylist.Songs.Count - 1);

            // 2. Persist via service
            if (_playlistSongService == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistManager] PlaylistSongService not initialized");
                return;
            }

            var success = await _playlistSongService.RemoveSongAsync(_currentPlaylist.Id, songIndex);
            if (!success)
            {
                DalamudApi.PluginLog.Error("[PlaylistManager] Failed to persist song removal");
                return;
            }

            // 3. Notify other clients
            Plugin.IpcProvider.RemoveTrackIndex(songIndex);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistManager] Error removing song at index {SongIndex}", songIndex);
        }
    }

    public Task RemoveLocal(int songIndex)
    {
        // Deprecated: Use RemoveSongAsync instead
        return RemoveSongAsync(songIndex);
    }

    internal void CalculateCurrentSongIndexAfterReorder(int songIndex, int targetIndex)
    {
        if (_currentSongIndex == -1) return;
        if (songIndex == _currentSongIndex)
        {
            _currentSongIndex = targetIndex;
        }
        else if (songIndex < _currentSongIndex && targetIndex >= _currentSongIndex)
        {
            _currentSongIndex--;
        }
        else if (songIndex > _currentSongIndex && targetIndex <= _currentSongIndex)
        {
            _currentSongIndex++;
        }
    }

    public async Task MoveSongToIndexAsync(int songIndex, int targetIndex)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(songIndex))
            return;

        if (songIndex == targetIndex)
            return;

        var pmdUseChatPlaylistSync = Plugin.Config.playOnMultipleDevices && Plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            Plugin.PartyChatCommand.SendChangeSongOrder(songIndex, targetIndex);
            return;
        }

        try
        {
            // Clamp target index
            targetIndex = Math.Clamp(targetIndex, 0, _currentPlaylist.Songs.Count - 1);

            // 1. Local modification
            if (!_currentPlaylist.MoveSongToIndex(songIndex, targetIndex))
                return;

            // Update current song index if needed
            CalculateCurrentSongIndexAfterReorder(songIndex, targetIndex);

            // 2. Persist via service
            if (_playlistSongService == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistManager] PlaylistSongService not initialized");
                return;
            }

            var success = await _playlistSongService.ReorderSongAsync(_currentPlaylist.Id, songIndex, targetIndex);
            if (!success)
            {
                DalamudApi.PluginLog.Error("[PlaylistManager] Failed to persist song reorder");
                return;
            }

            // 3. Notify other clients
            Plugin.IpcProvider.MoveSongToIndex(songIndex, targetIndex);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistManager] Error moving song from {From} to {To}", songIndex, targetIndex);
        }
    }

    public Task MoveSongToIndexLocal(int songIndex, int targetIndex)
    {
        // Deprecated: Use MoveSongToIndexAsync instead
        return MoveSongToIndexAsync(songIndex, targetIndex);
    }

    public void SetCurrentSongAsPlayed()
    {
        if (Plugin.CurrentBardPlayback.IsLoaded)
        {
            var progress = Plugin.CurrentBardPlayback.GetPlaybackProgress();
            var playedThresholdPercent = 0.85;
            if (progress >= playedThresholdPercent)
            {
                _ = ChangeSongPlayedStatusAsync(_currentSongIndex, true);
            }
        }
    }

    public async Task ChangeSongPlayedStatusAsync(int songIndex, bool isFilePlayed)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(songIndex))
            return;

        try
        {
            // 1. Local modification
            if (!_currentPlaylist.MarkSongAsPlayed(songIndex))
                return;

            // 2. Persist via service
            if (_playlistSongService == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistManager] PlaylistSongService not initialized");
                return;
            }

            var success = await _playlistSongService.MarkSongAsPlayedAsync(_currentPlaylist.Id, songIndex);
            if (!success)
            {
                DalamudApi.PluginLog.Error("[PlaylistManager] Failed to persist song played status");
                return;
            }

            // 3. Notify other clients
            Plugin.IpcProvider.ChangeSongPlayedStatus(songIndex, isFilePlayed);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistManager] Error changing song played status at index {SongIndex}", songIndex);
        }
    }

    public Task ChangeSongPlayedStatusLocal(int songIndex, bool isSongPlayed)
    {
        // Deprecated: Use ChangeSongPlayedStatusAsync instead
        return ChangeSongPlayedStatusAsync(songIndex, isSongPlayed);
    }

    public async Task ResetAllSongsPlayedStatusAsync()
    {
        if (_currentPlaylist == null || _currentPlaylist.Songs.Count == 0)
            return;

        try
        {
            // 1. Local modification
            _currentPlaylist.ResetAllSongsPlayedStatus();

            // 2. Persist via service
            if (_playlistSongService == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistManager] PlaylistSongService not initialized");
                return;
            }

            var success = await _playlistSongService.ResetAllSongsPlayedStatusAsync(_currentPlaylist.Id);
            if (!success)
            {
                DalamudApi.PluginLog.Error("[PlaylistManager] Failed to persist reset played status");
                return;
            }

            // 3. Notify other clients
            Plugin.IpcProvider.ResetAllSongsPlayedStatus();
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistManager] Error resetting all songs played status");
        }
    }

    public async Task ResetAllSongsPlayedStatusDbAsync()
    {
        try
        {
            // Batch reset IsPlayed for all songs in the current playlist
            await _playlistRepository.ResetAllSongsPlayedStatusAsync(_currentPlaylist.Id);

        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "error when resetting played status for playlist [{0}]", _currentPlaylist.Id);
        }
    }

    /// <summary>
    /// Sync song file data - validates file path and recalculates duration
    /// </summary>
    public async Task SyncSongFileDataAsync(Song song)
    {
        if (song == null) return;

        bool updated = false;

        // Validate file path
        var hasValidFilePath = !string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath);
        if (song.HasValidFilePath != hasValidFilePath)
        {
            song.HasValidFilePath = hasValidFilePath;
            updated = true;
        }

        // Update file last modified date if file exists
        if (hasValidFilePath)
        {
            try
            {
                var fileLastModified = File.GetLastWriteTimeUtc(song.FilePath);
                if (song.FileLastModifiedAt != fileLastModified)
                {
                    song.FileLastModifiedAt = fileLastModified;
                    updated = true;
                }
            }
            catch
            {
                // Ignore errors getting file date
            }
        }

        // Recalculate duration if file exists
        if (hasValidFilePath)
        {
            try
            {
                var midiFile = LoadSongFile(song.FilePath);
                if (midiFile != null)
                {
                    var newDuration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                    if (song.Duration != newDuration)
                    {
                        song.Duration = newDuration;
                        updated = true;
                    }
                }
            }
            catch
            {
                // If we can't read the file, mark as invalid
                if (song.HasValidFilePath)
                {
                    song.HasValidFilePath = false;
                    updated = true;
                }
            }
        }

        if (updated)
        {
            await _songRepository.UpdateAsync(song);
        }
    }


    public void ResetAllSongsPlayedStatusLocal()
    {
        // Update in-memory playlist state if available
        if (_currentPlaylist.Songs != null)
        {
            foreach (var ps in _currentPlaylist.Songs)
            {
                if (ps.IsPlayed)
                    ps.IsPlayed = false;
            }
        }
    }

    public async Task AddAsync(IEnumerable<string> filePaths)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
        {
            DalamudApi.PluginLog.Warning("[PlaylistManager] Cannot add songs - current playlist is invalid");
            return;
        }

        var success = 0;
        var sw = Stopwatch.StartNew();

        if (_playlistSongService == null || _songService == null || _midiFileService == null)
        {
            DalamudApi.PluginLog.Warning("[PlaylistManager] Services not initialized");
            return;
        }

        await Task.Run(async () =>
        {
            foreach (var (file, path) in CheckValidFiles(filePaths))
            {
                try
                {
                    var songLength = file.GetDurationTimeSpan() ?? TimeSpan.Zero;

                    // 1. Create or get song via service
                    var song = await _songService.GetOrCreateFromFileAsync(
                        path,
                        Path.GetFileNameWithoutExtension(path),
                        "", // Artist
                        0,  // ReleaseYear
                        songLength
                    );

                    if (song == null)
                    {
                        DalamudApi.PluginLog.Warning("[PlaylistManager] Failed to create/get song from file {FilePath}", path);
                        continue;
                    }

                    // 2. Add to current playlist (local + DB)
                    var playlistSong = new PlaylistSong
                    {
                        Song = song,
                        IsPlayed = false,
                        AddedAt = DateTime.UtcNow
                    };

                    _currentPlaylist.AddSong(playlistSong);

                    var addSuccess = await _playlistSongService.AddSongAsync(_currentPlaylist.Id, song);
                    if (!addSuccess)
                    {
                        DalamudApi.PluginLog.Warning("[PlaylistManager] Failed to add song to playlist {FilePath}", path);
                        // Remove from local state if DB failed
                        _currentPlaylist.RemoveSongAt(_currentPlaylist.Songs.Count - 1);
                        continue;
                    }

                    success++;
                }
                catch (Exception ex)
                {
                    DalamudApi.PluginLog.Warning(ex, "[PlaylistManager] Error when adding song");
                }
            }

            // 3. Calculate durations in parallel for songs that don't have them
            var songsToCalc = _currentPlaylist.Songs
                .Where(ps => ps.Song != null && ps.Song.Duration == default)
                .Select(ps => ps.Song!)
                .ToList();

            if (songsToCalc.Count > 0)
            {
                await _midiFileService.CalculateAllDurationsAsync(songsToCalc);
            }
        });

        // 4. Notify other clients
        Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);
        DalamudApi.PluginLog.Information("[PlaylistManager] File import complete in {Elapsed} ms! success: {Success}",
            sw.Elapsed.TotalMilliseconds, success);
    }



    internal void CalculateDurationAll()
    {
        // DEPRECATED: This function is no longer used. Use IMidiFileService.CalculateAllDurationsAsync instead.
        // Kept for backward compatibility only.
        DalamudApi.PluginLog.Warning("[PlaylistManager] CalculateDurationAll is deprecated - use IMidiFileService instead");
    }

    internal void CalculateSongDuration(int songIndex)
    {
        if (!IsValidSongIndex(songIndex)) return;

        try
        {
            var song = _currentSongs[songIndex];

            if (!File.Exists(song.FilePath))
            {
                _ = RemoveSongAsync(songIndex);
                ImGuiUtil.AddNotification(NotificationType.Warning, $"The song file no longer exists and has been removed from the playlist");
                return;
            }

            var midiFile = LoadSongFile(song.FilePath);
            if (midiFile != null)
            {
                song.Duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                _ = _songRepository.UpdateAsync(song);
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, $"error when getting {_currentSongs[songIndex].FilePath} duration");
        }
    }

    internal bool IsValidSongIndex(int songIndex)
    {
        var isEmptyList = _currentSongs == null || _currentSongs.Count == 0;
        var isInvalidIndex = songIndex < 0 || songIndex >= (_currentSongs?.Count ?? 0);

        if (isEmptyList || isInvalidIndex)
            return false;

        return true;
    }

    private IEnumerable<(MidiFile, string)> CheckValidFiles(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            MidiFile? file = null;

            file = LoadSongFile(path);
            if (file is not null) yield return (file, path);
        }
    }

    internal MidiFile? LoadSongFile(string path)
    {
        if (Path.GetExtension(path).Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".midi", StringComparison.OrdinalIgnoreCase))
            return LoadMidiFile(path);
        return null;
    }

    private MidiFile? LoadMidiFile(string filePath)
    {
        DalamudApi.PluginLog.Debug($"[LoadMidiFile] -> {filePath} START");
        MidiFile? loaded = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!File.Exists(filePath))
            {
                DalamudApi.PluginLog.Warning($"File not exist! path: {filePath}");
                return null;
            }

            using (var f = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                loaded = MidiFile.Read(f, readingSettings);
            }

            DalamudApi.PluginLog.Debug($"[LoadMidiFile] -> {filePath} OK! in {stopwatch.Elapsed.TotalMilliseconds} ms");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Warning(ex, "Failed to load file at {0}", filePath);
        }

        return loaded;
    }

    public MidiFile? LoadMidiFile(Stream midi)
    {
        DalamudApi.PluginLog.Debug($"[LoadMidiFile] -> START");
        MidiFile? loaded = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (midi == null)
            {
                DalamudApi.PluginLog.Warning($"Stream was empty");
                return null;
            }

            loaded = MidiFile.Read(midi, readingSettings);

            DalamudApi.PluginLog.Debug($"[LoadMidiFile] -> OK! in {stopwatch.Elapsed.TotalMilliseconds} ms");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Warning(ex, "Failed to load from stream.");
        }

        return loaded;
    }

    public async Task<bool> LoadPlayback(int? index = null, bool startPlaying = false, bool sync = true)
    {
        if (index is int songIndex)
        {
            _currentSongIndex = songIndex;
        }

        if (sync)
        {
            Plugin.IpcProvider.LoadPlayback(_currentSongIndex);
        }

        if (await LoadPlaybackPrivate())
        {
            if (startPlaying)
            {
                Plugin.MidiPlayerControl.DoPlay();
            }

            return true;
        }

        return false;
    }

    public static string ExtractSongName(string input, string capturePattern, string capturedOutputReplacement, string findPattern, string replacement)
    {
        if (string.IsNullOrEmpty(capturePattern) || string.IsNullOrEmpty(capturedOutputReplacement))
            return input;

        try
        {
            return Regex.Replace(input, capturePattern, match =>
            {
                string result = capturedOutputReplacement;

                for (int i = match.Groups.Count - 1; i >= 1; i--)
                {
                    result = result.Replace($"${i}", match.Groups[i].Value);
                }

                result = Regex.Replace(result, @"\$\d+", "");

                if (!string.IsNullOrEmpty(findPattern))
                {
                    result = Regex.Replace(result, findPattern, replacement);
                }

                return result;
            });
        }
        catch
        {
            return input;
        }
    }

    public string GetPostSongName(int songIndex)
    {
        if (!IsValidSongIndex(songIndex))
        {
            return string.Empty;
        }

        var song = _currentSongs[songIndex];
        var songName = ExtractSongName(
            song.Name ?? Path.GetFileName(song.FilePath),
            Plugin.Config.postSongNameCaptureRegex,
            Plugin.Config.postSongNameCaptureOutputFormat,
            Plugin.Config.postSongNameFindRegex,
            Plugin.Config.postSongNameReplacement);

        return songName;
    }

    public int FindSongIndex(string songName)
    {
        if (string.IsNullOrWhiteSpace(songName))
            return -1;

        return _currentSongs.FindIndex(f =>
            (f.Name ?? Path.GetFileName(f.FilePath)).Contains(songName, StringComparison.OrdinalIgnoreCase)
        );
    }

    public void SendSongToChat(int songIndex)
    {
        if (DalamudApi.PartyList.IsInParty() && !DalamudApi.PartyList.IsPartyLeader()) return;
        if (!IsValidSongIndex(songIndex)) return;

        if (Plugin.MidiPlayerControl._status != MidiPlayerControl.MidiPlayerStatus.Paused)
        {
            var songName = GetPostSongName(songIndex);
            if (songName == "") return;

            var chatComand = Plugin.Config.GetChatCommand(Plugin.Config.SongNameChatTarget);

            var chatText = $"{chatComand}{songName}";
            Chat.SendMessage(chatText);
        }
    }

    private async Task<bool> LoadPlaybackPrivate()
    {
        try
        {
            if (!IsValidSongIndex(_currentSongIndex)) return false;

            var song = _currentSongs[_currentSongIndex];
            return await Plugin.FilePlayback.LoadPlayback(song.FilePath);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString());
            return false;
        }
    }
}
