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
    private static IServiceProvider? _serviceProvider;

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

        var services = new ServiceCollection();

        // Repositories (first-class citizens in DI container)
        services.AddSingleton<IPlaylistRepository>(playlistRepository);
        services.AddSingleton<ISongRepository>(songRepository);
        services.AddSingleton<ITagRepository>(tagRepository);

        // Playlist Services
        services.AddSingleton<IMidiFileService>(new MidiFileService(config));
        services.AddSingleton<ISongService>(sp =>
            new SongService(songRepository, sp.GetRequiredService<IMidiFileService>()));
        services.AddSingleton<IPlaylistService>(
            new PlaylistService(playlistRepository));
        services.AddSingleton<IPlaylistSongService>(
            new PlaylistSongService(playlistRepository, songRepository));

        _serviceProvider = services.BuildServiceProvider();

        DalamudApi.PluginLog.Information($"[ServiceContainer] Service registry initialized successfully with all repositories and services");
    }

    /// <summary>
    /// Get a service from the registry.
    /// </summary>
    public static T GetService<T>() where T : notnull
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException($"Service {typeof(T).Name} not found. ServiceContainer may not be initialized.");
        return _serviceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Get a service from the registry (nullable).
    /// </summary>
    public static T? GetServiceOrNull<T>() where T : class =>
        _serviceProvider?.GetService<T>();

    /// <summary>
    /// Check if registry is initialized.
    /// </summary>
    public static bool IsInitialized => _serviceProvider != null;
}
