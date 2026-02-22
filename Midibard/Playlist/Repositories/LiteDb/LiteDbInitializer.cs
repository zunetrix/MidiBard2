using System;
using System.IO;

using LiteDB;

namespace MidiBard.Playlist;

public class LiteDbInitializer : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly string _databasePath;
    private const int CurrentSchemaVersion = 0;

    public LiteDbInitializer(string databasePath)
    {
        _databasePath = databasePath;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Configure BsonMapper for DbRef relationships
        ConfigureBsonMapper();

        // Initialize database with connection string
        var connectionString = new ConnectionString
        {
            Filename = databasePath,
            Connection = ConnectionType.Shared
        };

        _database = new LiteDatabase(connectionString);

        // Run initialization
        Initialize();
    }

    public LiteDatabase Database => _database;

    private void ConfigureBsonMapper()
    {
        var mapper = BsonMapper.Global;

        // Configure DbRef for Playlist.Songs -> PlaylistSong
        mapper.Entity<Playlist>()
            .DbRef(x => x.Songs, "playlist_songs");

        // Configure DbRef for Song.Tags -> Tag
        mapper.Entity<Song>()
            .DbRef(x => x.Tags, "tags");

        // Configure DbRef for PlaylistSong.Playlist -> Playlist
        mapper.Entity<PlaylistSong>()
            .DbRef(x => x.Playlist, "playlists");

        // Configure DbRef for PlaylistSong.Song -> Song
        mapper.Entity<PlaylistSong>()
            .DbRef(x => x.Song, "songs");
    }

    private void Initialize()
    {
        // Ensure collections exist
        EnsureCollections();

        // Run migrations
        Migrate();

        // Seed initial data if needed
        SeedData();
    }

    private void EnsureCollections()
    {
        // Get all collection names that should exist
        var expectedCollections = new[]
        {
            "playlists",
            "songs",
            "playlist_songs",
            "tags",
            "metadata"
        };

        foreach (var collectionName in expectedCollections)
        {
            // Accessing the collection creates it if it doesn't exist
            // This is the LiteDB way of ensuring collections exist
            _database.GetCollection(collectionName);
        }

        // Ensure unique index on Playlist.Name
        var PlaylistCollection = _database.GetCollection<Playlist>("playlists");
        PlaylistCollection.EnsureIndex(x => x.Name, true);

        // Ensure unique index on Song.FilePath
        var songCollection = _database.GetCollection<Song>("songs");
        songCollection.EnsureIndex(x => x.FilePath, true);
        songCollection.EnsureIndex(x => x.Name); // For song search
        songCollection.EnsureIndex(x => x.Artist); // For filtering by artist

        // Ensure unique index on Tag.Name
        var tagCollection = _database.GetCollection<Tag>("tags");
        tagCollection.EnsureIndex(x => x.Name, true);

        // Add indexes for PlaylistSong (join table)
        var playlistSongCollection = _database.GetCollection<PlaylistSong>("playlist_songs");
        playlistSongCollection.EnsureIndex(x => x.Playlist!.Id); // For finding songs in a playlist
    }

    private void Migrate()
    {
        var metadata = GetMetadata();

        if (metadata == null)
        {
            // First time initialization
            metadata = new DatabaseMetadata
            {
                SchemaVersion = CurrentSchemaVersion,
                CreatedAt = DateTime.UtcNow,
                LastMigratedAt = DateTime.UtcNow
            };

            _database.GetCollection<DatabaseMetadata>("metadata").Insert(metadata);
            DalamudApi.PluginLog.Information("LiteDB database initialized with schema version {0}", CurrentSchemaVersion);
            return;
        }

        // Check if migration is needed
        if (metadata.SchemaVersion < CurrentSchemaVersion)
        {
            PerformMigration(metadata.SchemaVersion, CurrentSchemaVersion);

            // Update metadata
            metadata.SchemaVersion = CurrentSchemaVersion;
            metadata.LastMigratedAt = DateTime.UtcNow;
            _database.GetCollection<DatabaseMetadata>("metadata").Update(metadata);

            DalamudApi.PluginLog.Information("LiteDB database migrated from version {0} to {1}",
                metadata.SchemaVersion, CurrentSchemaVersion);
        }
    }

    private void PerformMigration(int fromVersion, int toVersion)
    {
        // Migrations will be added as schema evolves
        // Currently starting at version 0
        // Migration from version 0 to 1 (example)
        if (fromVersion < 1 && toVersion >= 1)
        {
            // Example migration: add new indexes or transform data
            // var playlists = _database.GetCollection<Playlist>("playlists");
            // Add indexes or data transformations here
        }
    }

    private void SeedData()
    {
        var metadata = GetMetadata();

        if (metadata?.Seeded == false)
        {
            // Seed default data if needed
            // For example: default playlist, default settings, etc.

            metadata.Seeded = true;
            _database.GetCollection<DatabaseMetadata>("metadata").Update(metadata);
        }
    }

    private DatabaseMetadata? GetMetadata()
    {
        return _database.GetCollection<DatabaseMetadata>("metadata").FindOne(x => true);
    }

    public void Dispose()
    {
        _database?.Dispose();
    }

    private class DatabaseMetadata
    {
        public int Id { get; set; }
        public int SchemaVersion { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastMigratedAt { get; set; }
        public bool Seeded { get; set; }
    }
}
