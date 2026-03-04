namespace MidiBard.Playlist.Services;

/// <summary>
/// Controls which fields are included in an export (CSV or JSON).
/// </summary>
public class ExportOptions
{
    public bool IncludeName = true;
    public bool IncludeArtist = true;
    public bool IncludeDuration = true;
    public bool IncludeReleaseYear = false;
    public bool IncludeRating = false;
    public bool IncludeLastPlayedAt = false;
    public bool IncludeFileLastModifiedAt = false;
    public bool IncludeFilePath = true;
    public bool IncludeTags = true;
    public bool IncludeComments = false;

    // Playlist-specific fields
    public bool IncludePlaylistName = true;
    public bool IncludeIsPlayed = false;
}
