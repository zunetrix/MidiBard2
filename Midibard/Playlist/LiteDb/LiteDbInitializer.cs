using System;
using System.IO;

using LiteDB;

namespace MidiBard.Playlist;

public class LiteDbInitializer : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly string _databasePath;
    private const int CurrentSchemaVersion = 1;

    public LiteDbInitializer(string databasePath)
    {
        _databasePath = databasePath;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

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
            "metadata"
        };

        foreach (var collectionName in expectedCollections)
        {
            // Accessing the collection creates it if it doesn't exist
            // This is the LiteDB way of ensuring collections exist
            _database.GetCollection(collectionName);
        }
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
        // Migration from version 0 to 1 (example)
        if (fromVersion < 1 && toVersion >= 1)
        {
            // Example migration: add new indexes or transform data
            // var playlists = _database.GetCollection<Playlist>("playlists");
            // Add indexes or data transformations here
        }

        // Add more migrations as schema evolves
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
