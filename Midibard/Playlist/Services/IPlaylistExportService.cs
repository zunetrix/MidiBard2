using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for exporting playlist data to various file formats.
/// </summary>
public interface IPlaylistExportService
{
    /// <summary>
    /// Export a playlist to CSV format.
    /// Columns: Index, FileName, Duration, FilePath
    /// </summary>
    /// <param name="playlist">The playlist to export</param>
    /// <param name="filePath">Output file path</param>
    /// <returns>True if export succeeded, false otherwise</returns>
    Task<bool> ExportToCsvAsync(Playlist playlist, string filePath);

    /// <summary>
    /// Export a playlist to JSON format.
    /// </summary>
    /// <param name="playlist">The playlist to export</param>
    /// <param name="filePath">Output file path</param>
    /// <returns>True if export succeeded, false otherwise</returns>
    Task<bool> ExportToJsonAsync(Playlist playlist, string filePath);
}
