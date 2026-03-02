using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for exporting playlist data to various file formats.
/// </summary>
public class PlaylistExportService : IPlaylistExportService
{
    /// <summary>
    /// Export a playlist to CSV format.
    /// Format: Index,FileName,Duration,FilePath
    /// </summary>
    public async Task<bool> ExportToCsvAsync(Playlist playlist, string filePath)
    {
        if (playlist?.Songs == null || string.IsNullOrWhiteSpace(filePath))
            return false;

        return await Task.Run(() =>
        {
            try
            {
                using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    // Write header
                    writer.WriteLine("Index,FileName,Duration,FilePath");

                    // Write data rows
                    for (int i = 0; i < playlist.Songs.Count; i++)
                    {
                        var playlistSong = playlist.Songs[i];
                        if (playlistSong?.Song == null)
                            continue;

                        var song = playlistSong.Song;
                        var fileName = Path.GetFileNameWithoutExtension(song.FilePath);

                        // Format: HH:MM:SS or MM:SS
                        var duration = song.Duration;
                        var durationStr = duration.Hours > 0
                            ? $"{duration.Hours}:{duration.Minutes:00}:{duration.Seconds:00}"
                            : $"{duration.Minutes}:{duration.Seconds:00}";

                        // Escape quotes in filenames and paths
                        var escapedFileName = EscapeCsvField(fileName);
                        var escapedPath = EscapeCsvField(song.FilePath);

                        writer.WriteLine($"{i + 1},{escapedFileName},{durationStr},{escapedPath}");
                    }
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

    /// <summary>
    /// Export a playlist to JSON format.
    /// Includes playlist metadata and all songs with their details.
    /// </summary>
    public async Task<bool> ExportToJsonAsync(Playlist playlist, string filePath)
    {
        if (playlist?.Songs == null || string.IsNullOrWhiteSpace(filePath))
            return false;

        return await Task.Run(() =>
        {
            try
            {
                var json = new StringBuilder();
                json.AppendLine("{");
                json.AppendLine($"  \"playlistName\": \"{EscapeJsonString(playlist.Name)}\",");
                json.AppendLine($"  \"songCount\": {playlist.Songs.Count},");
                json.AppendLine("  \"songs\": [");

                for (int i = 0; i < playlist.Songs.Count; i++)
                {
                    var playlistSong = playlist.Songs[i];
                    if (playlistSong?.Song == null)
                        continue;

                    var song = playlistSong.Song;
                    json.AppendLine("    {");
                    json.AppendLine($"      \"index\": {i + 1},");
                    json.AppendLine($"      \"fileName\": \"{EscapeJsonString(Path.GetFileNameWithoutExtension(song.FilePath))}\",");
                    json.AppendLine($"      \"artist\": \"{EscapeJsonString(song.Artist ?? "")}\",");
                    json.AppendLine($"      \"duration\": \"{song.Duration:hh\\:mm\\:ss}\",");
                    json.AppendLine($"      \"filePath\": \"{EscapeJsonString(song.FilePath)}\",");
                    json.AppendLine($"      \"isPlayed\": {(playlistSong.IsPlayed ? "true" : "false")}");
                    json.AppendLine(i < playlist.Songs.Count - 1 ? "    }," : "    }");
                }

                json.AppendLine("  ]");
                json.AppendLine("}");

                File.WriteAllText(filePath, json.ToString(), Encoding.UTF8);

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

    // Helper methods
    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "\"\"";

        // If field contains comma, quotes, or newlines, wrap in quotes and escape quotes
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        return field;
    }

    private static string EscapeJsonString(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
