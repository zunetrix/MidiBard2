using MidiBard.Playlist;
using MidiBard.Tests.Infrastructure;

using PlaylistModel = MidiBard.Playlist.Playlist;

namespace MidiBard.Tests.Repositories;

public class PlaylistRepositoryTests : IDisposable
{
    private readonly InMemoryDbFixture _db;
    private readonly LiteDbSongRepository _songRepo;
    private readonly LiteDbPlaylistRepository _playlistRepo;

    public PlaylistRepositoryTests()
    {
        _db = new InMemoryDbFixture();
        _songRepo = new LiteDbSongRepository(_db.Database);
        _playlistRepo = new LiteDbPlaylistRepository(_db.Database, _songRepo);
    }

    public void Dispose() => _db.Dispose();

    private Task<Song> CreateSongAsync(string path = @"C:\songs\ps.mid") =>
        _songRepo.CreateOrGetSongAsync(path, "Song", "", 0, TimeSpan.FromMinutes(2));

    // --- CreateAsync ---

    [Fact]
    public async Task CreateAsync_NewPlaylist_PersistsWithId()
    {
        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "My List" });
        playlist.Id.ShouldBeGreaterThan(0);
        playlist.Name.ShouldBe("My List");
        playlist.Songs.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_PersistedToDatabase()
    {
        var created = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "Saved List" });
        var loaded = await _playlistRepo.GetByIdAsync(created.Id);
        loaded.ShouldNotBeNull();
        loaded!.Name.ShouldBe("Saved List");
    }

    // --- GetByIdAsync ---

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _playlistRepo.GetByIdAsync(9999);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithSong_IncludesSongData()
    {
        var song = await CreateSongAsync(@"C:\songs\included.mid");
        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "With Songs" });
        await _playlistRepo.AddSongToPlaylistAsync(playlist.Id, song.Id, -1);

        var loaded = await _playlistRepo.GetByIdAsync(playlist.Id);

        loaded.ShouldNotBeNull();
        loaded!.Songs.Count.ShouldBe(1);
        loaded.Songs[0].Song.ShouldNotBeNull();
        loaded.Songs[0].Song!.Id.ShouldBe(song.Id);
    }

    // --- UpdateAsync ---

    [Fact]
    public async Task UpdateAsync_PersistsNameChange()
    {
        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "Original" });
        playlist.Name = "Updated";
        await _playlistRepo.UpdateAsync(playlist);

        var loaded = await _playlistRepo.GetByIdAsync(playlist.Id);
        loaded!.Name.ShouldBe("Updated");
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_RemovesPlaylist()
    {
        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "ToDelete" });
        await _playlistRepo.DeleteAsync(playlist.Id);

        var loaded = await _playlistRepo.GetByIdAsync(playlist.Id);
        loaded.ShouldBeNull();
    }

    // --- AddSongToPlaylistAsync ---

    [Fact]
    public async Task AddSongToPlaylistAsync_AddsCorrectly()
    {
        var song = await CreateSongAsync(@"C:\songs\add.mid");
        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "Add Test" });

        await _playlistRepo.AddSongToPlaylistAsync(playlist.Id, song.Id, -1);

        var loaded = await _playlistRepo.GetByIdAsync(playlist.Id);
        loaded!.Songs.Count.ShouldBe(1);
        loaded.Songs[0].Song!.Id.ShouldBe(song.Id);
    }

    [Fact]
    public async Task AddSongToPlaylistAsync_DuplicateSong_NotAdded()
    {
        var song = await CreateSongAsync(@"C:\songs\dup.mid");
        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "Dup Test" });

        await _playlistRepo.AddSongToPlaylistAsync(playlist.Id, song.Id, -1);
        await _playlistRepo.AddSongToPlaylistAsync(playlist.Id, song.Id, -1);

        var loaded = await _playlistRepo.GetByIdAsync(playlist.Id);
        loaded!.Songs.Count.ShouldBe(1);
    }

    // --- RemoveSongFromPlaylistAsync ---

    [Fact]
    public async Task RemoveSongFromPlaylistAsync_RemovesCorrectly()
    {
        var song = await CreateSongAsync(@"C:\songs\rem.mid");
        var playlist = await _playlistRepo.CreateAsync(new PlaylistModel { Name = "Remove Test" });
        await _playlistRepo.AddSongToPlaylistAsync(playlist.Id, song.Id, -1);

        await _playlistRepo.RemoveSongFromPlaylistAsync(playlist.Id, song.Id);

        var loaded = await _playlistRepo.GetByIdAsync(playlist.Id);
        loaded!.Songs.ShouldBeEmpty();
    }

    // --- GetAllAsync ---

    [Fact]
    public async Task GetAllAsync_ReturnsAllPlaylists()
    {
        await _playlistRepo.CreateAsync(new PlaylistModel { Name = "List A" });
        await _playlistRepo.CreateAsync(new PlaylistModel { Name = "List B" });

        var all = await _playlistRepo.GetAllAsync();

        all.ShouldContain(p => p.Name == "List A");
        all.ShouldContain(p => p.Name == "List B");
    }
}
