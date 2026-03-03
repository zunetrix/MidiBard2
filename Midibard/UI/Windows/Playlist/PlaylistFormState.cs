using System.Collections.Generic;

using MidiBard.Playlist;

namespace MidiBard;

/// <summary>
/// Encapsulates form-related UI state for the Playlist window.
/// </summary>
public class PlaylistFormState
{
    // Edit song fields
    public string EditFilePath { get; set; } = string.Empty;
    public string EditName = string.Empty;
    public string EditArtist = string.Empty;
    public int EditReleaseYear = 0;
    public int EditRating = 0;
    public string EditDuration { get; set; } = string.Empty;
    public int EditPlayCount = 0;
    public string EditLastPlayedAt { get; set; } = string.Empty;
    public string EditCreatedAt { get; set; } = string.Empty;
    public string EditUpdatedAt { get; set; } = string.Empty;
    public string EditAddedAt { get; set; } = string.Empty;
    public bool EditIsPlayed = false;
    public string EditTag = string.Empty;

    // Tag editing
    public int SelectedTagIndex = -1;
    public List<Tag> AvailableTags = new();
    public bool TagsLoaded = false;

    // New playlist input
    public string NewPlaylistName = string.Empty;

    public void LoadEditPlaylistSongState(PlaylistSong playlistSong)
    {
        EditFilePath = playlistSong.Song.FilePath ?? "";
        EditName = playlistSong.Song.Name ?? "";
        EditArtist = playlistSong.Song.Artist ?? "";
        EditReleaseYear = playlistSong.Song.ReleaseYear;
        EditRating = playlistSong.Song.Rating;
        EditDuration = playlistSong.Song.Duration.ToString(@"mm\:ss");
        EditPlayCount = playlistSong.Song.PlayCount;
        EditLastPlayedAt = playlistSong.Song.LastPlayedAt?.ToString("g") ?? "-";
        EditCreatedAt = playlistSong.Song.CreatedAt.ToString("g");
        EditUpdatedAt = playlistSong.Song.UpdatedAt.ToString("g");
        EditAddedAt = playlistSong?.AddedAt.ToString("g") ?? "-";
        EditIsPlayed = playlistSong?.IsPlayed ?? false;
    }

    public void ClearEditFields()
    {
        EditFilePath = string.Empty;
        EditName = string.Empty;
        EditArtist = string.Empty;
        EditReleaseYear = 0;
        EditRating = 0;
        EditDuration = string.Empty;
        EditPlayCount = 0;
        EditLastPlayedAt = string.Empty;
        EditCreatedAt = string.Empty;
        EditUpdatedAt = string.Empty;
        EditAddedAt = string.Empty;
        EditIsPlayed = false;
        EditTag = string.Empty;
    }
}
