using System.IO;

using MidiBard.Playlist;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Models;

public class SongTests
{
    public SongTests() => DalamudTestSetup.Initialize();

    // --- RecordPlay ---

    [Fact]
    public void RecordPlay_IncrementsPlayCount()
    {
        var song = new Song();
        song.RecordPlay();
        song.PlayCount.ShouldBe(1);
    }

    [Fact]
    public void RecordPlay_CalledMultipleTimes_Accumulates()
    {
        var song = new Song();
        song.RecordPlay();
        song.RecordPlay();
        song.RecordPlay();
        song.PlayCount.ShouldBe(3);
    }

    [Fact]
    public void RecordPlay_SetsLastPlayedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var song = new Song();
        song.RecordPlay();
        song.LastPlayedAt.ShouldNotBeNull();
        song.LastPlayedAt!.Value.ShouldBeGreaterThan(before);
    }

    [Fact]
    public void RecordPlay_UpdatesUpdatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var song = new Song();
        song.RecordPlay();
        song.UpdatedAt.ShouldBeGreaterThan(before);
    }

    // --- SetRating ---

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void SetRating_ValidRange_SetsRating(int rating)
    {
        var song = new Song();
        song.SetRating(rating);
        song.Rating.ShouldBe(rating);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    public void SetRating_OutOfRange_DoesNotChangeRating(int rating)
    {
        var song = new Song { Rating = 3 };
        song.SetRating(rating);
        song.Rating.ShouldBe(3);
    }

    [Fact]
    public void SetRating_ValidValue_UpdatesUpdatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var song = new Song();
        song.SetRating(4);
        song.UpdatedAt.ShouldBeGreaterThan(before);
    }

    [Fact]
    public void SetRating_OutOfRange_DoesNotUpdateUpdatedAt()
    {
        var song = new Song { Rating = 2, UpdatedAt = new DateTime(2020, 1, 1) };
        song.SetRating(-1);
        song.UpdatedAt.ShouldBe(new DateTime(2020, 1, 1));
    }

    // --- ValidateFile ---

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
