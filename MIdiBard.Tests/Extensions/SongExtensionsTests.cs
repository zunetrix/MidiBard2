using MidiBard.Playlist;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Extensions;

public class SongExtensionsTests
{
    public SongExtensionsTests() => DalamudTestSetup.Initialize();

    // ExtractSongName 

    [Fact]
    public void ExtractSongName_EmptyCapturePattern_ReturnsInput()
    {
        SongExtensions.ExtractSongName("My Song", "", "$1", "", "").ShouldBe("My Song");
    }

    [Fact]
    public void ExtractSongName_EmptyOutputFormat_ReturnsInput()
    {
        SongExtensions.ExtractSongName("My Song", @"\[(\d+)\] (.+)", "", "", "").ShouldBe("My Song");
    }

    [Fact]
    public void ExtractSongName_MatchingPattern_ExtractsCaptureGroup()
    {
        SongExtensions.ExtractSongName("[001] My Song", @"\[(\d+)\] (.+)", "$2", "", "")
            .ShouldBe("My Song");
    }

    [Fact]
    public void ExtractSongName_MultipleGroups_FormatsOutput()
    {
        SongExtensions.ExtractSongName("Artist - Song Title", @"(.+) - (.+)", "$2 by $1", "", "")
            .ShouldBe("Song Title by Artist");
    }

    [Fact]
    public void ExtractSongName_WithFindReplace_AppliesSubstitution()
    {
        SongExtensions.ExtractSongName("[001] My_Song", @"\[(\d+)\] (.+)", "$2", "_", " ")
            .ShouldBe("My Song");
    }

    [Fact]
    public void ExtractSongName_NoMatch_ReturnsInputUnchanged()
    {
        SongExtensions.ExtractSongName("NoMatch", @"\[(\d+)\]", "$1", "", "")
            .ShouldBe("NoMatch");
    }

    [Fact]
    public void ExtractSongName_InvalidRegex_ReturnsInput()
    {
        // Invalid regex — exception silently swallowed, input returned as-is
        SongExtensions.ExtractSongName("My Song", "[invalid((", "$1", "", "")
            .ShouldBe("My Song");
    }

    [Fact]
    public void ExtractSongName_UnusedPlaceholders_Removed()
    {
        // Pattern matches one group; $2 has no match and should be removed
        SongExtensions.ExtractSongName("[001] Title", @"\[(\d+)\] .+", "$1 $2", "", "")
            .ShouldBe("001 ");
    }

    // GetFormattedName 

    [Fact]
    public void GetFormattedName_WithNameSet_UsesName()
    {
        var song = new Song { Name = "[001] My Song", FilePath = @"C:\music\other.mid" };
        song.GetFormattedName(@"\[(\d+)\] (.+)", "$2", "", "").ShouldBe("My Song");
    }

    [Fact]
    public void GetFormattedName_NullName_FallsBackToFilename()
    {
        var song = new Song { Name = null!, FilePath = @"C:\music\My Track.mid" };
        song.GetFormattedName("", "", "", "").ShouldBe("My Track.mid");
    }

    [Fact]
    public void GetFormattedName_EmptyPatterns_ReturnsNameAsIs()
    {
        var song = new Song { Name = "My Song" };
        song.GetFormattedName("", "", "", "").ShouldBe("My Song");
    }

    [Fact]
    public void GetFormattedName_EmptyName_UsesEmptyStringNotFilename()
    {
        // ?? only checks for null — empty string is used as-is (fallback requires null)
        var song = new Song { Name = string.Empty, FilePath = @"C:\music\track.mid" };
        song.GetFormattedName("", "", "", "").ShouldBe(string.Empty);
    }
}
