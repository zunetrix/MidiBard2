using System.IO;

using MidiBard.Playlist;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Extensions;

public class PlaylistSongExtensionsTests
{
    public PlaylistSongExtensionsTests() => DalamudTestSetup.Initialize();

    private static PlaylistSong Entry(string filePath, TimeSpan? duration = null) =>
        new() { Song = new Song { FilePath = filePath, Duration = duration ?? TimeSpan.Zero } };

    // GetFileName 

    [Fact]
    public void GetFileName_ReturnsNameWithoutExtension()
    {
        Entry(Path.Combine("music", "My Song.mid")).GetFileName().ShouldBe("My Song");
    }

    [Fact]
    public void GetFileName_NullSong_ReturnsEmpty()
    {
        new PlaylistSong { Song = null }.GetFileName().ShouldBe(string.Empty);
    }

    // GetFileDirectory 

    [Fact]
    public void GetFileDirectory_ReturnsDirectory()
    {
        var directory = Path.Combine("music", "sub");
        Entry(Path.Combine(directory, "My Song.mid")).GetFileDirectory().ShouldBe(directory);
    }

    [Fact]
    public void GetFileDirectory_NullSong_ReturnsEmpty()
    {
        new PlaylistSong { Song = null }.GetFileDirectory().ShouldBe(string.Empty);
    }

    // GetLrcPath 

    [Fact]
    public void GetLrcPath_ChangesExtensionToLrc()
    {
        Entry(@"C:\music\song.mid").GetLrcPath().ShouldBe(@"C:\music\song.lrc");
    }

    [Fact]
    public void GetLrcPath_NullSong_ReturnsEmpty()
    {
        new PlaylistSong { Song = null }.GetLrcPath().ShouldBe(string.Empty);
    }

    // GetSongLengthFormated 

    [Fact]
    public void GetSongLengthFormated_WithoutHours_FormatsMmSs()
    {
        Entry(@"C:\s.mid", TimeSpan.FromSeconds(125)).GetSongLengthFormated().ShouldBe("02:05");
    }

    [Fact]
    public void GetSongLengthFormated_WithHours_FormatsHMmSs()
    {
        Entry(@"C:\s.mid", TimeSpan.FromSeconds(3725)).GetSongLengthFormated().ShouldBe("1:02:05");
    }

    [Fact]
    public void GetSongLengthFormated_NullSong_ReturnsZero()
    {
        new PlaylistSong { Song = null }.GetSongLengthFormated().ShouldBe("00:00");
    }

    // GetSongLength 

    [Fact]
    public void GetSongLength_ReturnsDuration()
    {
        var expected = TimeSpan.FromMinutes(3.5);
        Entry(@"C:\s.mid", expected).GetSongLength().ShouldBe(expected);
    }

    [Fact]
    public void GetSongLength_NullSong_ReturnsZero()
    {
        new PlaylistSong { Song = null }.GetSongLength().ShouldBe(TimeSpan.Zero);
    }

    // GetFilePath 

    [Fact]
    public void GetFilePath_ReturnsFilePath()
    {
        Entry(@"C:\music\My Song.mid").GetFilePath().ShouldBe(@"C:\music\My Song.mid");
    }

    [Fact]
    public void GetFilePath_NullSong_ReturnsEmpty()
    {
        new PlaylistSong { Song = null }.GetFilePath().ShouldBe(string.Empty);
    }
}
