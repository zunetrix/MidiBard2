namespace MidiBard;

/// <summary>
/// Persisted column visibility settings for the Songs collection window.
/// Fields must be public (not properties) to support ref passing to ImGui.Checkbox.
/// </summary>
public class SongsWindowColumnSettings
{
    public bool Name = true;
    public bool Artist = true;
    public bool Year = false;
    public bool Duration = true;
    public bool PlayCount = true;
    public bool LastPlayed = true;
    public bool Rating = true;
    public bool FilePath = false;
    public bool FileModified = true;
    public bool Tags = false;
    public bool Comments = false;
    public bool IsValid = false;
}

/// <summary>
/// Persisted column visibility settings for the Playlist window song table.
/// Fields must be public (not properties) to support ref passing to ImGui.Checkbox.
/// </summary>
public class PlaylistWindowColumnSettings
{
    public bool Name = true;
    public bool Artist = true;
    public bool Year = false;
    public bool Duration = true;
    public bool PlayCount = true;
    public bool LastPlayed = true;
    public bool Played = true;
    public bool Rating = true;
    public bool Tags = false;
    public bool Comments = false;
    public bool FilePath = false;
    public bool FileModified = true;
}
