using System.IO;
using System.Text.RegularExpressions;

namespace MidiBard.Playlist;

/// <summary>
/// Extension methods for Song providing display formatting and name extraction utilities.
/// </summary>
public static class SongExtensions
{
    /// <summary>
    /// Format a chat message from a template string using {Token} placeholders
    /// filled from the song's database fields.
    /// Supported tokens: {SongName} {Artist} {Year} {Duration} {Comments} {Tag[0]} {Tag[1]} ...
    /// </summary>
    public static string FormatFromTemplate(this Song song, string template)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var result = template;
        result = result.Replace("{SongName}", song.Name ?? string.Empty);
        result = result.Replace("{Artist}", song.Artist ?? string.Empty);
        result = result.Replace("{Year}", song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : string.Empty);
        result = result.Replace("{Duration}", song.Duration != System.TimeSpan.Zero
            ? song.Duration.ToString(@"m\:ss") : string.Empty);
        result = result.Replace("{Comments}", song.Comments ?? string.Empty);

        if (song.Tags != null)
        {
            for (int i = 0; i < song.Tags.Count; i++)
                result = result.Replace($"{{Tag[{i}]}}", song.Tags[i]?.Name ?? string.Empty);
        }

        // Remove any unresolved {Tag[n]} placeholders
        result = Regex.Replace(result, @"\{Tag\[\d+\]\}", string.Empty);

        return result;
    }

    /// <summary>
    /// Get a formatted display name for a song using regex-based extraction and transformation.
    /// </summary>
    /// <param name="song">The song to format.</param>
    /// <param name="capturePattern">Regex pattern to extract parts of the song name.</param>
    /// <param name="capturedOutputFormat">Format template with ${1}, ${2}, etc. for captured groups.</param>
    /// <param name="findPattern">Optional regex pattern for additional find/replace.</param>
    /// <param name="replacement">Replacement pattern for find/replace.</param>
    /// <returns>Formatted song name, or original name if extraction fails.</returns>
    public static string GetFormattedName(
        this Song song,
        string capturePattern,
        string capturedOutputFormat,
        string findPattern,
        string replacement)
    {
        var input = Path.GetFileNameWithoutExtension(song.FilePath);
        return ExtractSongName(input, capturePattern, capturedOutputFormat, findPattern, replacement);
    }

    /// <summary>
    /// Regex-based song name extraction and formatting utility.
    /// Extracts portions of an input string using capture groups and formats them.
    /// </summary>
    public static string ExtractSongName(
        string input,
        string capturePattern,
        string capturedOutputReplacement,
        string findPattern,
        string replacement)
    {
        if (string.IsNullOrEmpty(capturePattern) || string.IsNullOrEmpty(capturedOutputReplacement))
            return input;

        try
        {
            return Regex.Replace(input, capturePattern, match =>
            {
                string result = capturedOutputReplacement;

                for (int i = match.Groups.Count - 1; i >= 1; i--)
                {
                    result = result.Replace($"${i}", match.Groups[i].Value);
                }

                result = Regex.Replace(result, @"\$\d+", "");

                if (!string.IsNullOrEmpty(findPattern))
                {
                    result = Regex.Replace(result, findPattern, replacement);
                }

                return result;
            });
        }
        catch
        {
            return input;
        }
    }
}
