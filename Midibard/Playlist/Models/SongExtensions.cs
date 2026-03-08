using System.IO;
using System.Text.RegularExpressions;

namespace MidiBard.Playlist;

/// <summary>
/// Extension methods for Song providing display formatting and name extraction utilities.
/// </summary>
public static class SongExtensions
{
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
        // TODO: crete option to choose the output format based on song fields
        // var input = song.Name ?? Path.GetFileName(song.FilePath);
        var input = Path.GetFileName(song.FilePath);
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
