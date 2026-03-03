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

    public Task<Tag?> GetByIdAsync(int id)
    {
        var collection = _database.GetCollection<Tag>("tags");
        var tag = collection.FindById(id);
        return Task.FromResult<Tag?>(tag);
    }

    public Task<Tag?> GetByNameAsync(string name)
    {
        var collection = _database.GetCollection<Tag>("tags");
        var tag = collection.FindOne(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<Tag?>(tag);
    }

    public Task<List<Tag>> GetAllAsync()
    {
        var collection = _database.GetCollection<Tag>("tags");
        var tags = collection.FindAll().OrderBy(x => x.Name).ToList();
        return Task.FromResult(tags);
    }

    public Task<Tag> CreateAsync(string name)
    {
        var collection = _database.GetCollection<Tag>("tags");
        var tag = new Tag
        {
            Name = name
        };
        collection.Insert(tag);
        return Task.FromResult(tag);
    }

    public Task<Tag> CreateOrGetAsync(string name)
    {
        var existing = GetByNameAsync(name).Result;
        if (existing != null)
        {
            return Task.FromResult(existing);
        }
        return CreateAsync(name);
    }

    public Task UpdateAsync(Tag tag)
    {
        var collection = _database.GetCollection<Tag>("tags");
        collection.Update(tag);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        try
        {
            var collection = _database.GetCollection<Tag>("tags");
            collection.Delete(id);
            DalamudApi.PluginLog.Information($"[LiteDbTagRepository] Deleted tag {id}");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbTagRepository] Error deleting tag {id}");
            throw;
        }
    }
}
