using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MidiBard.Playlist;

/// <summary>
/// Implementation of IPlaylistSongEditService.
/// Manages composite edits of Playlist + PlaylistSong + Song as unified entity.
/// Used by PlaylistWindow for unified editing of songs within a specific playlist.
/// </summary>
public class PlaylistSongEditService : IPlaylistSongEditService
{
    private readonly IPlaylistRepository _playlistRepository;
    private readonly ISongRepository _songRepository;
    private readonly IGlobalSongService _globalSongService;

    public PlaylistSongEditService(
        IPlaylistRepository playlistRepository,
        ISongRepository songRepository,
        IGlobalSongService globalSongService)
    {
        _playlistRepository = playlistRepository ?? throw new ArgumentNullException(nameof(playlistRepository));
        _songRepository = songRepository ?? throw new ArgumentNullException(nameof(songRepository));
        _globalSongService = globalSongService ?? throw new ArgumentNullException(nameof(globalSongService));
    }

    public async Task<PlaylistSongEditComposite?> GetCompositeAsync(int playlistId, int songId)
    {
        try
        {
            // Load playlist with all songs
            var playlist = await _playlistRepository.GetByIdAsync(playlistId);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistSongEditService] Playlist {PlaylistId} not found", playlistId);
                return null;
            }

            // Find the specific PlaylistSong within the playlist
            var playlistSong = playlist.Songs?.FirstOrDefault(ps => ps.Song?.Id == songId);
            if (playlistSong == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistSongEditService] Song {SongId} not found in playlist {PlaylistId}", songId, playlistId);
                return null;
            }

            // Load full song details (with tags)
            var song = await _songRepository.GetSongByIdAsync(songId);
            if (song == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistSongEditService] Song {SongId} not found in repository", songId);
                return null;
            }

            return new PlaylistSongEditComposite
            {
                Playlist = playlist,
                PlaylistSong = playlistSong,
                Song = song
            };
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistSongEditService] Error loading composite for song {SongId}", songId);
            return null;
        }
    }

    public async Task<bool> UpdateCompositeAsync(PlaylistSongEditComposite composite)
    {
        if (composite == null || !composite.IsValid)
        {
            DalamudApi.PluginLog.Warning("[PlaylistSongEditService] Invalid composite entity");
            return false;
        }

        try
        {
            // Step 1: Update global song metadata (Name, Artist, Rating, etc.)
            // This updates the song.Song entity properties set by the form
            await _globalSongService.UpdateMetadataAsync(composite.Song);

            // Step 2: Update playlist-song state (IsPlayed, AddedAt)
            // These are attributes of the song IN THIS SPECIFIC PLAYLIST
            // Note: playlistSong reference already modified by caller, we just persist
            if (composite.PlaylistSong != null)
            {
                // PlaylistSong updates through the playlist save below
            }

            // Step 3: Save the playlist with updated PlaylistSong state
            composite.Playlist.UpdatedAt = DateTime.UtcNow;
            await _playlistRepository.UpdateAsync(composite.Playlist);

            DalamudApi.PluginLog.Information(
                "[PlaylistSongEditService] Updated composite: Song {SongId} in Playlist {PlaylistId}",
                composite.Song.Id,
                composite.Playlist.Id);

            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistSongEditService] Error updating composite");
            return false;
        }
    }

    public async Task<Dictionary<int, PlaylistSong>> GetPlaylistSongMapAsync(int playlistId)
    {
        try
        {
            var playlist = await _playlistRepository.GetByIdAsync(playlistId);
            if (playlist?.Songs == null || playlist.Songs.Count == 0)
            {
                return new();
            }

            // Create O(1) lookup map keyed by Song.Id
            return playlist.Songs
                .Where(ps => ps.Song != null)
                .ToDictionary(ps => ps.Song!.Id, ps => ps);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistSongEditService] Error creating playlist song map for playlist {PlaylistId}", playlistId);
            return new();
        }
    }
}
