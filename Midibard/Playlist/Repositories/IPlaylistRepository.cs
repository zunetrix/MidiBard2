using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist;

public interface IPlaylistRepository
{
    Task<Playlist?> GetByIdAsync(int id);
    Task<Playlist?> GetByFilePathAsync(string filePath);
    Task<List<Playlist>> GetAllAsync();
    Task<Playlist> CreateAsync(Playlist playlist);
    Task UpdateAsync(Playlist playlist);
    Task DeleteAsync(int id);

    Task<Song> AddSongAsync(int playlistId, Song song);
    Task RemoveSongAsync(int playlistId, int songId);
    Task UpdateSongAsync(int playlistId, Song song);
    Task ReorderSongAsync(int playlistId, int fromIndex, int toIndex);

    Task MarkSongAsPlayedAsync(int playlistId, int songId);
    Task IncrementPlayCountAsync(int playlistId, int songId);
    Task SetSongRatingAsync(int playlistId, int songId, double rate);
    Task AddSongTagAsync(int playlistId, int songId, string tag);
    Task RemoveSongTagAsync(int playlistId, int songId, string tag);
}
