using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MidiBard.Playlist.Helpers;

public record SongMetadata
{
    public string? SongName { get; init; }
    public string? Artist { get; init; }
    public int? ReleaseYear { get; init; }
    public int? Rating { get; init; }
    public string? Comments { get; init; }
    public List<string> Tags { get; init; } = new();
}

public static class SongMetadataExtractor
{
    public static SongMetadata Extract(string filename, IEnumerable<ExtractionRule> rules)
    {
        string? songName = null;
        string? artist = null;
        int? releaseYear = null;
        int? rating = null;
        string? comments = null;
        var tags = new List<string>();

        var activeRules = rules
            .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.RegexPattern))
            .ToList();

        foreach (ExtractionField field in Enum.GetValues<ExtractionField>())
        {
            var fieldRules = activeRules.Where(r => r.Field == field);

            if (field == ExtractionField.Tags)
            {
                foreach (var rule in fieldRules)
                {
                    var opts = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                    var m = Regex.Match(filename, rule.RegexPattern, opts);
                    if (!m.Success) continue;

                    var raw = Sanitize(m.Result(rule.OutputFormat ?? "$1"), rule);
                    if (string.IsNullOrEmpty(raw)) continue;

                    if (!string.IsNullOrEmpty(rule.Separator))
                        tags.AddRange(raw.Split(rule.Separator, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(t => t.Trim())
                                        .Where(t => t.Length > 0));
                    else
                        tags.Add(raw);
                }
            }
            else
            {
                // First match wins for scalar fields
                foreach (var rule in fieldRules)
                {
                    var opts = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                    var m = Regex.Match(filename, rule.RegexPattern, opts);
                    if (!m.Success) continue;

                    var value = Sanitize(m.Result(rule.OutputFormat ?? "$1"), rule);
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    switch (field)
                    {
                        case ExtractionField.SongName:
                            songName = value;
                            break;
                        case ExtractionField.Artist:
                            artist = value;
                            break;
                        case ExtractionField.ReleaseYear:
                            if (int.TryParse(value, out var year)) releaseYear = year;
                            break;
                        case ExtractionField.Rating:
                            if (int.TryParse(value, out var r) && r >= 0 && r <= 5) rating = r;
                            break;
                        case ExtractionField.Comments:
                            comments = value;
                            break;
                    }
                    break; // first match wins
                }
            }
        }

        return new SongMetadata
        {
            SongName = songName,
            Artist = artist,
            ReleaseYear = releaseYear,
            Rating = rating,
            Comments = comments,
            Tags = tags,
        };
    }

    internal static string Sanitize(string value, ExtractionRule rule)
    {
        if (!string.IsNullOrEmpty(rule.SanitizePattern))
        {
            var opts = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            value = Regex.Replace(value, rule.SanitizePattern, "", opts);
        }
        return value.Trim();
    }
}
