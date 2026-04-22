using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for exporting song and playlist data to CSV or JSON.
/// </summary>
public class PlaylistExportService : IPlaylistExportService
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

    public async Task<bool> ExportSongsToCsvAsync(IList<Song> songs, string filePath, ExportOptions options)
    {
        if (songs == null || string.IsNullOrWhiteSpace(filePath))
            return false;

        return await Task.Run(() =>
        {
            try
            {
                using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
                writer.WriteLine(BuildCsvHeader(options, isPlaylist: false));
                for (int i = 0; i < songs.Count; i++)
                    writer.WriteLine(BuildCsvSongRow(songs[i], null, null, options, isPlaylist: false));

                DalamudApi.PluginLog.Information($"[PlaylistExportService] Songs exported to CSV: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[PlaylistExportService] Error exporting songs to CSV: {filePath}");
                return false;
            }
        });
    }

    public async Task<bool> ExportSongsToJsonAsync(IList<Song> songs, string filePath, ExportOptions options)
    {
        if (songs == null || string.IsNullOrWhiteSpace(filePath))
            return false;

        return await Task.Run(() =>
        {
            try
            {
                var rows = songs.Select(song =>
                    BuildJsonSongRow(song, null, null, options, isPlaylist: false)).ToList();

                var root = new { songCount = songs.Count, songs = rows };
                var json = JsonSerializer.Serialize(root, _jsonSerializerOptions);
                File.WriteAllText(filePath, json, Encoding.UTF8);

                DalamudApi.PluginLog.Information($"[PlaylistExportService] Songs exported to JSON: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[PlaylistExportService] Error exporting songs to JSON: {filePath}");
                return false;
            }
        });
    }

    public async Task<bool> ExportPlaylistSongsToCsvAsync(
        string playlistName, IList<Song> songs, IDictionary<int, PlaylistSong> songLookup,
        string filePath, ExportOptions options)
    {
        if (songs == null || string.IsNullOrWhiteSpace(filePath))
            return false;

        return await Task.Run(() =>
        {
            try
            {
                using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
                writer.WriteLine(BuildCsvHeader(options, isPlaylist: true));
                for (int i = 0; i < songs.Count; i++)
                {
                    songLookup.TryGetValue(songs[i].Id, out var ps);
                    writer.WriteLine(BuildCsvSongRow(songs[i], playlistName, ps, options, isPlaylist: true));
                }

                DalamudApi.PluginLog.Information($"[PlaylistExportService] Playlist exported to CSV: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[PlaylistExportService] Error exporting playlist to CSV: {filePath}");
                return false;
            }
        });
    }

    public async Task<bool> ExportPlaylistSongsToJsonAsync(
        string playlistName, IList<Song> songs, IDictionary<int, PlaylistSong> songLookup,
        string filePath, ExportOptions options)
    {
        if (songs == null || string.IsNullOrWhiteSpace(filePath))
            return false;

        return await Task.Run(() =>
        {
            try
            {
                var rows = songs.Select(song =>
                {
                    songLookup.TryGetValue(song.Id, out var ps);
                    return BuildJsonSongRow(song, playlistName, ps, options, isPlaylist: true);
                }).ToList();

                var root = new { playlistName, songCount = songs.Count, songs = rows };
                var json = JsonSerializer.Serialize(root, _jsonSerializerOptions);
                File.WriteAllText(filePath, json, Encoding.UTF8);

                DalamudApi.PluginLog.Information($"[PlaylistExportService] Playlist exported to JSON: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[PlaylistExportService] Error exporting playlist to JSON: {filePath}");
                return false;
            }
        });
    }

    // Build helpers

    private static string BuildCsvHeader(ExportOptions options, bool isPlaylist)
    {
        var cols = new List<string>();
        if (isPlaylist && options.IncludePlaylistName) cols.Add("Playlist Name");
        if (options.IncludeName) cols.Add("Song Name");
        if (options.IncludeArtist) cols.Add("Artist");
        if (options.IncludeDuration) cols.Add("Duration");
        if (options.IncludeReleaseYear) cols.Add("Release Year");
        if (options.IncludeRating) cols.Add("Rating");
        if (options.IncludeLastPlayedAt) cols.Add("Last Played");
        if (options.IncludeFileLastModifiedAt) cols.Add("File Modified");
        if (options.IncludeFilePath) cols.Add("File Path");
        if (options.IncludeTags) cols.Add("Tags");
        if (options.IncludeComments) cols.Add("Comments");
        if (isPlaylist && options.IncludeIsPlayed) cols.Add("Is Played");
        return string.Join(",", cols.Select(EscapeCsvField));
    }

    private static string BuildCsvSongRow(
        Song song, string playlistName, PlaylistSong ps,
        ExportOptions options, bool isPlaylist)
    {
        var fields = new List<string>();
        if (isPlaylist && options.IncludePlaylistName) fields.Add(playlistName ?? string.Empty);
        if (options.IncludeName) fields.Add(song.Name?.Length > 0 ? song.Name : Path.GetFileNameWithoutExtension(song.FilePath) ?? string.Empty);
        if (options.IncludeArtist) fields.Add(song.Artist ?? string.Empty);
        if (options.IncludeDuration) fields.Add(FormatDuration(song.Duration));
        if (options.IncludeReleaseYear) fields.Add(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : string.Empty);
        if (options.IncludeRating) fields.Add(song.Rating.ToString());
        if (options.IncludeLastPlayedAt) fields.Add(song.LastPlayedAt?.ToString("g") ?? string.Empty);
        if (options.IncludeFileLastModifiedAt) fields.Add(song.FileLastModifiedAt.ToString("g"));
        if (options.IncludeFilePath) fields.Add(song.FilePath ?? string.Empty);
        if (options.IncludeTags) fields.Add(string.Join(", ", song.Tags?.Select(t => t.Name) ?? Enumerable.Empty<string>()));
        if (options.IncludeComments) fields.Add(song.Comments ?? string.Empty);
        if (isPlaylist && options.IncludeIsPlayed) fields.Add(ps?.IsPlayed.ToString() ?? "False");
        return string.Join(",", fields.Select(EscapeCsvField));
    }

    private static Dictionary<string, object> BuildJsonSongRow(
        Song song, string playlistName, PlaylistSong ps,
        ExportOptions options, bool isPlaylist)
    {
        var row = new Dictionary<string, object>();
        if (isPlaylist && options.IncludePlaylistName) row["playlistName"] = playlistName ?? string.Empty;
        if (options.IncludeName) row["songName"] = song.Name?.Length > 0 ? song.Name : Path.GetFileNameWithoutExtension(song.FilePath) ?? string.Empty;
        if (options.IncludeArtist) row["artist"] = song.Artist ?? string.Empty;
        if (options.IncludeDuration) row["duration"] = FormatDuration(song.Duration);
        if (options.IncludeReleaseYear) row["releaseYear"] = song.ReleaseYear;
        if (options.IncludeRating) row["rating"] = song.Rating;
        if (options.IncludeLastPlayedAt) row["lastPlayedAt"] = song.LastPlayedAt?.ToString("g") ?? string.Empty;
        if (options.IncludeFileLastModifiedAt) row["fileLastModifiedAt"] = song.FileLastModifiedAt.ToString("g");
        if (options.IncludeFilePath) row["filePath"] = song.FilePath ?? string.Empty;
        if (options.IncludeTags) row["tags"] = song.Tags?.Select(t => t.Name).ToArray() ?? Array.Empty<string>();
        if (options.IncludeComments) row["comments"] = song.Comments ?? string.Empty;
        if (isPlaylist && options.IncludeIsPlayed) row["isPlayed"] = ps?.IsPlayed ?? false;
        return row;
    }

    private static string FormatDuration(TimeSpan d)
    {
        return d.Hours > 0
            ? $"{d.Hours}:{d.Minutes:00}:{d.Seconds:00}"
            : $"{d.Minutes}:{d.Seconds:00}";
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }
}
