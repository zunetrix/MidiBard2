using System.Collections.Generic;

namespace MidiBard.Playlist;

/// <summary>
/// Interface for unified song edit form state used by both PlaylistWindow and SongsWindow.
/// Consolidates the current dual pattern: PlaylistFormState + individual _edit* fields in SongsWindow.
/// Provides both Song (global) and PlaylistSong (playlist-scoped) field management.
/// </summary>
public interface ISongEditFormState
{
    // Load methods for different contexts
    /// <summary>
    /// Load edit state from a Song entity (global context, used by SongsWindow).
    /// </summary>
    void LoadFromSong(Song song);

    /// <summary>
    /// Load edit state from a PlaylistSong entity (playlist context, used by PlaylistWindow).
    /// </summary>
    void LoadFromPlaylistSong(PlaylistSong playlistSong, int playlistId);

    // Get edited values back
    /// <summary>
    /// Get the Song entity with changes applied.
    /// </summary>
    Song GetSongChanges();

    /// <summary>
    /// Get the PlaylistSong entity with changes applied.
    /// Returns null if in global context (SongsWindow).
    /// </summary>
    PlaylistSong? GetPlaylistSongChanges();

    // All editable fields - Global context (both windows)
    /// <summary>File path of the MIDI file.</summary>
    string EditFilePath { get; set; }

    /// <summary>Song title/name.</summary>
    string EditName { get; set; }

    /// <summary>Artist name.</summary>
    string EditArtist { get; set; }

    /// <summary>Release year of the song.</summary>
    int EditReleaseYear { get; set; }

    /// <summary>Star rating (1-5).</summary>
    int EditRating { get; set; }

    /// <summary>Formatted duration string (MM:SS or HH:MM:SS).</summary>
    string EditDuration { get; set; }

    /// <summary>Number of times the song has been played.</summary>
    int EditPlayCount { get; set; }

    /// <summary>Timestamp of last play.</summary>
    string EditLastPlayedAt { get; set; }

    /// <summary>Timestamp when song was created in library.</summary>
    string EditCreatedAt { get; set; }

    /// <summary>Timestamp when song metadata was last updated.</summary>
    string EditUpdatedAt { get; set; }

    // Playlist-specific fields - Only relevant in PlaylistWindow
    /// <summary>Whether the song is marked as played in this specific playlist.</summary>
    bool EditIsPlayed { get; set; }

    /// <summary>Timestamp when the song was added to this playlist.</summary>
    string EditAddedAt { get; set; }

    // Tag management - Shared between both windows
    /// <summary>Currently selected tag index in dropdown.</summary>
    int SelectedTagIndex { get; set; }

    /// <summary>List of all available tags for selection.</summary>
    List<Tag> AvailableTags { get; set; }

    /// <summary>Whether tags have been loaded from database.</summary>
    bool TagsLoaded { get; set; }

    // Utility methods for clearing state
    /// <summary>Clear all edit fields to default/empty values.</summary>
    void ClearEditFields();
}
