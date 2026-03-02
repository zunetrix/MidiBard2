using System;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Configuration for building Playlist services.
/// Creates instances of all Playlist-related services.
/// </summary>
public static class PlaylistServiceConfiguration
{
    /// <summary>
    /// Build instances of all Playlist services.
    /// </summary>
    public static (
        IPlaylistService playlistService,
        IPlaylistSongService playlistSongService,
        ISongService songService,
        IMidiFileService midiFileService)
        BuildServices(
            Configuration config,
            IPlaylistRepository playlistRepository,
            ISongRepository songRepository,
            ITagRepository tagRepository)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (playlistRepository == null)
            throw new ArgumentNullException(nameof(playlistRepository));
        if (songRepository == null)
            throw new ArgumentNullException(nameof(songRepository));
        if (tagRepository == null)
            throw new ArgumentNullException(nameof(tagRepository));

        // Create MIDI File Service with Config
        var midiFileService = new MidiFileService(config);

        // Create Song Service
        var songService = new SongService(songRepository, midiFileService);

        // Create Playlist Service
        var playlistService = new PlaylistService(playlistRepository);

        // Create PlaylistSong Service
        var playlistSongService = new PlaylistSongService(playlistRepository, songRepository);

        return (playlistService, playlistSongService, songService, midiFileService);
    }
}
