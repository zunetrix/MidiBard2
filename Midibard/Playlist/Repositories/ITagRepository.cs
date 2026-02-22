using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist;

public interface ITagRepository
{
    Task<Tag?> GetByIdAsync(int id);
    Task<Tag?> GetByNameAsync(string name);
    Task<List<Tag>> GetAllAsync();
    Task<Tag> CreateAsync(string name);
    Task<Tag> CreateOrGetAsync(string name);
    Task UpdateAsync(Tag tag);
    Task DeleteAsync(int id);
}
