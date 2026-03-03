using System.IO;

using MidiBard.Playlist;
using MidiBard.Playlist.Services;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Services;

/// <summary>
/// Integration tests for MidiFileService using the real test.mid file.
/// The file is copied to the output directory via the csproj CopyToOutputDirectory setting.
/// </summary>
public class MidiFileServiceTests
{
    private static readonly string TestMidiPath =
        Path.Combine(AppContext.BaseDirectory, "data", "test.mid");

    private readonly MidiFileService _service;

    public MidiFileServiceTests()
    {
        DalamudTestSetup.Initialize();
        _service = new MidiFileService();
    }

    // --- LoadMidiFile(string) ---

    [Fact]
    public void LoadMidiFile_ValidFile_ReturnsNonNull()
    {
        var result = _service.LoadMidiFile(TestMidiPath);
        result.ShouldNotBeNull();
    }

    [Fact]
    public void LoadMidiFile_EmptyPath_ReturnsNull()
    {
        var result = _service.LoadMidiFile(string.Empty);
        result.ShouldBeNull();
    }

    [Fact]
    public void LoadMidiFile_NonExistentFile_ReturnsNull()
    {
        var result = _service.LoadMidiFile(@"C:\nonexistent\ghost.mid");
        result.ShouldBeNull();
    }

    // --- LoadMidiFile(Stream) ---

    [Fact]
    public void LoadMidiFile_FromStream_ReturnsNonNull()
    {
        using var stream = File.OpenRead(TestMidiPath);
        var result = _service.LoadMidiFile(stream);
        result.ShouldNotBeNull();
    }

    [Fact]
    public void LoadMidiFile_NullStream_ReturnsNull()
    {
        var result = _service.LoadMidiFile((Stream)null!);
        result.ShouldBeNull();
    }

    // --- CalculateDuration ---

    [Fact]
    public void CalculateDuration_ValidFile_ReturnsPositiveDuration()
    {
        var midiFile = _service.LoadMidiFile(TestMidiPath);
        midiFile.ShouldNotBeNull();

        var duration = _service.CalculateDuration(midiFile!);

        duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void CalculateDuration_NullFile_ReturnsZero()
    {
        var duration = _service.CalculateDuration(null!);
        duration.ShouldBe(TimeSpan.Zero);
    }

    // --- CalculateDurationFromFileAsync ---

    [Fact]
    public async Task CalculateDurationFromFileAsync_ValidFile_ReturnsPositiveDuration()
    {
        var duration = await _service.CalculateDurationFromFileAsync(TestMidiPath);
        duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task CalculateDurationFromFileAsync_NonExistentFile_ReturnsZero()
    {
        var duration = await _service.CalculateDurationFromFileAsync(@"C:\nonexistent\ghost.mid");
        duration.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task CalculateDurationFromFileAsync_MatchesSyncCalculation()
    {
        var midiFile = _service.LoadMidiFile(TestMidiPath);
        var syncDuration = _service.CalculateDuration(midiFile!);

        var asyncDuration = await _service.CalculateDurationFromFileAsync(TestMidiPath);

        asyncDuration.ShouldBe(syncDuration);
    }

    // --- ValidateMidiFile ---

    [Fact]
    public void ValidateMidiFile_ValidFile_ReturnsTrue()
    {
        var (isValid, error) = _service.ValidateMidiFile(TestMidiPath);

        isValid.ShouldBeTrue();
        error.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateMidiFile_EmptyPath_ReturnsFalse()
    {
        var (isValid, error) = _service.ValidateMidiFile(string.Empty);

        isValid.ShouldBeFalse();
        error.ShouldNotBeEmpty();
    }

    [Fact]
    public void ValidateMidiFile_NonExistentFile_ReturnsFalse()
    {
        var (isValid, error) = _service.ValidateMidiFile(@"C:\nonexistent\ghost.mid");

        isValid.ShouldBeFalse();
        error.ShouldNotBeEmpty();
    }

    [Fact]
    public void ValidateMidiFile_WrongExtension_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName(); // creates a .tmp file
        try
        {
            var (isValid, error) = _service.ValidateMidiFile(tempFile);

            isValid.ShouldBeFalse();
            error.ShouldNotBeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- ExtractSongNameFromMidi ---

    [Fact]
    public void ExtractSongNameFromMidi_ValidFile_ReturnsNonEmpty()
    {
        var name = _service.ExtractSongNameFromMidi(TestMidiPath);
        name.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ExtractSongNameFromMidi_EmptyPath_ReturnsEmpty()
    {
        var name = _service.ExtractSongNameFromMidi(string.Empty);
        name.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractSongNameFromMidi_NonExistentFile_FallsBackToFilename()
    {
        var name = _service.ExtractSongNameFromMidi(@"C:\songs\my_song.mid");
        name.ShouldBe("my_song");
    }

    // --- CalculateAllDurationsAsync ---

    [Fact]
    public async Task CalculateAllDurationsAsync_ValidSong_SetsPositiveDuration()
    {
        var song = new Song { Id = 1, FilePath = TestMidiPath, Duration = TimeSpan.Zero };

        await _service.CalculateAllDurationsAsync(new List<Song> { song });

        song.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task CalculateAllDurationsAsync_EmptyList_CompletesWithoutError()
    {
        await _service.CalculateAllDurationsAsync(new List<Song>());
        // No exception = pass
    }

    [Fact]
    public async Task CalculateAllDurationsAsync_NullList_CompletesWithoutError()
    {
        await _service.CalculateAllDurationsAsync(null!);
        // No exception = pass
    }

    [Fact]
    public async Task CalculateAllDurationsAsync_SkipsSongWithExistingDuration()
    {
        var existingDuration = TimeSpan.FromMinutes(5);
        var song = new Song { Id = 1, FilePath = TestMidiPath, Duration = existingDuration };

        await _service.CalculateAllDurationsAsync(new List<Song> { song });

        song.Duration.ShouldBe(existingDuration);
    }

    [Fact]
    public async Task CalculateAllDurationsAsync_SkipsSongWithEmptyPath()
    {
        var song = new Song { Id = 1, FilePath = "", Duration = TimeSpan.Zero };

        await _service.CalculateAllDurationsAsync(new List<Song> { song });

        song.Duration.ShouldBe(TimeSpan.Zero);
    }
}
