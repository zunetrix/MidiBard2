using System.IO;

using LiteDB;

using MidiBard.Playlist;

namespace MidiBard.Tests.Infrastructure;

/// <summary>
/// Provides an isolated LiteDB in-memory database for each test class.
/// BsonMapper.Global is a static singleton — configured only once via a static flag.
/// Indexes are per-database and created for every instance.
/// </summary>
public sealed class InMemoryDbFixture : IDisposable
{
    public LiteDatabase Database { get; }

    private static bool _mapperConfigured;
    private static readonly object _mapperLock = new();

    public InMemoryDbFixture()
    {
        DalamudTestSetup.Initialize();

        lock (_mapperLock)
        {
            if (!_mapperConfigured)
            {
                var mapper = BsonMapper.Global;
                mapper.SerializeNullValues = true;
                mapper.Entity<Song>().DbRef(x => x.Tags, "tags");
                mapper.Entity<PlaylistSong>().DbRef(x => x.Song, "songs");
                // Match production config: dates stored as UTC, deserialized as local time
                mapper.RegisterType<DateTime>(
                    serialize: dt => new BsonValue(dt),
                    deserialize: bson => bson.AsDateTime.ToLocalTime());
                _mapperConfigured = true;
            }
        }

        Database = new LiteDatabase(new MemoryStream());
        CreateIndexes(Database);
    }

    private static void CreateIndexes(LiteDatabase db)
    {
        db.GetCollection<Song>("songs").EnsureIndex(x => x.FilePath, unique: true);
        db.GetCollection<Tag>("tags").EnsureIndex(x => x.Name, unique: true);
    }

    public void Dispose() => Database.Dispose();
}
