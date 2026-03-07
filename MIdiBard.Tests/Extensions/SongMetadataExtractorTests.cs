using MidiBard.Playlist.Helpers;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Extensions;

public class SongMetadataExtractorTests
{
    public SongMetadataExtractorTests() => DalamudTestSetup.Initialize();

    [Fact]
    public void Extract_SongNameAndArtist_UsesFirstMatchingRulePerField()
    {
        var rules = new List<ExtractionRule>
        {
            new()
            {
                Field = ExtractionField.Artist,
                Enabled = true,
                RegexPattern = @"^(.+?)\s*-\s*",
                OutputFormat = "$1",
            },
            new()
            {
                Field = ExtractionField.SongName,
                Enabled = true,
                RegexPattern = @"^.+?\s*-\s*(.+)$",
                OutputFormat = "$1",
            },
            new()
            {
                Field = ExtractionField.SongName,
                Enabled = true,
                RegexPattern = @"^(.+)$",
                OutputFormat = "should-not-win",
            },
        };

        var result = SongMetadataExtractor.Extract("Beethoven - Moonlight Sonata", rules);

        result.Artist.ShouldBe("Beethoven");
        result.SongName.ShouldBe("Moonlight Sonata");
    }

    [Fact]
    public void Extract_WithSanitizePatternAndReplacement_ReplacesAllMatches()
    {
        var rules = new List<ExtractionRule>
        {
            new()
            {
                Field = ExtractionField.SongName,
                Enabled = true,
                RegexPattern = @"^(.+)$",
                OutputFormat = "$1",
                SanitizePattern = @"[_\-()]",
                SanitizeReplacement = " ",
            },
        };

        var result = SongMetadataExtractor.Extract("My_Song-(Live)", rules);

        result.SongName.ShouldBe("My Song  Live");
    }

    [Fact]
    public void Extract_WithSanitizePatternAndEmptyReplacement_RemovesMatches()
    {
        var rules = new List<ExtractionRule>
        {
            new()
            {
                Field = ExtractionField.SongName,
                Enabled = true,
                RegexPattern = @"^(.+)$",
                OutputFormat = "$1",
                SanitizePattern = @"[_\-()]",
                SanitizeReplacement = null,
            },
        };

        var result = SongMetadataExtractor.Extract("My_Song-(Live)", rules);

        result.SongName.ShouldBe("MySongLive");
    }

    [Fact]
    public void Extract_Tags_WithSeparator_SplitsAndAggregatesFromMultipleRules()
    {
        var rules = new List<ExtractionRule>
        {
            new()
            {
                Field = ExtractionField.Tags,
                Enabled = true,
                RegexPattern = @"_([^_]+)_",
                OutputFormat = "$1",
                Separator = ",",
            },
            new()
            {
                Field = ExtractionField.Tags,
                Enabled = true,
                RegexPattern = @"\[(.+?)\]",
                OutputFormat = "$1",
                Separator = "|",
            },
        };

        var result = SongMetadataExtractor.Extract("Song _jazz,live_ [piano|solo]", rules);

        result.Tags.ShouldBe(new List<string> { "jazz", "live", "piano", "solo" });
    }

    [Fact]
    public void Extract_Rating_OnlyAcceptsRangeZeroToFive()
    {
        var rules = new List<ExtractionRule>
        {
            new()
            {
                Field = ExtractionField.Rating,
                Enabled = true,
                RegexPattern = @"rate=(\d+)",
                OutputFormat = "$1",
            },
        };

        var valid = SongMetadataExtractor.Extract("name rate=5", rules);
        var invalid = SongMetadataExtractor.Extract("name rate=9", rules);

        valid.Rating.ShouldBe(5);
        invalid.Rating.ShouldBeNull();
    }
}
