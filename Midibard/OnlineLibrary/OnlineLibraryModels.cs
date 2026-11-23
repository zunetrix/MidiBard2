using System;
using System.Collections.Generic;

namespace MidiBard.OnlineLibrary;

public enum EntrySource
{
    Xivmidi,
    Bmp
}

/// <summary>Which libraries a search covers. <see cref="All"/> covers every <see cref="EntrySource"/>.</summary>
public enum SearchSource
{
    All,
    Xivmidi,
    Bmp
}

/// <summary>Order the servers return results in. Only matters for which entries come first when a search is truncated.</summary>
public enum SortOrder
{
    Newest,
    Oldest,
    TitleAsc,
    TitleDesc,
    MostDownloaded
}

/// <param name="Ensemble">0 for any size, otherwise the number of performers (1-8).</param>
public sealed record SearchFilters(string Artist, string Title, string Editor, int Ensemble, SearchSource Source, SortOrder Sort);

public sealed record OnlineFileEntry
{
    public string Id { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public string Arranger { get; init; } = "";

    public int? EnsembleSize { get; init; }
    public string EnsembleText { get; init; } = "";

    public int? DurationSeconds { get; init; }
    public string DurationText { get; init; } = "";

    public DateTime? PublishedAt { get; init; }
    public string PublishedText { get; init; } = "";

    public int DownloadsCount { get; init; }
    public int LikesCount { get; init; }

    public string Url { get; init; } = "";
    public string PageUrl { get; init; } = "";

    public EntrySource Source { get; init; }
}

/// <summary>One page of search results.</summary>
/// <param name="Entries">Entries that have a download URL.</param>
/// <param name="TotalPages">Page count announced by the server, when it gives one.</param>
/// <param name="RawCount">Entries the server returned, including the skipped ones. 0 means the server ran out of pages.</param>
public sealed record SearchPage(IReadOnlyList<OnlineFileEntry> Entries, int? TotalPages, int RawCount)
{
    public int Skipped => RawCount - Entries.Count;
}

public sealed record DownloadedMidi(string FileName, byte[] Data);

/// <summary>A request to an online library failed. The message is meant to be shown to the user.</summary>
public sealed class MidiLibraryException(string message, Exception? inner = null) : Exception(message, inner);

public static class EnsembleSizes
{
    /// <summary>Index = number of performers, 0 = any.</summary>
    public static readonly string[] Labels =
        { "Any", "Solo", "Duo", "Trio", "Quartet", "Quintet", "Sextet", "Septet", "Octet" };

    public static bool IsValid(int size) => size is > 0 and < 9;
}
