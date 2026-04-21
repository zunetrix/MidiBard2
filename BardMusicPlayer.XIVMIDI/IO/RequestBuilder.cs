using System;

namespace BardMusicPlayer.XIVMIDI.IO;

/// <summary>
/// Build the API request string for the BardMusicPlayer MIDI search API.
/// </summary>
public class RequestBuilder
{
    private readonly string ApiBaseUrl = "https://bardmusicplayer.com/api/midi-search";

    /// <summary>
    /// General search term (matches title, artist and source).
    /// </summary>
    public string Search { get; set; } = "";

    /// <summary>
    /// Filter by editor/arranger display name.
    /// </summary>
    public string Editor { get; set; } = "";

    /// <summary>
    /// Filter by ensemble size.
    /// Accepted values: solo, duo, trio, quartet, quintet, sextet, septet, octet.
    /// Use the <see cref="Misc.EnsembleSize"/> dictionary to map an index to the right string.
    /// </summary>
    public string Ensemble { get; set; } = "";

    /// <summary>
    /// Filter by source website.
    /// Example values: "xivmidi.com", "songs.bardmusicplayer.com".
    /// Leave empty to search across all sources.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Sort order.
    /// Accepted values:
    ///   -createdAt  → Newest First (default)
    ///    createdAt  → Oldest First
    ///    titleSort  → A–Z
    ///   -titleSort  → Z–A
    ///   -downloads  → Most Downloaded
    /// </summary>
    public string Sort { get; set; } = "-createdAt";

    /// <summary>
    /// Page number for paginated results.
    /// When not set (≤ 0) the API returns all records.
    /// </summary>
    public int Page { get; set; } = 0;

    /// <summary>
    /// Builds and returns the full query URL.
    /// </summary>
    public string BuildRequest()
    {
        var parts = new System.Collections.Generic.List<string>();

        if (!string.IsNullOrWhiteSpace(Search))
            parts.Add("search=" + Uri.EscapeDataString(Search));

        if (!string.IsNullOrWhiteSpace(Editor))
            parts.Add("editor=" + Uri.EscapeDataString(Editor));

        if (!string.IsNullOrWhiteSpace(Ensemble))
            parts.Add("ensemble=" + Uri.EscapeDataString(Ensemble));

        if (!string.IsNullOrWhiteSpace(Source))
            parts.Add("source=" + Uri.EscapeDataString(Source));

        if (!string.IsNullOrWhiteSpace(Sort))
            parts.Add("sort=" + Uri.EscapeDataString(Sort));

        if (Page > 0)
            parts.Add("page=" + Page);

        return parts.Count > 0
            ? ApiBaseUrl + "?" + string.Join("&", parts)
            : ApiBaseUrl;
    }
}
