using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

using BardMusicPlayer.XIVMIDI;
using BardMusicPlayer.XIVMIDI.Events;
using BardMusicPlayer.XIVMIDI.IO;

namespace MidiBard.Managers;


public record SearchFilters(string Artist, string Title, string Editor, int Ensemble, SearchSource Source, SortOrder Sort);
public enum EntrySource
{
    Unknown,
    Xivmidi,
    Bmp
}

// Order matches SourceLabels in BardMusicLibraryWindow: the combo index is cast to this enum.
public enum SearchSource { Xivmidi, Bmp, Both }
public record SourceProgress
{
    public int LoadedPages { get; init; } = 0;
    public int TotalPages { get; init; } = 0;
    public bool Finished { get; init; } = false;
    public string? Error { get; init; } = null;
}

public record SearchSnapshot
{
    public static SearchSnapshot Empty { get; } = new()
    {
        Entries = [],
        BmpProgress = null,
        XivmidiProgress = null,
        Truncated = false,
        Running = false,
        SkippedNoUrl = 0
    };
    public IReadOnlyList<OnlineFileEntry> Entries { get; init; } = [];
    public SourceProgress? BmpProgress { get; init; }
    public SourceProgress? XivmidiProgress { get; init; }
    public bool Truncated { get; init; }
    public bool Running { get; init; }
    public int SkippedNoUrl { get; init; }
}

public record OnlineFileEntry
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

public enum BMLDownload
{
    Playback,
    ToPlaylist
}


/// <summary>
/// Runs multi-page searches against xivmidi and BMP and exposes the results as immutable snapshots.
/// </summary>
/// <remarks>
/// Event handlers run on the XIVMidiApi event thread. All mutable state is guarded by <c>_lock</c>.
/// The UI only reads <see cref="Snapshot"/>, which is replaced as a whole with Volatile.Write and never mutated.
/// Calls into the library (RequestPage, CancelDownloads) are made outside the lock.
/// </remarks>
public class OnlineSearch : IDisposable
{
    private const int PageSize = 100; // Must match the limit=100 hardcoded in XIVMIDIRequestBuilder and BMPAPIRequestBuilder.
    private readonly object _lock = new object();
    private SearchSnapshot _snapshot = SearchSnapshot.Empty;
    private int _maxPagesPerSource;
    private int _searchId = 0;
    private List<OnlineFileEntry> _entries = [];
    private SearchFilters? _filters;
    private SourceProgress? _xivProgress;
    private SourceProgress? _bmpProgress;
    private bool _truncated = false;
    private bool _running = false;
    private int _skippedNoUrl = 0;


    // Constructor + Dispose
    public OnlineSearch()
    {
        XIVMidiApi.Instance.OnBMPSongList += OnBmpSongList;
        XIVMidiApi.Instance.OnXIVSongList += OnXivSongList;
        XIVMidiApi.Instance.OnXIVRequestError += OnRequestError;
    }
    public void Dispose()
    {
        XIVMidiApi.Instance.OnBMPSongList -= OnBmpSongList;
        XIVMidiApi.Instance.OnXIVSongList -= OnXivSongList;
        XIVMidiApi.Instance.OnXIVRequestError -= OnRequestError;
    }

    // Public
    public void Start(SearchFilters filters)
    {
        int id;
        bool askBmp;
        bool askXiv;

        lock (_lock)
        {
            _searchId ++;
            _filters = filters;
            _entries = [];

            _xivProgress = filters.Source == SearchSource.Xivmidi || filters.Source == SearchSource.Both ? new SourceProgress() : null;
            _bmpProgress = filters.Source == SearchSource.Bmp || filters.Source == SearchSource.Both ? new SourceProgress() : null;

            _truncated = false;
            _running = true;
            _skippedNoUrl = 0;

            Publish();

            id = _searchId;
            askXiv = _xivProgress is not null;
            askBmp = _bmpProgress is not null;

            // Page cap per source: 10 pages (1,000 entries) for a single source, 8 pages each (1,600 entries) for Both.
            _maxPagesPerSource = filters.Source == SearchSource.Both ? 8 : 10;
        }

        if (askXiv)
            RequestPage(EntrySource.Xivmidi, 1, filters, id);

        if (askBmp)
            RequestPage(EntrySource.Bmp, 1, filters, id);
    }
    public void Cancel()
    {
        lock (_lock)
        {
            if (!_running) return;

            _searchId ++;
            _running = false;
            _truncated = true;
            Publish();
        }
        XIVMidiApi.Instance.CancelDownloads();
    }
    public SearchSnapshot Snapshot => Volatile.Read(ref _snapshot);

    // Event Handlers
    private void OnBmpSongList(object sender, XIVMidiBMPSongsEvent e)
    {
        if (e.Arguments is not int id) return;

        var data = e.Songs?.docs ?? [];

        List<OnlineFileEntry> entries = [];
        foreach (var song in data)
        {
            if (!string.IsNullOrWhiteSpace(song.url))
                entries.Add(OnlineEntryMapper.Map(song));
        }

        OnPageReceived(EntrySource.Bmp, id, entries, e.Songs?.totalPages, data.Count - entries.Count);

    }
    private void OnXivSongList(object sender, XIVMidiXIVSongsEvent e)
    {
        if (e.Arguments is not int id) return;

        var data = e.Songs?.data ?? [];

        List<OnlineFileEntry> entries = [];
        foreach (var song in data)
        {
            if (!string.IsNullOrWhiteSpace(song.download_url))
                entries.Add(OnlineEntryMapper.Map(song));
        }

        OnPageReceived(EntrySource.Xivmidi, id, entries, (e.Songs?.meta?.total + PageSize  - 1) / PageSize, data.Count - entries.Count);

    }
    private void OnRequestError(object sender, XIVMidiApiErrorEvent e)
    {
        DalamudApi.PluginLog.Warning("[XIVMidiApiErrorEvent] {requestKind} - {errorCode} - {message}", e.RequestKind, e.ErrorCode, e.Message);

        if (e.Arguments is not int arg) return;

        EntrySource source = e.RequestKind switch
        {
            RequestKind.SongListXivmidi => EntrySource.Xivmidi,
            RequestKind.SongListBmp => EntrySource.Bmp,
            _ => EntrySource.Unknown

        };

        if (source == EntrySource.Unknown)
            return;

        lock (_lock)
        {
            ref var progress = ref (source == EntrySource.Xivmidi ? ref _xivProgress : ref _bmpProgress);

            if (arg != _searchId) return;
            if (progress is null) return;
            if (progress.Finished) return;

            progress = progress with
            {
                Finished = true,
                Error = $"{e.ErrorCode}: {e.Message}"
            };

            UpdateRunning();
            Publish();
        }
    }


    // Private methods
    private void OnPageReceived(EntrySource source, int id, List<OnlineFileEntry> entries, int? announcedPages, int skippedNoUrl)
    {
        int nextPage = 0;
        SearchFilters? f = null;
        bool askNext = false;

        lock (_lock)
        {
            ref var progress = ref (source == EntrySource.Xivmidi ? ref _xivProgress : ref _bmpProgress);

            if (id != _searchId) return;
            if (progress is null) return;
            if (progress.Finished) return;

            foreach (var entry in entries)
            {
                _entries.Add(entry);
            }

            var loadedPages = progress.LoadedPages + 1;
            var totalPages = Math.Max(loadedPages, announcedPages ?? 0);

            // Test the raw page size (kept + skipped entries): a page whose entries were all skipped
            // does not mean the server has run out of pages. An empty page stops the loop even if
            // the server announced more, so a wrong total cannot make it run forever.
            askNext = loadedPages < totalPages && loadedPages < _maxPagesPerSource && entries.Count + skippedNoUrl > 0;
            var truncated = loadedPages >= _maxPagesPerSource && loadedPages < totalPages;
            if (truncated) _truncated = true;


            progress = progress with
            {
                LoadedPages = loadedPages,
                TotalPages = totalPages,
                Finished = !askNext
            };

            _skippedNoUrl += skippedNoUrl;

            UpdateRunning();
            Publish();

            if (askNext)
            {
                nextPage = loadedPages + 1;
                f = _filters;
            }

        }

        if (askNext)
        {
            RequestPage(source, nextPage, f, id);
        }

    }
    private void RequestPage(EntrySource source, int page, SearchFilters f, int searchId)
    {
        if (source == EntrySource.Xivmidi)
        {
            var request = new XIVMIDIRequestBuilder
            {
                Artist = f.Artist,
                Title = f.Title,
                Credit = f.Editor,
                bandSize = f.Ensemble,
                skip = (page - 1) * PageSize,
                Sort = f.Sort,
            };

            DalamudApi.PluginLog.Information($"[DEBUG] xivmidi url = {request.BuildRequest()}");

            XIVMidiApi.Instance.GetSonglist(request, args: searchId);


            return;
        }
        if (source == EntrySource.Bmp)
        {
            var request = new BMPAPIRequestBuilder
            {
                Search = (f.Artist + " " + f.Title).Trim(),
                Editor = f.Editor,
                bandSize = f.Ensemble,
                page = page,
                Sort = f.Sort
            };

            DalamudApi.PluginLog.Information($"[DEBUG] BMP url = {request.BuildRequest()}");

            XIVMidiApi.Instance.GetSonglist(request, args: searchId);


            return;
        }
    }

    // Must be called under lock (_lock) !
    private void UpdateRunning()
    {
        var bmpProgressFinished = _bmpProgress is null || _bmpProgress.Finished;
        var xivProgressFinished = _xivProgress is null || _xivProgress.Finished;

        if (bmpProgressFinished && xivProgressFinished)
        {
            _running = false;
        }
    }

    // Must be called under lock (_lock) !
    private void Publish()
    {
        var newSnapshot = new SearchSnapshot
        {
            Entries = _entries.ToList(),
            BmpProgress = _bmpProgress,
            XivmidiProgress = _xivProgress,
            Truncated = _truncated,
            Running = _running,
            SkippedNoUrl = _skippedNoUrl
        };

        Volatile.Write(ref _snapshot, newSnapshot);
    }

}

public static class OnlineEntryMapper
{
    public static readonly string[] EnsembleLabels =
    { "Any", "Solo", "Duo", "Trio", "Quartet", "Quintet", "Sextet", "Septet", "Octet" };

    public static OnlineFileEntry Map(BMPResponseContainer.Doc doc)
    {

        int downloads;
        if (!int.TryParse(doc.downloads, NumberStyles.Integer, CultureInfo.InvariantCulture, out downloads))
            downloads = 0;

        int? ensembleIndex;
        string ensembleText = NormalizeEnsemble(doc.ensembleSize, out ensembleIndex);


        return new OnlineFileEntry
        {
            Id = doc.id.ToString(),
            Artist = doc.artist ?? "",
            Title = doc.title ?? "",
            Arranger = doc.arranger ?? "",

            EnsembleSize = ensembleIndex,
            EnsembleText = ensembleText,

            DurationSeconds = doc.songDurationMs > 0 ? (int)(doc.songDurationMs / 1000) : null ,
            DurationText = doc.duration ?? "",

            PublishedAt = doc.createdAt == DateTime.MinValue ? null : doc.createdAt,
            PublishedText = doc.createdAt == DateTime.MinValue ? "" : doc.createdAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),

            DownloadsCount = downloads,
            LikesCount = doc.hearts,

            Url = doc.url ?? "",
            PageUrl = $"https://bardmusicplayer.com/midis#midi={doc.id}",

            Source = EntrySource.Bmp
        };
    }

    public static OnlineFileEntry Map(XIVMIDIResponseContainer.File file)
    {
        return new OnlineFileEntry
        {
            Id = file.id ?? "",
            Artist = file.artist ?? "",
            Title = file.title ?? "",
            Arranger = file.credit ?? "",

            EnsembleSize = file.bandsize > 0 && file.bandsize < 9 ? file.bandsize : null,
            EnsembleText = file.bandsize > 0 && file.bandsize < 9 ? EnsembleLabels[file.bandsize] : file.bandsize.ToString(),

            DurationSeconds = file.duration == 0 ? null : file.duration,
            DurationText = file.duration != 0 ? $"{file.duration / 60}:{(file.duration % 60).ToString("00")}" : "",

            PublishedAt = file.published_at == DateTime.MinValue ? null : file.published_at,
            PublishedText = file.published_at == DateTime.MinValue ? "" : file.published_at?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",

            DownloadsCount = file.download_count,
            LikesCount = file.like_count,

            Url = file.download_url ?? "",
            PageUrl = "",

            Source = EntrySource.Xivmidi
        };
    }

    private static string NormalizeEnsemble(string? raw, out int? index)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            index = null;
            return "";
        }

        var found = Array.FindIndex(EnsembleLabels, l =>
            string.Equals(l, raw, StringComparison.OrdinalIgnoreCase)
        );


        if (found > 0)
        {
            index = found;
            return EnsembleLabels[found];
        }

        index = null;
        return raw;
    }
}
