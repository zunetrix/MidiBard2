using System;

using Microsoft.Extensions.DependencyInjection;

using MidiBard.Playlist;
using MidiBard.Playlist.Services;

namespace MidiBard;

/// <summary>
/// Centralized service registry for dependency injection.
/// Registers all application services and repositories in one place.
/// Single source of truth for application-wide DI container.
/// </summary>
public static class ServiceContainer
{
    // Repositories
    public static ISongRepository SongRepository { get; private set; } = null!;
    public static IPlaylistRepository PlaylistRepository { get; private set; } = null!;
    public static ITagRepository TagRepository { get; private set; } = null!;

    // Services
    public static ISongService SongService { get; private set; } = null!;
    public static IPlaylistService PlaylistService { get; private set; } = null!;
    public static IPlaylistSongService PlaylistSongService { get; private set; } = null!;
    public static IMidiFileService MidiFileService { get; private set; } = null!;
    public static ITagService TagService { get; private set; } = null!;

    /// <summary>
    /// Check if registry is initialized.
    /// </summary>
    public static bool IsInitialized => SongRepository != null;

    /// <summary>
    /// Initialize the service registry with all dependencies (repositories + services).
    /// </summary>
    public static void Initialize(
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

        // Assign repositories directly
        SongRepository = songRepository;
        PlaylistRepository = playlistRepository;
        TagRepository = tagRepository;

        // Build DI container for services
        var services = new ServiceCollection();
        services.AddSingleton<IPlaylistRepository>(playlistRepository);
        services.AddSingleton<ISongRepository>(songRepository);
        services.AddSingleton<ITagRepository>(tagRepository);
        services.AddSingleton<IMidiFileService>(new MidiFileService(config));
        services.AddSingleton<ISongService>(sp =>
            new SongService(songRepository, playlistRepository, sp.GetRequiredService<IMidiFileService>()));
        services.AddSingleton<ITagService>(
            new TagService(tagRepository, songRepository));
        services.AddSingleton<IPlaylistService>(
            new PlaylistService(playlistRepository));
        services.AddSingleton<IPlaylistSongService>(
            new PlaylistSongService(playlistRepository, songRepository));

        var provider = services.BuildServiceProvider();

        // Extract services to static properties
        SongService = provider.GetRequiredService<ISongService>();
        PlaylistService = provider.GetRequiredService<IPlaylistService>();
        PlaylistSongService = provider.GetRequiredService<IPlaylistSongService>();
        MidiFileService = provider.GetRequiredService<IMidiFileService>();
        TagService = provider.GetRequiredService<ITagService>();

        DalamudApi.PluginLog.Information($"[ServiceContainer] Service registry initialized successfully with all repositories and services");
    }
}
