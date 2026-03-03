using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service interface for managing tags.
/// </summary>
public interface ITagService
{
    Task<Tag?> GetByIdAsync(int id);
    Task<Tag?> GetByNameAsync(string name);
    Task<List<Tag>> GetAllAsync();
    Task<Tag?> CreateAsync(string name);
    Task<Tag?> CreateOrGetAsync(string name);
    Task<bool> UpdateAsync(Tag tag);
    Task<bool> DeleteAsync(int tagId);
}
