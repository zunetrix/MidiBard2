using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using LiteDB;

namespace MidiBard.Playlist;

public class LiteDbSongRepository : ISongRepository
{
    private readonly LiteDatabase _database;

    public LiteDbSongRepository(LiteDatabase database)
    {
        _database = database;
    }

    public Task<Song?> GetSongByIdAsync(int id) => Task.Run<Song?>(() =>
    {
        try
        {
            var song = _database.GetCollection<Song>("songs").Include(x => x.Tags).FindById(id);
            if (song != null)
                DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Loaded song {id} (with all tags)");
            return song;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error getting song {id}");
            throw;
        }
    });

    public Task<Song?> GetSongByIdLightAsync(int id) => Task.Run<Song?>(() =>
    {
        try
        {
            var song = _database.GetCollection<Song>("songs").FindById(id);
            if (song != null)
                DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Loaded song {id} (lightweight - without tags)");
            return song;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error getting song {id}");
            throw;
        }
    });

    public Task<Song?> GetByIdAsync(int id) => GetSongByIdAsync(id);

    public Task<Song?> GetByFilePathAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.FromResult<Song?>(null);

        return Task.Run<Song?>(() =>
        {
            try
            {
                var song = _database.GetCollection<Song>("songs").FindOne(x => x.FilePath == filePath);
                if (song != null)
                    DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Loaded song by file path (lightweight): {filePath}");
                return song;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error getting song by file path {filePath}");
                throw;
            }
        });
    }

    public Task<Song?> GetByFilePathWithTagsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.FromResult<Song?>(null);

        return Task.Run<Song?>(() =>
        {
            try
            {
                var song = _database.GetCollection<Song>("songs").Include(x => x.Tags).FindOne(x => x.FilePath == filePath);
                if (song != null)
                    DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Loaded song by file path (with tags): {filePath}");
                return song;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error getting song by file path with tags {filePath}");
                throw;
            }
        });
    }

    public Task<Song> CreateOrGetSongAsync(string filePath, string name, string artist, int releaseYear, TimeSpan duration, bool isValid = true, DateTime fileLastModifiedAt = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("FilePath cannot be empty", nameof(filePath));

        return Task.Run(() =>
        {
            try
            {
                var collection = _database.GetCollection<Song>("songs");
                var existingSong = collection.FindOne(x => x.FilePath == filePath);
                if (existingSong != null)
                {
                    bool updated = false;
                    if (existingSong.Duration != duration) { existingSong.Duration = duration; updated = true; }
                    if (existingSong.Name != name) { existingSong.Name = name; updated = true; }
                    if (existingSong.Artist != artist) { existingSong.Artist = artist; updated = true; }
                    if (existingSong.ReleaseYear != releaseYear) { existingSong.ReleaseYear = releaseYear; updated = true; }
                    if (existingSong.IsValid != isValid) { existingSong.IsValid = isValid; updated = true; }
                    if (fileLastModifiedAt != default && existingSong.FileLastModifiedAt != fileLastModifiedAt)
                    {
                        existingSong.FileLastModifiedAt = fileLastModifiedAt;
                        updated = true;
                    }
                    if (updated)
                    {
                        existingSong.UpdatedAt = DateTime.UtcNow;
                        collection.Update(existingSong);
                        DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Updated existing song {filePath}");
                    }
                    return existingSong;
                }

                var newSong = new Song
                {
                    FilePath = filePath,
                    Name = name,
                    Artist = artist,
                    ReleaseYear = releaseYear,
                    Duration = duration,
                    IsValid = isValid,
                    FileLastModifiedAt = fileLastModifiedAt,
                    Tags = new List<Tag>(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                collection.Insert(newSong);
                DalamudApi.PluginLog.Information($"[LiteDbSongRepository] Created new song {filePath}");
                return newSong;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error creating/getting song {filePath}");
                throw;
            }
        });
    }

    public Task<List<Song>> GetAllSongsAsync() => Task.Run(() =>
    {
        try
        {
            var songs = _database.GetCollection<Song>("songs").FindAll().ToList();
            DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Loaded {songs.Count} songs (lightweight - without tags)");
            return songs;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting all songs");
            throw;
        }
    });

    public Task<List<Song>> GetAllSongsWithTagsAsync() => Task.Run(() =>
    {
        try
        {
            var songs = _database.GetCollection<Song>("songs").Include(x => x.Tags).FindAll().ToList();
            DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Loaded {songs.Count} songs (with all tags)");
            return songs;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting all songs with tags");
            throw;
        }
    });

    public Task<List<Song>> GetSongsByIdsAsync(IEnumerable<int> songIds)
    {
        ArgumentNullException.ThrowIfNull(songIds);
        var ids = songIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Task.FromResult(new List<Song>());

        return Task.Run(() =>
        {
            try
            {
                var songs = _database.GetCollection<Song>("songs").Find(x => ids.Contains(x.Id)).ToList();
                DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Loaded {songs.Count} songs by IDs (lightweight)");
                return songs;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting songs by ids");
                throw;
            }
        });
    }

    public Task<List<Song>> GetSongsByIdsWithTagsAsync(IEnumerable<int> songIds)
    {
        ArgumentNullException.ThrowIfNull(songIds);
        var ids = songIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Task.FromResult(new List<Song>());

        return Task.Run(() =>
        {
            try
            {
                var songs = _database.GetCollection<Song>("songs").Include(x => x.Tags).Find(x => ids.Contains(x.Id)).ToList();
                DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Loaded {songs.Count} songs by IDs (with tags)");
                return songs;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting songs by ids with tags");
                throw;
            }
        });
    }

    public Task UpdateAsync(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (string.IsNullOrWhiteSpace(song.FilePath))
            throw new ArgumentException("Song file path cannot be empty", nameof(song));

        return Task.Run(() =>
        {
            try
            {
                song.UpdatedAt = DateTime.UtcNow;
                _database.GetCollection<Song>("songs").Update(song);
                DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Updated song {song.Id}: {song.Name}");
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error updating song {song.Id}");
                throw;
            }
        });
    }

    public Task DeleteAsync(int id) => Task.Run(() =>
    {
        try
        {
            _database.GetCollection<Song>("songs").Delete(id);
            DalamudApi.PluginLog.Information($"[LiteDbSongRepository] Deleted song {id}");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error deleting song {id}");
            throw;
        }
    });

    public Task DeleteAllAsync() => Task.Run(() =>
        _database.GetCollection<Song>("songs").DeleteAll()
    );

    // ==================== Song-specific Operations ====================

    public Task IncrementPlayCountAsync(int songId) => Task.Run(() =>
    {
        try
        {
            var collection = _database.GetCollection<Song>("songs");
            var song = collection.FindById(songId);
            if (song != null)
            {
                song.PlayCount++;
                song.LastPlayedAt = DateTime.UtcNow;
                song.UpdatedAt = DateTime.UtcNow;
                collection.Update(song);
                DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Incremented play count for song {songId}: {song.PlayCount}");
            }
            else
            {
                DalamudApi.PluginLog.Warning($"[LiteDbSongRepository] Song {songId} not found for play count increment");
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error incrementing play count for song {songId}");
            throw;
        }
    });

    public Task AddTagAsync(int songId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("Tag name cannot be empty", nameof(tagName));

        return Task.Run(() =>
        {
            try
            {
                var songCollection = _database.GetCollection<Song>("songs");
                var song = songCollection.Include(x => x.Tags).FindById(songId);
                if (song == null) return;

                var tag = CreateOrGetTag(_database.GetCollection<Tag>("tags"), tagName);
                if (!song.Tags.Any(t => t.Id == tag.Id))
                {
                    song.Tags.Add(tag);
                    song.UpdatedAt = DateTime.UtcNow;
                    songCollection.Update(song);
                }
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error adding tag {tagName} to song {songId}");
                throw;
            }
        });
    }

    public Task RemoveTagAsync(int songId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("Tag name cannot be empty", nameof(tagName));

        return Task.Run(() =>
        {
            try
            {
                var songCollection = _database.GetCollection<Song>("songs");
                var song = songCollection.Include(x => x.Tags).FindById(songId);
                if (song == null)
                {
                    DalamudApi.PluginLog.Warning($"[LiteDbSongRepository] Song {songId} not found for tag removal");
                    return;
                }

                var tagToRemove = song.Tags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
                if (tagToRemove != null)
                {
                    song.Tags.Remove(tagToRemove);
                    song.UpdatedAt = DateTime.UtcNow;
                    songCollection.Update(song);
                    DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Removed tag {tagName} from song {songId}");
                }
                else
                {
                    DalamudApi.PluginLog.Debug($"[LiteDbSongRepository] Tag {tagName} not found on song {songId}");
                }
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error removing tag {tagName} from song {songId}");
                throw;
            }
        });
    }

    public Task RemoveTagByIdAsync(int songId, int tagId) => Task.Run(() =>
    {
        var songCollection = _database.GetCollection<Song>("songs");
        var song = songCollection.Include(x => x.Tags).FindById(songId);
        if (song == null) return;

        var tagToRemove = song.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tagToRemove != null)
        {
            song.Tags.Remove(tagToRemove);
            song.UpdatedAt = DateTime.UtcNow;
            songCollection.Update(song);
        }
    });

    public Task AddTagsAsync(int songId, IEnumerable<string> tagNames)
    {
        ArgumentNullException.ThrowIfNull(tagNames);
        var tagNameList = tagNames.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (tagNameList.Count == 0)
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            try
            {
                var songCollection = _database.GetCollection<Song>("songs");
                var song = songCollection.Include(x => x.Tags).FindById(songId);
                if (song == null) return;

                var tagCollection = _database.GetCollection<Tag>("tags");
                bool updated = false;
                foreach (var tagName in tagNameList)
                {
                    if (song.Tags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var tag = CreateOrGetTag(tagCollection, tagName);
                    song.Tags.Add(tag);
                    updated = true;
                }

                if (updated)
                {
                    song.UpdatedAt = DateTime.UtcNow;
                    songCollection.Update(song);
                }
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error adding tags to song {songId}");
                throw;
            }
        });
    }

    public Task RemoveTagsAsync(int songId, IEnumerable<string> tagNames)
    {
        ArgumentNullException.ThrowIfNull(tagNames);
        var tagNameList = tagNames.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (tagNameList.Count == 0)
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            var songCollection = _database.GetCollection<Song>("songs");
            var song = songCollection.Include(x => x.Tags).FindById(songId);
            if (song == null) return;

            var tagsToRemove = song.Tags
                .Where(t => tagNameList.Any(tn => tn.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (tagsToRemove.Count > 0)
            {
                foreach (var tag in tagsToRemove)
                    song.Tags.Remove(tag);

                song.UpdatedAt = DateTime.UtcNow;
                songCollection.Update(song);
            }
        });
    }

    public Task<List<Song>> BulkReplaceFilePathPrefixAsync(string oldPrefix, string newPrefix)
    {
        return Task.Run(() =>
        {
            var collection = _database.GetCollection<Song>("songs");
            var songs = collection.FindAll()
                .Where(s => s.FilePath != null && s.FilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var song in songs)
                song.FilePath = newPrefix + song.FilePath[oldPrefix.Length..];

            return songs;
        });
    }

    public Task<List<Song>> BulkInsertSongsAsync(IEnumerable<Song> songs)
    {
        return Task.Run(() =>
        {
            var list = songs.ToList();
            _database.GetCollection<Song>("songs").InsertBulk(list);
            return list;
        });
    }

    public Task<int> BulkUpdateAsync(IEnumerable<Song> songs)
    {
        return Task.Run(() =>
        {
            var list = songs.ToList();
            if (list.Count == 0) return 0;
            return _database.GetCollection<Song>("songs").Update(list);
        });
    }

    // ==================== SyncId Methods ====================

    public Task<Song?> GetBySyncIdAsync(int syncId) => Task.Run<Song?>(() =>
    {
        try
        {
            return _database.GetCollection<Song>("songs").FindOne(x => x.SyncId == syncId);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbSongRepository] Error getting song by SyncId {syncId}");
            throw;
        }
    });

    public Task<int> GetMaxSyncIdAsync() => Task.Run(() =>
    {
        try
        {
            var col = _database.GetCollection<Song>("songs");
            var max = col.FindAll()
                .Where(s => s.SyncId.HasValue)
                .Select(s => s.SyncId!.Value)
                .DefaultIfEmpty(0)
                .Max();
            return max;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting max SyncId");
            throw;
        }
    });

    public Task<List<int>> GetAllSyncIdsAsync() => Task.Run(() =>
    {
        try
        {
            return _database.GetCollection<Song>("songs")
                .FindAll()
                .Where(s => s.SyncId.HasValue)
                .Select(s => s.SyncId!.Value)
                .OrderBy(id => id)
                .ToList();
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting all SyncIds");
            throw;
        }
    });

    private static Tag CreateOrGetTag(ILiteCollection<Tag> collection, string tagName)
    {
        var existing = collection.FindOne(x => x.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var tag = new Tag { Name = tagName };
        collection.Insert(tag);
        return tag;
    }
}
