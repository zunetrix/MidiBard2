using System.IO;

using MidiBard.Playlist;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Models;

public class SongTests
{
    public SongTests() => DalamudTestSetup.Initialize();

    // ValidateFile 

    [Fact]
    public void ValidateFile_EmptyPath_SetsIsValidFalse()
    {
        var song = new Song { FilePath = string.Empty };
        song.ValidateFile();
        song.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void ValidateFile_NonExistentFile_SetsIsValidFalse()
    {
        var song = new Song { FilePath = @"C:\does\not\exist\song.mid" };
        song.ValidateFile();
        song.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void ValidateFile_ExistingFile_SetsIsValidTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var song = new Song { FilePath = tempFile };
            song.ValidateFile();
            song.IsValid.ShouldBeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateFile_UpdatesUpdatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var song = new Song { FilePath = string.Empty };
        song.ValidateFile();
        song.UpdatedAt.ShouldBeGreaterThan(before);
    }
}
