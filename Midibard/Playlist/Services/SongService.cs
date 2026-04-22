using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for managing songs globally.
/// </summary>
public class SongService : ISongService
{
    private readonly ISongRepository _songRepository;
    private readonly IPlaylistRepository _playlistRepository;
    private readonly IMidiFileService _midiFileService;

    public SongService(
        ISongRepository songRepository,
        IPlaylistRepository playlistRepository,
        IMidiFileService midiFileService)
    {
        ArgumentNullException.ThrowIfNull(songRepository);
        ArgumentNullException.ThrowIfNull(playlistRepository);
        ArgumentNullException.ThrowIfNull(midiFileService);

        _songRepository = songRepository;
        _playlistRepository = playlistRepository;
        _midiFileService = midiFileService;
    }

    public async Task<Song?> GetByIdAsync(int id)
    {
        try
        {
            var song = await _songRepository.GetSongByIdAsync(id);
            if (song == null)
            {
                DalamudApi.PluginLog.Warning($"[SongService] Song {id} not found");
                return null;
            }
            return song;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error getting song {id}");
            return null;
        }
    }

    public async Task<Song?> GetOrCreateFromFileAsync(string filePath, string name, string artist, int releaseYear, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            DalamudApi.PluginLog.Warning("[SongService] Cannot create song with empty file path");
            return null;
        }

        try
        {
            var fileExists = File.Exists(filePath);
            DateTime fileLastModifiedAt = default;
            if (fileExists)
            {
                try { fileLastModifiedAt = File.GetLastWriteTime(filePath); }
                catch { /* leave as default */ }
            }

            var song = await _songRepository.CreateOrGetSongAsync(
                filePath, name, artist, releaseYear, duration,
                isValid: fileExists,
                fileLastModifiedAt: fileLastModifiedAt);

            DalamudApi.PluginLog.Debug($"[SongService] Song {filePath}: created or retrieved");
            return song;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error creating/getting song from file {filePath}");
            return null;
        }
    }

    public async Task<bool> UpdateAsync(Song song)
    {
        if (song == null)
        {
            DalamudApi.PluginLog.Warning("[SongService] Cannot update null song");
            return false;
        }

        try
        {
            await _songRepository.UpdateAsync(song);
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error updating song {song.Id}");
            return false;
        }
    }

    public async Task<bool> RecordPlayAsync(int songId)
    {
        try
        {
            await _songRepository.IncrementPlayCountAsync(songId);
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error recording play for song {songId}");
            return false;
        }
    }

    public async Task<bool> SetRatingAsync(int songId, int rating)
    {
        try
        {
            await _songRepository.SetRatingAsync(songId, rating);
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error setting rating for song {songId}");
            return false;
        }
    }

    public async Task<bool> AddTagAsync(int songId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            DalamudApi.PluginLog.Warning("[SongService] Cannot add empty tag");
            return false;
        }

        try
        {
            await _songRepository.AddTagAsync(songId, tagName);
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error adding tag to song {songId}");
            return false;
        }
    }

    public async Task<bool> RemoveTagAsync(int songId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            DalamudApi.PluginLog.Warning("[SongService] Cannot remove empty tag");
            return false;
        }

        try
        {
            await _songRepository.RemoveTagAsync(songId, tagName);
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error removing tag from song {songId}");
            return false;
        }
    }

    public async Task<bool> ValidateFileAsync(int songId)
    {
        try
        {
            var song = await GetByIdAsync(songId);
            if (song == null)
                return false;

            song.ValidateFile();
            return await UpdateAsync(song);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error validating file for song {songId}");
            return false;
        }
    }

    public async Task<List<Song>> GetByIdsAsync(IEnumerable<int> songIds)
    {
        try
        {
            return await _songRepository.GetSongsByIdsAsync(songIds);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[SongService] Error getting songs by ids");
            return new List<Song>();
        }
    }

    public async Task<TimeSpan> GetOrCalculateDurationAsync(int songId)
    {
        try
        {
            var song = await GetByIdAsync(songId);
            if (song == null)
                return TimeSpan.Zero;

            if (song.Duration > TimeSpan.Zero)
                return song.Duration;

            var duration = await _midiFileService.CalculateDurationFromFileAsync(song.FilePath);
            if (duration > TimeSpan.Zero)
            {
                song.Duration = duration;
                await UpdateAsync(song);
            }

            return duration;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error calculating duration for song {songId}");
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Delete a song with cascading cleanup:
    /// 1. Removes song from all playlists (PlaylistSongs)
    /// 2. Removes song from database
    /// </summary>
    public async Task<bool> DeleteAsync(int songId)
    {
        try
        {
            // Step 1: Remove this song from all playlists
            var playlists = await _playlistRepository.GetAllAsync();
            var playlistsAffected = 0;

            foreach (var playlist in playlists)
            {
                if (playlist.Songs == null || playlist.Songs.Count == 0)
                    continue;

                var playlistSongsToRemove = playlist.Songs
                    .Where(ps => ps.Song?.Id == songId)
                    .ToList();

                if (playlistSongsToRemove.Any())
                {
                    playlistsAffected++;
                    foreach (var ps in playlistSongsToRemove)
                    {
                        playlist.Songs.Remove(ps);
                    }
                    playlist.UpdatedAt = DateTime.UtcNow;
                    await _playlistRepository.UpdateAsync(playlist);
                }
            }

            // Step 2: Delete the song
            await _songRepository.DeleteAsync(songId);

            DalamudApi.PluginLog.Information($"[SongService] Deleted song {songId} (removed from {playlistsAffected} playlists)");

            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongService] Error deleting song {songId}");
            return false;
        }
    }

    public async Task<int> BulkReplaceFilePathPrefixAsync(string oldPrefix, string newPrefix)
    {
        var songs = await _songRepository.BulkReplaceFilePathPrefixAsync(oldPrefix, newPrefix);

        foreach (var song in songs)
        {
            song.IsValid = File.Exists(song.FilePath);
            if (song.IsValid)
                song.FileLastModifiedAt = File.GetLastWriteTime(song.FilePath!);
            song.UpdatedAt = DateTime.UtcNow;
        }

        await _songRepository.BulkUpdateAsync(songs);

        DalamudApi.PluginLog.Information($"[SongService] Bulk replaced prefix '{oldPrefix}' -> '{newPrefix}' on {songs.Count} songs");
        return songs.Count;
    }

    public Task<int> BulkUpdateAsync(IEnumerable<Song> songs)
    {
        return _songRepository.BulkUpdateAsync(songs);
    }
}
