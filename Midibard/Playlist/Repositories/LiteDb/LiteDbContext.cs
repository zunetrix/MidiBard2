using System;
using System.IO;

using LiteDB;

namespace MidiBard.Playlist;

public class LiteDbContext : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly string _databasePath;
    private const int CurrentSchemaVersion = 0;

    public LiteDbContext(string databasePath)
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

        // Store null values explicitly so all fields appear in documents
        mapper.SerializeNullValues = true;

        // Dates are stored as UTC in LiteDB; convert to local time on read so
        // entities already carry the correct local value for both display and logic.
        mapper.RegisterType<DateTime>(
            serialize: dt => new BsonValue(dt),
            deserialize: bson => bson.AsDateTime.ToLocalTime());

        // Playlist.Songs is now embedded (not a DbRef)
        // No need to configure DbRef for it

        // Configure DbRef for Song.Tags -> Tag
        mapper.Entity<Song>()
            .DbRef(x => x.Tags, "tags");

        // Configure DbRef for PlaylistSong.Song -> Song
        // PlaylistSong.Playlist is removed (no longer needed)
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
            "tags",
            "metadata"
            // Note: playlist_songs is no longer a separate collection
            // PlaylistSong documents are now embedded in Playlist.Songs
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

        // Note: Indexes for PlaylistSong are no longer needed
        // since PlaylistSong documents are now embedded in Playlist.Songs
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
            DalamudApi.PluginLog.Information($"LiteDB database initialized with schema version {CurrentSchemaVersion}");
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

            DalamudApi.PluginLog.Information($"LiteDB database migrated from version {metadata.SchemaVersion} to {CurrentSchemaVersion}");
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
            // Seed default playlist.
            // Guard with FindOne first to avoid the insert in the common case, then catch
            // the rare LiteException(unique constraint) that occurs when two clients start
            // simultaneously on a fresh install and both pass the null-check before either
            // has committed its write.
            var playlistCollection = _database.GetCollection<Playlist>("playlists");
            if (playlistCollection.FindOne(x => x.Name == "Default") == null)
            {
                try
                {
                    playlistCollection.Insert(new Playlist
                    {
                        Name = "Default",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                catch (LiteException)
                {
                    // Another client inserted "Default" concurrently - already exists, proceed.
                }
            }

            // Seed default tags (same concurrent-insert guard).
            var tagCollection = _database.GetCollection<Tag>("tags");
            var defaultTags = new[]
            {
                "Pop",
                "Rock",
                "Rock Classic",
                "Hard Rock",
                "Heavy Metal",
                "Punk Rock",
                "J-Rock",
                "K-Pop",
                "J-Pop",
                "EDM / Techno",
                "Disco",
                "Classical",
                "Jazz",
                "Blues",
                "Funk",
                "Folk",
                "Western",
                "Country",
                "Hip Hop / Rap",

                "Christmas",
                "Halloween",
                "Anime",
                "Video Game",
                "Movie",
                "TV Show",
                "Meme",
                "Relax",
                "Romantic",
                "Favorite"
            };

            foreach (var tagName in defaultTags)
            {
                if (tagCollection.FindOne(x => x.Name == tagName) == null)
                {
                    try
                    {
                        tagCollection.Insert(new Tag { Name = tagName });
                    }
                    catch (LiteException)
                    {
                        // Another client inserted this tag concurrently - already exists, proceed.
                    }
                }
            }

            metadata.Seeded = true;
            _database.GetCollection<DatabaseMetadata>("metadata").Update(metadata);

            DalamudApi.PluginLog.Information("LiteDB database seeded with default playlist and tags");
        }
    }

    private DatabaseMetadata? GetMetadata()
    {
        return _database.GetCollection<DatabaseMetadata>("metadata").FindOne(x => true);
    }

    /// <summary>
    /// Deletes all documents from a collection without resetting its auto-increment sequence.
    /// </summary>
    public void ResetCollection(string collectionName)
    {
        _database.GetCollection(collectionName).DeleteAll();
    }

    /// <summary>
    /// Resets the auto-increment sequence for a collection so the next insert starts at Id = 1.
    /// </summary>
    public void ResetSequence(string collectionName)
    {
        var sequences = _database.GetCollection("$sequences");
        sequences.Delete(collectionName);
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
