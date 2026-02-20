using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Newtonsoft.Json;

namespace MidiBard.Playlist;

public class JsonPlaylistRepository : IPlaylistRepository
{
    private const string PlaylistDirectory = "playlists";
    private int _nextPlaylistId = 1;
    private int _nextSongId = 1;

    public Task<Playlist?> GetByIdAsync(int id)
    {
        throw new NotImplementedException();
    }

    public async Task<Playlist?> GetByFilePathAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var root = JsonConvert.DeserializeObject<PlaylistJson>(json);

            if (root == null)
                return null;

            var songs = root.Songs?.Select(s => new Song
            {
                Id = s.Id ?? 0,
                FilePath = ResolveRelativePath(filePath, s.FilePath),
                SongDuration = ParseTimeSpan(s.SongLength),
                IsSongPlayed = s.IsFilePlayed,
                CreatedAt = s.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = s.UpdatedAt ?? DateTime.UtcNow,
                LastPlayedAt = s.LastPlayedAt,
                PlayCount = s.PlayCount,
                Tags = s.Tags ?? new List<string>(),
                Rate = s.Rate
            }).ToList() ?? new List<Song>();

            // Update next IDs if needed
            if (songs.Any())
            {
                var maxSongId = songs.Max(s => s.Id);
                if (maxSongId >= _nextSongId)
                    _nextSongId = maxSongId + 1;
            }

            var playlist = new Playlist
            {
                Id = root.Id ?? 0,
                Name = root.PlaylistName ?? Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                CurrentSongIndex = root.CurrentSongIndex,
                CreatedAt = root.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = root.UpdatedAt ?? DateTime.UtcNow,
                Songs = songs
            };

            if (playlist.Id >= _nextPlaylistId)
                _nextPlaylistId = playlist.Id + 1;

            return playlist;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Warning(ex, "Error loading playlist from {0}", filePath);
            return null;
        }
    }

    public Task<List<Playlist>> GetAllAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<Playlist> CreateAsync(Playlist playlist)
    {
        if (string.IsNullOrEmpty(playlist.FilePath))
        {
            var directory = Path.Combine(
                DalamudApi.PluginInterface.GetPluginConfigDirectory(),
                PlaylistDirectory);
            Directory.CreateDirectory(directory);
            playlist.FilePath = Path.Combine(directory, $"{playlist.Name}.json");
        }

        // Assign ID if not set
        if (playlist.Id == 0)
        {
            playlist.Id = _nextPlaylistId++;
        }

        // Assign IDs to songs if not set
        foreach (var song in playlist.Songs)
        {
            if (song.Id == 0)
            {
                song.Id = _nextSongId++;
            }
        }

        await SaveAsync(playlist);
        return playlist;
    }

    public async Task UpdateAsync(Playlist playlist)
    {
        playlist.UpdatedAt = DateTime.UtcNow;
        await SaveAsync(playlist);
    }

    public Task DeleteAsync(int id)
    {
        throw new NotImplementedException();
    }

    public Task<Song> AddSongAsync(int playlistId, Song song)
    {
        throw new NotImplementedException();
    }

    public Task RemoveSongAsync(int playlistId, int songId)
    {
        throw new NotImplementedException();
    }

    public Task UpdateSongAsync(int playlistId, Song song)
    {
        throw new NotImplementedException();
    }

    public Task ReorderSongAsync(int playlistId, int fromIndex, int toIndex)
    {
        throw new NotImplementedException();
    }

    public Task MarkSongAsPlayedAsync(int playlistId, int songId)
    {
        throw new NotImplementedException();
    }

    public Task IncrementPlayCountAsync(int playlistId, int songId)
    {
        throw new NotImplementedException();
    }

    public Task SetSongRatingAsync(int playlistId, int songId, double rate)
    {
        throw new NotImplementedException();
    }

    public Task AddSongTagAsync(int playlistId, int songId, string tag)
    {
        throw new NotImplementedException();
    }

    public Task RemoveSongTagAsync(int playlistId, int songId, string tag)
    {
        throw new NotImplementedException();
    }

    private async Task SaveAsync(Playlist playlist)
    {
        try
        {
            var playlistJson = new PlaylistJson
            {
                Id = playlist.Id,
                PlaylistName = playlist.Name,
                CurrentSongIndex = playlist.CurrentSongIndex,
                CreatedAt = playlist.CreatedAt,
                UpdatedAt = playlist.UpdatedAt,
                Songs = playlist.Songs.Select(s => new SongJson
                {
                    Id = s.Id,
                    FilePath = GetRelativePath(playlist.FilePath, s.FilePath),
                    SongLength = s.SongDuration.ToString(),
                    IsFilePlayed = s.IsSongPlayed,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt,
                    LastPlayedAt = s.LastPlayedAt,
                    PlayCount = s.PlayCount,
                    Tags = s.Tags,
                    Rate = s.Rate
                }).ToList()
            };

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };

            var json = JsonConvert.SerializeObject(playlistJson, settings);
            await File.WriteAllTextAsync(playlist.FilePath, json);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Warning(ex, "Error saving playlist to {0}", playlist.FilePath);
        }
    }

    private static string ResolveRelativePath(string basePath, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return string.Empty;

        var directory = Path.GetDirectoryName(basePath);
        return Path.GetFullPath(Path.Combine(directory ?? "", relativePath));
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return string.Empty;

        var directory = Path.GetDirectoryName(basePath);
        return Path.GetRelativePath(directory ?? "", fullPath);
    }

    private static TimeSpan ParseTimeSpan(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return TimeSpan.Zero;

        return TimeSpan.TryParse(value, out var result) ? result : TimeSpan.Zero;
    }

    private class PlaylistJson
    {
        public int? Id { get; set; }
        public string? PlaylistName { get; set; }
        public TimeSpan PlaylistTotalDuration { get; set; }
        public int CurrentSongIndex { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<SongJson>? Songs { get; set; }
    }

    private class SongJson
    {
        public int? Id { get; set; }
        public string? FilePath { get; set; }
        public string? SongLength { get; set; }
        public bool IsFilePlayed { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastPlayedAt { get; set; }
        public int PlayCount { get; set; }
        public List<string>? Tags { get; set; }
        public double Rate { get; set; }
    }
}
