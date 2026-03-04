using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for managing tags globally with cascading cleanup.
/// </summary>
public class TagService : ITagService
{
    private readonly ITagRepository _tagRepository;
    private readonly ISongRepository _songRepository;

    public TagService(
        ITagRepository tagRepository,
        ISongRepository songRepository)
    {
        ArgumentNullException.ThrowIfNull(tagRepository);
        ArgumentNullException.ThrowIfNull(songRepository);

        _tagRepository = tagRepository;
        _songRepository = songRepository;
    }

    public async Task<Tag?> GetByIdAsync(int id)
    {
        try
        {
            return await _tagRepository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[TagService] Error getting tag {id}");
            return null;
        }
    }

    public async Task<Tag?> GetByNameAsync(string name)
    {
        try
        {
            return await _tagRepository.GetByNameAsync(name);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[TagService] Error getting tag by name {name}");
            return null;
        }
    }

    public async Task<List<Tag>> GetAllAsync()
    {
        try
        {
            return await _tagRepository.GetAllAsync();
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[TagService] Error getting all tags");
            return new List<Tag>();
        }
    }

    public async Task<Tag?> CreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            DalamudApi.PluginLog.Warning("[TagService] Cannot create tag with empty name");
            return null;
        }

        try
        {
            return await _tagRepository.CreateAsync(name);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[TagService] Error creating tag {name}");
            return null;
        }
    }

    public async Task<Tag?> CreateOrGetAsync(string name)
    {
        try
        {
            return await _tagRepository.CreateOrGetAsync(name);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[TagService] Error creating or getting tag {name}");
            return null;
        }
    }

    public async Task<bool> UpdateAsync(Tag tag)
    {
        if (tag == null)
        {
            DalamudApi.PluginLog.Warning("[TagService] Cannot update null tag");
            return false;
        }

        try
        {
            await _tagRepository.UpdateAsync(tag);
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[TagService] Error updating tag {tag.Id}");
            return false;
        }
    }

    /// <summary>
    /// Delete a tag with cascading cleanup:
    /// 1. Removes tag from all songs that reference it
    /// 2. Removes tag from database
    /// </summary>
    public async Task<bool> DeleteAsync(int tagId)
    {
        try
        {
            // Step 1: Get the tag to be deleted (for logging)
            var tag = await _tagRepository.GetByIdAsync(tagId);
            if (tag == null)
            {
                DalamudApi.PluginLog.Warning($"[TagService] Tag {tagId} not found for deletion");
                return false;
            }

            // Step 2: Remove this tag from all songs that reference it
            var allSongs = await _songRepository.GetAllSongsAsync();
            var songsAffected = 0;

            foreach (var song in allSongs)
            {
                if (song.Tags == null || song.Tags.Count == 0)
                    continue;

                var tagToRemove = song.Tags.FirstOrDefault(t => t.Id == tagId);
                if (tagToRemove != null)
                {
                    songsAffected++;
                    song.Tags.Remove(tagToRemove);
                    song.UpdatedAt = DateTime.UtcNow;
                    await _songRepository.UpdateAsync(song);
                }
            }

            // Step 3: Delete the tag
            await _tagRepository.DeleteAsync(tagId);

            DalamudApi.PluginLog.Information($"[TagService] Deleted tag {tagId}: {tag.Name} (removed from {songsAffected} songs)");

            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[TagService] Error deleting tag {tagId}");
            return false;
        }
    }
}
