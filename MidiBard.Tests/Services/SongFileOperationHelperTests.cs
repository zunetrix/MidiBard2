using System.Collections.Generic;

using MidiBard.Playlist.Helpers;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Services;

/// <summary>
/// Tests for SongFileOperationHelper static helpers and the metadata extraction flow
/// that was broken when [ID] was not stripped before applying regex rules.
/// </summary>
public class SongFileOperationHelperTests
{
    public SongFileOperationHelperTests()
    {
        DalamudTestSetup.Initialize();
    }

    // ==================== ExtractSyncId ====================

    [Fact]
    public void ExtractSyncId_WithTrailingBracket_ReturnsId()
    {
        var result = SongFileOperationHelper.ExtractSyncId("My Song [42]");
        result.ShouldBe(42);
    }

    [Fact]
    public void ExtractSyncId_WithSpaceBeforeBracket_ReturnsId()
    {
        var result = SongFileOperationHelper.ExtractSyncId("8x - Rock - Song Name (Artist) 2023 [7]");
        result.ShouldBe(7);
    }

    [Fact]
    public void ExtractSyncId_NoTrailingBracket_ReturnsNull()
    {
        var result = SongFileOperationHelper.ExtractSyncId("My Song Without Id");
        result.ShouldBeNull();
    }

    [Fact]
    public void ExtractSyncId_MidStringBracket_ReturnsNull()
    {
        // [N] mid-string should NOT be treated as the SyncId stamp
        var result = SongFileOperationHelper.ExtractSyncId("My [42] Song");
        result.ShouldBeNull();
    }

    [Fact]
    public void ExtractSyncId_EmptyString_ReturnsNull()
    {
        SongFileOperationHelper.ExtractSyncId("").ShouldBeNull();
        SongFileOperationHelper.ExtractSyncId(null!).ShouldBeNull();
    }

    // ==================== CleanNameFromSyncId ====================

    [Fact]
    public void CleanNameFromSyncId_WithId_RemovesTrailingStamp()
    {
        var result = SongFileOperationHelper.CleanNameFromSyncId("My Song [42]");
        result.ShouldBe("My Song");
    }

    [Fact]
    public void CleanNameFromSyncId_FullFormatWithId_RemovesStampOnly()
    {
        var input = "8x - Rock - Song Name (Artist) 2023 [7]";
        var result = SongFileOperationHelper.CleanNameFromSyncId(input);
        result.ShouldBe("8x - Rock - Song Name (Artist) 2023");
    }

    [Fact]
    public void CleanNameFromSyncId_NoId_IsNoOp()
    {
        var input = "8x - Rock - Song Name (Artist) 2023";
        var result = SongFileOperationHelper.CleanNameFromSyncId(input);
        result.ShouldBe(input);
    }

    [Fact]
    public void CleanNameFromSyncId_EmptyString_ReturnsEmptyString()
    {
        SongFileOperationHelper.CleanNameFromSyncId("").ShouldBe("");
    }

    // ==================== BuildStampedFileName ====================

    [Fact]
    public void BuildStampedFileName_ProducesExpectedFormat()
    {
        var result = SongFileOperationHelper.BuildStampedFileName("My Song", 42, ".mid");
        result.ShouldBe("My Song [42].mid");
    }

    // ==================== Bug 1: metadata extraction must use clean filename ====================
    // Regression: previously the raw filename (with [ID]) was passed to SongMetadataExtractor,
    // causing regex patterns that don't account for [ID] to produce wrong or empty results.
    //
    // The invariant being tested: CleanNameFromSyncId strips the [ID] so that a rule written
    // to match "Title" at the end of string works regardless of whether the file was stamped.
    // We use a simple end-anchored pattern to demonstrate this clearly.

    [Theory]
    [InlineData("Title Song [42]")]
    [InlineData("Title Song")]
    public void SongMetadataExtractor_CleanFilename_AllowsEndAnchoredRuleToMatch(string rawFilename)
    {
        // Rule: capture everything - should match completely after [ID] is stripped.
        var cleanName = SongFileOperationHelper.CleanNameFromSyncId(rawFilename);
        var rules = new List<ExtractionRule>
        {
            new ExtractionRule
            {
                Field        = ExtractionField.SongName,
                Enabled      = true,
                Label        = "Song name",
                RegexPattern = @"^(.+)$",   // captures full string - fails if [ID] leaks in
                OutputFormat = "$1",
                IgnoreCase   = true,
            }
        };

        var metadata = SongMetadataExtractor.Extract(cleanName, rules);

        // After stripping [ID], "Title Song [42]" → "Title Song" → matched by ^(.+)$
        metadata.SongName.ShouldBe("Title Song",
            $"Extraction should work the same whether [ID] was present or not. Input: '{rawFilename}'");
    }

    [Theory]
    [InlineData("Song - (Artist) [42]", "Artist")]
    [InlineData("Song - (Artist)", "Artist")]
    public void SongMetadataExtractor_WithOrWithoutId_ExtractsSameArtist(string rawFilename, string expectedArtist)
    {
        var cleanName = SongFileOperationHelper.CleanNameFromSyncId(rawFilename);
        var rules = new List<ExtractionRule>
        {
            new ExtractionRule
            {
                Field        = ExtractionField.Artist,
                Enabled      = true,
                Label        = "Artist",
                RegexPattern = @"\(([^)]+)\)\s*$",  // captures last (...) at end-of-clean-string
                OutputFormat = "$1",
                IgnoreCase   = true,
            }
        };

        var metadata = SongMetadataExtractor.Extract(cleanName, rules);

        metadata.Artist.ShouldBe(expectedArtist,
            $"Artist extraction should not be affected by [ID]. Input: '{rawFilename}'");
    }

    [Theory]
    [InlineData("Song Name 2023 [42]", 2023)]
    [InlineData("Song Name 2023", 2023)]
    public void SongMetadataExtractor_WithOrWithoutId_ExtractsSameYear(string rawFilename, int expectedYear)
    {
        var cleanName = SongFileOperationHelper.CleanNameFromSyncId(rawFilename);
        var rules = new List<ExtractionRule>
        {
            new ExtractionRule
            {
                Field        = ExtractionField.ReleaseYear,
                Enabled      = true,
                Label        = "Year",
                RegexPattern = @"(\d{4})\s*$",  // trailing 4-digit year at end-of-clean-string
                OutputFormat = "$1",
                IgnoreCase   = true,
            }
        };

        var metadata = SongMetadataExtractor.Extract(cleanName, rules);

        metadata.ReleaseYear.ShouldBe(expectedYear,
            $"Year extraction should not be affected by [ID]. Input: '{rawFilename}'");
    }

    // ==================== Bug 3: SyncId lookup anchor ====================
    // Verifies that ExtractSyncId returns the correct anchor value so the caller
    // can use it to query the DB BEFORE creating a new record.

    [Fact]
    public void ExtractSyncId_IsTheCorrectAnchorForDbLookup()
    {
        // Simulate a renamed file: "Old Name [42].mid" → "New Name [42].mid"
        // The embedded ID (42) must survive the rename and be extractable from the new name.
        var oldRaw = "Old Name [42]";
        var newRaw = "New Name [42]";

        var oldId = SongFileOperationHelper.ExtractSyncId(oldRaw);
        var newId = SongFileOperationHelper.ExtractSyncId(newRaw);

        oldId.ShouldBe(42);
        newId.ShouldBe(42);
        oldId.ShouldBe(newId, "SyncId must be the same after rename so DB lookup still resolves the original record");
    }

    [Fact]
    public void CleanNameFromSyncId_AfterRename_ReturnsNewDisplayName()
    {
        // The display name (used as song.Name) should reflect the new filename, not the old one.
        var newRaw = "New Name [42]";
        var newClean = SongFileOperationHelper.CleanNameFromSyncId(newRaw);

        newClean.ShouldBe("New Name");
    }
}
