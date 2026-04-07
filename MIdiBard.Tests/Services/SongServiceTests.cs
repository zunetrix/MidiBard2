using System;
using System.Collections.Generic;
using System.IO;

using MidiBard.Playlist;
using MidiBard.Playlist.Services;
using MidiBard.Tests.Infrastructure;

using Moq;

using PlaylistModel = MidiBard.Playlist.Playlist;

namespace MidiBard.Tests.Services;

public class SongServiceTests
{
    private readonly Mock<ISongRepository> _songRepo;
    private readonly Mock<IPlaylistRepository> _playlistRepo;
    private readonly Mock<IMidiFileService> _midiService;
    private readonly SongService _service;

    public SongServiceTests()
    {
        DalamudTestSetup.Initialize();
        _songRepo = new Mock<ISongRepository>();
        _playlistRepo = new Mock<IPlaylistRepository>();
        _midiService = new Mock<IMidiFileService>();
        _service = new SongService(_songRepo.Object, _playlistRepo.Object, _midiService.Object);
    }

    // GetByIdAsync 

    [Fact]
    public async Task GetByIdAsync_ExistingSong_ReturnsFromRepository()
    {
        var expected = new Song { Id = 1, Name = "Test" };
        _songRepo.Setup(r => r.GetSongByIdAsync(1)).ReturnsAsync(expected);

        var result = await _service.GetByIdAsync(1);

        result.ShouldBe(expected);
        _songRepo.Verify(r => r.GetSongByIdAsync(1), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        _songRepo.Setup(r => r.GetSongByIdAsync(99)).ReturnsAsync((Song?)null);

        var result = await _service.GetByIdAsync(99);

        result.ShouldBeNull();
    }

    // GetOrCreateFromFileAsync 

    [Fact]
    public async Task GetOrCreateFromFileAsync_EmptyPath_ReturnsNull()
    {
        var result = await _service.GetOrCreateFromFileAsync("", "", "", 0, TimeSpan.Zero);

        result.ShouldBeNull();
        _songRepo.Verify(r => r.CreateOrGetSongAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateFromFileAsync_ValidPath_AlwaysCallsUpdateAsync()
    {
        // UpdateAsync must always be called — ensures FileLastModifiedAt is persisted
        var path = @"C:\nonexistent\file.mid";
        var song = new Song { Id = 1, FilePath = path };
        _songRepo.Setup(r => r.CreateOrGetSongAsync(
            path, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<DateTime>()))
            .ReturnsAsync(song);
        _songRepo.Setup(r => r.UpdateAsync(song)).Returns(Task.CompletedTask);

        await _service.GetOrCreateFromFileAsync(path, "Test", "", 0, TimeSpan.Zero);

        _songRepo.Verify(r => r.UpdateAsync(song), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateFromFileAsync_ExistingFile_PassesCorrectFileLastModifiedAt()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "test.mid");
        var expectedModifiedAt = File.GetLastWriteTime(path);
        var song = new Song { Id = 1, FilePath = path };

        _songRepo.Setup(r => r.CreateOrGetSongAsync(
            path, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<TimeSpan>(), true, expectedModifiedAt))
            .ReturnsAsync(song);
        _songRepo.Setup(r => r.UpdateAsync(song)).Returns(Task.CompletedTask);

        var result = await _service.GetOrCreateFromFileAsync(path, "Test", "", 0, TimeSpan.Zero);

        result.ShouldNotBeNull();
        result!.FileLastModifiedAt.ShouldBe(expectedModifiedAt);
        _songRepo.Verify(r => r.CreateOrGetSongAsync(
            path, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<TimeSpan>(), true, expectedModifiedAt), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateFromFileAsync_NonExistentFile_SetsIsValidFalse()
    {
        var path = @"C:\nonexistent\file.mid";
        var song = new Song { Id = 1, FilePath = path, IsValid = true };
        _songRepo.Setup(r => r.CreateOrGetSongAsync(
            path, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<DateTime>()))
            .ReturnsAsync(song);
        _songRepo.Setup(r => r.UpdateAsync(It.IsAny<Song>())).Returns(Task.CompletedTask);

        await _service.GetOrCreateFromFileAsync(path, "Test", "", 0, TimeSpan.Zero);

        song.IsValid.ShouldBeFalse();
    }

    // UpdateAsync 

    [Fact]
    public async Task UpdateAsync_ValidSong_DelegatesToRepository()
    {
        var song = new Song { Id = 1 };
        _songRepo.Setup(r => r.UpdateAsync(song)).Returns(Task.CompletedTask);

        var result = await _service.UpdateAsync(song);

        result.ShouldBeTrue();
        _songRepo.Verify(r => r.UpdateAsync(song), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NullSong_ReturnsFalse()
    {
        var result = await _service.UpdateAsync(null!);

        result.ShouldBeFalse();
        _songRepo.Verify(r => r.UpdateAsync(It.IsAny<Song>()), Times.Never);
    }

    // RecordPlayAsync 

    [Fact]
    public async Task RecordPlayAsync_DelegatesToRepository()
    {
        _songRepo.Setup(r => r.IncrementPlayCountAsync(5)).Returns(Task.CompletedTask);

        var result = await _service.RecordPlayAsync(5);

        result.ShouldBeTrue();
        _songRepo.Verify(r => r.IncrementPlayCountAsync(5), Times.Once);
    }

    // SetRatingAsync 

    [Fact]
    public async Task SetRatingAsync_DelegatesToRepository()
    {
        _songRepo.Setup(r => r.SetRatingAsync(3, 4)).Returns(Task.CompletedTask);

        var result = await _service.SetRatingAsync(3, 4);

        result.ShouldBeTrue();
        _songRepo.Verify(r => r.SetRatingAsync(3, 4), Times.Once);
    }

    // DeleteAsync (cascade) 

    [Fact]
    public async Task DeleteAsync_RemovesSongFromPlaylistsThenDeletes()
    {
        var song = new Song { Id = 10 };
        var affected = new PlaylistModel
        {
            Id = 1,
            Name = "Has Song",
            Songs = new List<PlaylistSong> { new() { Song = song } }
        };
        var unaffected = new PlaylistModel { Id = 2, Name = "Empty", Songs = new List<PlaylistSong>() };

        _playlistRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<PlaylistModel> { affected, unaffected });
        _playlistRepo.Setup(r => r.UpdateAsync(It.IsAny<PlaylistModel>())).Returns(Task.CompletedTask);
        _songRepo.Setup(r => r.DeleteAsync(10)).Returns(Task.CompletedTask);

        var result = await _service.DeleteAsync(10);

        result.ShouldBeTrue();
        affected.Songs.ShouldBeEmpty();
        _playlistRepo.Verify(r => r.UpdateAsync(affected), Times.Once);
        _playlistRepo.Verify(r => r.UpdateAsync(unaffected), Times.Never);
        _songRepo.Verify(r => r.DeleteAsync(10), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NoPlaylists_StillDeletesSong()
    {
        _playlistRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<PlaylistModel>());
        _songRepo.Setup(r => r.DeleteAsync(7)).Returns(Task.CompletedTask);

        var result = await _service.DeleteAsync(7);

        result.ShouldBeTrue();
        _songRepo.Verify(r => r.DeleteAsync(7), Times.Once);
    }

    // BulkUpdateAsync 

    [Fact]
    public async Task BulkUpdateAsync_DelegatesTo_Repository()
    {
        var songs = new List<Song> { new() { Id = 1 }, new() { Id = 2 } };
        _songRepo.Setup(r => r.BulkUpdateAsync(songs)).ReturnsAsync(2);

        var result = await _service.BulkUpdateAsync(songs);

        result.ShouldBe(2);
        _songRepo.Verify(r => r.BulkUpdateAsync(songs), Times.Once);
    }

    // BulkReplaceFilePathPrefixAsync uses BulkUpdateAsync 

    [Fact]
    public async Task BulkReplaceFilePathPrefixAsync_UsesBulkUpdateAsync_NotPerSongUpdateAsync()
    {
        var songs = new List<Song>
        {
            new() { Id = 1, FilePath = @"C:\new\a.mid" },
            new() { Id = 2, FilePath = @"C:\new\b.mid" },
        };
        _songRepo.Setup(r => r.BulkReplaceFilePathPrefixAsync(@"C:\old", @"C:\new"))
                 .ReturnsAsync(songs);
        _songRepo.Setup(r => r.BulkUpdateAsync(It.IsAny<IEnumerable<Song>>()))
                 .ReturnsAsync(2);

        var count = await _service.BulkReplaceFilePathPrefixAsync(@"C:\old", @"C:\new");

        count.ShouldBe(2);
        _songRepo.Verify(r => r.BulkUpdateAsync(It.IsAny<IEnumerable<Song>>()), Times.Once);
        _songRepo.Verify(r => r.UpdateAsync(It.IsAny<Song>()), Times.Never);
    }

    [Fact]
    public async Task BulkReplaceFilePathPrefixAsync_SetsIsValidOnEachSong()
    {
        var songs = new List<Song>
        {
            new() { Id = 1, FilePath = @"C:\nonexistent\a.mid", IsValid = true },
        };
        _songRepo.Setup(r => r.BulkReplaceFilePathPrefixAsync(It.IsAny<string>(), It.IsAny<string>()))
                 .ReturnsAsync(songs);
        _songRepo.Setup(r => r.BulkUpdateAsync(It.IsAny<IEnumerable<Song>>()))
                 .ReturnsAsync(1);

        await _service.BulkReplaceFilePathPrefixAsync(@"C:\old", @"C:\nonexistent");

        // File doesn't exist → IsValid must be set to false
        songs[0].IsValid.ShouldBeFalse();
    }
}
