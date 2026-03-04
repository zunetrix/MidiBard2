using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for exporting song and playlist data to various file formats.
/// </summary>
public interface IPlaylistExportService
{
    /// <summary>Export a flat list of songs to CSV.</summary>
    Task<bool> ExportSongsToCsvAsync(IList<Song> songs, string filePath, ExportOptions options);

    /// <summary>Export a flat list of songs to JSON.</summary>
    Task<bool> ExportSongsToJsonAsync(IList<Song> songs, string filePath, ExportOptions options);

    /// <summary>Export songs from a playlist context (includes PlaylistName / IsPlayed columns) to CSV.</summary>
    Task<bool> ExportPlaylistSongsToCsvAsync(string playlistName, IList<Song> songs, IDictionary<int, PlaylistSong> songLookup, string filePath, ExportOptions options);

    /// <summary>Export songs from a playlist context (includes PlaylistName / IsPlayed columns) to JSON.</summary>
    Task<bool> ExportPlaylistSongsToJsonAsync(string playlistName, IList<Song> songs, IDictionary<int, PlaylistSong> songLookup, string filePath, ExportOptions options);
}
