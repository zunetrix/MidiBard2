using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using LiteDB;

namespace MidiBard.Playlist;

public class LiteDbTagRepository : ITagRepository
{
    private readonly LiteDatabase _database;

    public LiteDbTagRepository(LiteDatabase database)
    {
        _database = database;
    }

    public Task<Tag?> GetByIdAsync(int id) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Tag>("tags");
        return (Tag?)collection.FindById(id);
    });

    public Task<Tag?> GetByNameAsync(string name) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Tag>("tags");
        return (Tag?)collection.FindOne(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    });

    public Task<List<Tag>> GetAllAsync() => Task.Run(() =>
    {
        var collection = _database.GetCollection<Tag>("tags");
        return collection.FindAll().OrderBy(x => x.Name).ToList();
    });

    public Task<Tag> CreateAsync(string name) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Tag>("tags");
        var tag = new Tag { Name = name };
        collection.Insert(tag);
        return tag;
    });

    /// <summary>
    /// Get existing tag by name or create it - all in one Task.Run to avoid nested round-trips.
    /// </summary>
    public Task<Tag> CreateOrGetAsync(string name) => Task.Run<Tag>(() =>
    {
        var collection = _database.GetCollection<Tag>("tags");
        var existing = collection.FindOne(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var tag = new Tag { Name = name };
        collection.Insert(tag);
        return tag;
    });

    public Task UpdateAsync(Tag tag) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Tag>("tags");
        collection.Update(tag);
    });

    public Task DeleteAsync(int id) => Task.Run(() =>
    {
        try
        {
            var collection = _database.GetCollection<Tag>("tags");
            collection.Delete(id);
            DalamudApi.PluginLog.Information($"[LiteDbTagRepository] Deleted tag {id}");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbTagRepository] Error deleting tag {id}");
            throw;
        }
    });
}
