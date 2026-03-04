using MidiBard.Playlist;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Repositories;

public class SongRepositoryTests : IDisposable
{
    private readonly InMemoryDbFixture _db;
    private readonly LiteDbSongRepository _repo;

    public SongRepositoryTests()
    {
        _db = new InMemoryDbFixture();
        _repo = new LiteDbSongRepository(_db.Database);
    }

    public void Dispose() => _db.Dispose();

    private Task<Song> CreateAsync(string path = @"C:\songs\test.mid", string name = "Test Song") =>
        _repo.CreateOrGetSongAsync(path, name, "Artist", 2024, TimeSpan.FromMinutes(3));

    // --- CreateOrGetSongAsync ---

    [Fact]
    public async Task CreateOrGetSongAsync_NewSong_InsertsWithId()
    {
        var song = await CreateAsync();
        song.Id.ShouldBeGreaterThan(0);
        song.FilePath.ShouldBe(@"C:\songs\test.mid");
        song.Name.ShouldBe("Test Song");
    }

    [Fact]
    public async Task CreateOrGetSongAsync_ExistingPath_ReturnsSameSong()
    {
        var first = await CreateAsync();
        var second = await CreateAsync();
        second.Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task CreateOrGetSongAsync_ExistingPathNewName_UpdatesName()
    {
        await _repo.CreateOrGetSongAsync(@"C:\songs\upd.mid", "Old Name", "", 0, TimeSpan.Zero);
        var updated = await _repo.CreateOrGetSongAsync(@"C:\songs\upd.mid", "New Name", "", 0, TimeSpan.Zero);
        updated.Name.ShouldBe("New Name");
    }

    // --- GetByIdAsync ---

    [Fact]
    public async Task GetByIdAsync_ExistingSong_Returns()
    {
        var created = await CreateAsync();
        var loaded = await _repo.GetByIdAsync(created.Id);
        loaded.ShouldNotBeNull();
        loaded!.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync(9999);
        result.ShouldBeNull();
    }

    // --- GetByFilePathAsync ---

    [Fact]
    public async Task GetByFilePathAsync_Match_Returns()
    {
        await CreateAsync(@"C:\songs\findme.mid");
        var result = await _repo.GetByFilePathAsync(@"C:\songs\findme.mid");
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetByFilePathAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByFilePathAsync(@"C:\songs\nothere.mid");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetByFilePathAsync_EmptyPath_ReturnsNull()
    {
        var result = await _repo.GetByFilePathAsync(string.Empty);
        result.ShouldBeNull();
    }

    // --- FileLastModifiedAt ---

    [Fact]
    public async Task CreateOrGetSongAsync_NewSong_PersistsFileLastModifiedAt()
    {
        var modifiedAt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var song = await _repo.CreateOrGetSongAsync(
            @"C:\songs\fmod.mid", "Test", "", 0, TimeSpan.Zero, fileLastModifiedAt: modifiedAt);

        var loaded = await _repo.GetByIdAsync(song.Id);
        loaded!.FileLastModifiedAt.ShouldBe(modifiedAt.ToLocalTime());
    }

    [Fact]
    public async Task CreateOrGetSongAsync_ExistingSong_UpdatesFileLastModifiedAt()
    {
        // Create without date
        var song = await _repo.CreateOrGetSongAsync(@"C:\songs\fmod2.mid", "Test", "", 0, TimeSpan.Zero);

        // Re-import with date
        var modifiedAt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        await _repo.CreateOrGetSongAsync(@"C:\songs\fmod2.mid", "Test", "", 0, TimeSpan.Zero, fileLastModifiedAt: modifiedAt);

        // Fresh load from DB — verifies the value was actually persisted
        var loaded = await _repo.GetByIdAsync(song.Id);
        loaded!.FileLastModifiedAt.ShouldBe(modifiedAt.ToLocalTime());
    }

    // --- UpdateAsync ---

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var song = await CreateAsync();
        song.Name = "Updated Name";
        song.Comments = "Some comments";
        await _repo.UpdateAsync(song);

        var loaded = await _repo.GetByIdAsync(song.Id);
        loaded!.Name.ShouldBe("Updated Name");
        loaded.Comments.ShouldBe("Some comments");
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_RemovesSong()
    {
        var song = await CreateAsync();
        await _repo.DeleteAsync(song.Id);
        var loaded = await _repo.GetByIdAsync(song.Id);
        loaded.ShouldBeNull();
    }

    // --- GetAllSongsAsync ---

    [Fact]
    public async Task GetAllSongsAsync_ReturnsAllSongs()
    {
        await _repo.CreateOrGetSongAsync(@"C:\s1.mid", "Song 1", "", 0, TimeSpan.Zero);
        await _repo.CreateOrGetSongAsync(@"C:\s2.mid", "Song 2", "", 0, TimeSpan.Zero);
        await _repo.CreateOrGetSongAsync(@"C:\s3.mid", "Song 3", "", 0, TimeSpan.Zero);

        var all = await _repo.GetAllSongsAsync();
        all.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    // --- IncrementPlayCountAsync ---

    [Fact]
    public async Task IncrementPlayCountAsync_IncrementsCount()
    {
        var song = await CreateAsync(@"C:\songs\play.mid");
        await _repo.IncrementPlayCountAsync(song.Id);
        await _repo.IncrementPlayCountAsync(song.Id);

        var loaded = await _repo.GetByIdAsync(song.Id);
        loaded!.PlayCount.ShouldBe(2);
    }

    [Fact]
    public async Task IncrementPlayCountAsync_SetsLastPlayedAt()
    {
        var song = await CreateAsync(@"C:\songs\lastplay.mid");
        song.LastPlayedAt.ShouldBeNull();

        await _repo.IncrementPlayCountAsync(song.Id);

        var loaded = await _repo.GetByIdAsync(song.Id);
        loaded!.LastPlayedAt.ShouldNotBeNull();
        loaded.LastPlayedAt!.Value.ShouldNotBe(DateTime.MinValue);
    }

    // --- DateTime UTC/local round-trip ---

    [Fact]
    public async Task FileLastModifiedAt_UtcInput_ReturnsLocalEquivalent()
    {
        var utcTime = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var song = await _repo.CreateOrGetSongAsync(
            @"C:\songs\dt_utc.mid", "Test", "", 0, TimeSpan.Zero, fileLastModifiedAt: utcTime);
        var loaded = await _repo.GetByIdAsync(song.Id);

        loaded!.FileLastModifiedAt.ShouldBe(utcTime.ToLocalTime());
        loaded.FileLastModifiedAt.Kind.ShouldBe(DateTimeKind.Local);
    }

    [Fact]
    public async Task FileLastModifiedAt_LocalInput_RoundTripsCorrectly()
    {
        var localTime = new DateTime(2024, 6, 1, 14, 30, 0, DateTimeKind.Local);

        var song = await _repo.CreateOrGetSongAsync(
            @"C:\songs\dt_local.mid", "Test", "", 0, TimeSpan.Zero, fileLastModifiedAt: localTime);
        var loaded = await _repo.GetByIdAsync(song.Id);

        loaded!.FileLastModifiedAt.ShouldBe(localTime);
        loaded.FileLastModifiedAt.Kind.ShouldBe(DateTimeKind.Local);
    }

    [Fact]
    public async Task LastPlayedAt_AfterIncrementPlayCount_IsLocalKind()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var song = await CreateAsync(@"C:\songs\dt_play.mid");

        await _repo.IncrementPlayCountAsync(song.Id);

        var loaded = await _repo.GetByIdAsync(song.Id);
        loaded!.LastPlayedAt.ShouldNotBeNull();
        loaded.LastPlayedAt!.Value.Kind.ShouldBe(DateTimeKind.Local);
        loaded.LastPlayedAt.Value.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task CreatedAt_AfterInsert_IsLocalKind()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var song = await CreateAsync(@"C:\songs\dt_created.mid");
        var loaded = await _repo.GetByIdAsync(song.Id);

        loaded!.CreatedAt.Kind.ShouldBe(DateTimeKind.Local);
        loaded.CreatedAt.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task UpdatedAt_AfterUpdate_IsLocalKind()
    {
        var song = await CreateAsync(@"C:\songs\dt_updated.mid");
        song.Name = "Renamed";
        await _repo.UpdateAsync(song);

        var before = DateTime.Now.AddSeconds(-1);
        var loaded = await _repo.GetByIdAsync(song.Id);

        loaded!.UpdatedAt.Kind.ShouldBe(DateTimeKind.Local);
    }

    // --- SetRatingAsync ---

    [Fact]
    public async Task SetRatingAsync_PersistsRating()
    {
        var song = await CreateAsync(@"C:\songs\rated.mid");
        await _repo.SetRatingAsync(song.Id, 7);

        var loaded = await _repo.GetByIdAsync(song.Id);
        loaded!.Rating.ShouldBe(7);
    }

    [Fact]
    public async Task SetRatingAsync_InvalidRange_Throws()
    {
        var song = await CreateAsync(@"C:\songs\rated2.mid");
        await Should.ThrowAsync<ArgumentException>(() => _repo.SetRatingAsync(song.Id, 11));
    }

    // --- AddTagAsync / RemoveTagAsync ---

    [Fact]
    public async Task AddTagAsync_AssociatesTagWithSong()
    {
        var song = await CreateAsync(@"C:\songs\tagged.mid");
        await _repo.AddTagAsync(song.Id, "Rock");

        var loaded = await _repo.GetSongByIdAsync(song.Id);
        loaded!.Tags.ShouldContain(t => t.Name == "Rock");
    }

    [Fact]
    public async Task RemoveTagAsync_DisassociatesTag()
    {
        var song = await CreateAsync(@"C:\songs\untagged.mid");
        await _repo.AddTagAsync(song.Id, "Jazz");
        await _repo.RemoveTagAsync(song.Id, "Jazz");

        var loaded = await _repo.GetSongByIdAsync(song.Id);
        loaded!.Tags.ShouldNotContain(t => t.Name == "Jazz");
    }
}
