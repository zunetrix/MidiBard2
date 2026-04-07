using MidiBard.Playlist;
using MidiBard.Playlist.Services;
using MidiBard.Tests.Infrastructure;

using PlaylistModel = MidiBard.Playlist.Playlist;

namespace MidiBard.Tests.Services;

/// <summary>
/// Integration tests for PlaylistSongService using an in-memory LiteDB database.
/// </summary>
public class PlaylistSongServiceTests : IDisposable
{
    private readonly InMemoryDbFixture _db;
    private readonly LiteDbSongRepository _songRepo;
    private readonly LiteDbPlaylistRepository _playlistRepo;
    private readonly PlaylistSongService _service;

    public PlaylistSongServiceTests()
    {
        _db = new InMemoryDbFixture();
        _songRepo = new LiteDbSongRepository(_db.Database);
        _playlistRepo = new LiteDbPlaylistRepository(_db.Database, _songRepo);
        _service = new PlaylistSongService(_playlistRepo, _songRepo);
    }

    public void Dispose() => _db.Dispose();

    private Task<Song> CreateSongAsync(string path) =>
        _songRepo.CreateOrGetSongAsync(path, "Song", "", 0, TimeSpan.Zero);

    // RemoveSongAsync 

    [Fact]
    public async Task RemoveSongAsync_NonSequentialId_RemovesCorrectSong()
    {
        // Create 9 dummy songs to advance the auto-increment ID counter
        for (int i = 1; i <= 9; i++)
            await CreateSongAsync($@"C:\songs\dummy{i}.mid");

        // 10th song will have Id=10, which is at playlist index 0
        var targetSong = await CreateSongAsync(@"C:\songs\target.mid");
        targetSong.Id.ShouldBeGreaterThan(1); // Id is NOT 0 or 1

        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "Test" });
        await _playlistRepo.AddSongToPlaylistAsync(playlist.Id, targetSong.Id, -1);

        // Sanity check: song is at index 0, but its Id != 0
        var before = await _playlistRepo.GetByIdAsync(playlist.Id);
        before!.Songs.Count.ShouldBe(1);
        before.Songs[0].Song!.Id.ShouldBe(targetSong.Id);

        // This is the regression scenario: old code passed songId as array index → wrong removal
        var result = await _service.RemoveSongAsync(playlist.Id, targetSong.Id);

        result.ShouldBeTrue();
        var after = await _playlistRepo.GetByIdAsync(playlist.Id);
        after!.Songs.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveSongAsync_FirstOfTwoSongs_RemovesCorrectOne()
    {
        var song1 = await CreateSongAsync(@"C:\songs\first.mid");
        var song2 = await CreateSongAsync(@"C:\songs\second.mid");

        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "Two Songs" });
        await _playlistRepo.AddSongToPlaylistAsync(playlist.Id, song1.Id, -1);
        await _playlistRepo.AddSongToPlaylistAsync(playlist.Id, song2.Id, -1);

        var result = await _service.RemoveSongAsync(playlist.Id, song1.Id);

        result.ShouldBeTrue();
        var after = await _playlistRepo.GetByIdAsync(playlist.Id);
        after!.Songs.Count.ShouldBe(1);
        after.Songs[0].Song!.Id.ShouldBe(song2.Id);
    }

    [Fact]
    public async Task RemoveSongAsync_InvalidSongId_ReturnsFalse()
    {
        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "Empty" });
        var result = await _service.RemoveSongAsync(playlist.Id, 9999);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveSongAsync_InvalidPlaylistId_ReturnsFalse()
    {
        var result = await _service.RemoveSongAsync(9999, 1);
        result.ShouldBeFalse();
    }
}
