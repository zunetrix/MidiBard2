using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

using BardMusicPlayer.XIVMIDI;
using BardMusicPlayer.XIVMIDI.Events;
using BardMusicPlayer.XIVMIDI.IO;

namespace MidiBard.Managers;

public record SourceProgress
{
    public int LoadedPages { get; init; } = 0;
    public int TotalPages { get; init; } = 0;
    public bool Finished { get; init; } = false;
}

public record SearchSnapshot
{
    public static SearchSnapshot Empty { get; } = new()
    {
        Entries = [],
        BMP_Progress = null,
        XIVMIDI_Progress = null,
        Truncated = false,
        Running = false,
        ErrorMessage = null
    };
    public IReadOnlyList<OnlineFileEntry> Entries { get; init; } = [];
    public SourceProgress? BMP_Progress { get; init; }
    public SourceProgress? XIVMIDI_Progress { get; init; }
    public bool Truncated { get; init; }
    public bool Running { get; init; }
    public string? ErrorMessage { get; init; }
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

    public EntrySource Source { get; init; }
}

public class OnlineSearch : IDisposable
{
    private readonly object _lock = new object();
    private SearchSnapshot _snapshot = SearchSnapshot.Empty;
    public SearchSnapshot Snapshot => Volatile.Read(ref _snapshot);
    private const int PageSize = 100;
    private int _maxPagesPerSource;


    private int _search_id = 0;
    private List<OnlineFileEntry> _entries = [];
    private SearchFilters? _filters;
    private SourceProgress? _xivProgress;
    private SourceProgress? _bmpProgress;
    private bool _truncated = false;
    private bool _running = false;
    private string? _error;


    private void Publish()
    {
        var newSnapshot = new SearchSnapshot
        {
            Entries = _entries.ToList(),
            BMP_Progress = _bmpProgress,
            XIVMIDI_Progress = _xivProgress,
            Truncated = _truncated,
            Running = _running,
            ErrorMessage = _error
        };

        Volatile.Write(ref _snapshot, newSnapshot);
    }

    public void Start(SearchFilters filters)
    {
        int id;
        bool askBmp;
        bool askXiv;

        lock (_lock)
        {
            _search_id ++;
            _filters = filters;
            _entries = [];

            _xivProgress = filters.Source == 0 || filters.Source == 2 ? new SourceProgress() : null;
            _bmpProgress = filters.Source == 1 || filters.Source == 2 ? new SourceProgress() : null;

            _truncated = false;
            _running = true;
            _error = null;

            Publish();

            id = _search_id;
            askXiv = _xivProgress is not null;
            askBmp = _bmpProgress is not null;

            _maxPagesPerSource = filters.Source == 2 ? 8 : 10;
        }

        if (askXiv)
            RequestPage(EntrySource.Xivmidi, 1, filters, id);

        if (askBmp)
            RequestPage(EntrySource.Bmp, 1, filters, id);
    }

    public OnlineSearch()
    {
        XIVMidiApi.Instance.OnBMPSongList += OnBmpSongList;
        XIVMidiApi.Instance.OnXIVSongList += OnXivSongList;
        // XIVMidiApi.Instance.OnXIVRequestError += ;
    }
    public void Dispose()
    {
        XIVMidiApi.Instance.OnBMPSongList -= OnBmpSongList;
        XIVMidiApi.Instance.OnXIVSongList -= OnXivSongList;
        // XIVMidiApi.Instance.OnXIVRequestError -= ;
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
                Sort = (SortOrder)f.Sort,
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
                Sort = (SortOrder)f.Sort
            };

            DalamudApi.PluginLog.Information($"[DEBUG] BMP url = {request.BuildRequest()}");

            XIVMidiApi.Instance.GetSonglist(request, args: searchId);


            return;
        }
    }

    private void OnBmpSongList(object sender, XIVMidiBMPSongsEvent e)
    {
        if (e.Arguments is int id)
        {

        }
        else return;

        var data = e.Songs?.docs ?? [];

        List<OnlineFileEntry> entries = [];
        foreach (var song in data)
        {
            entries.Add(OnlineEntryMapper.Map(song));
        }

        OnPageReceived(EntrySource.Bmp, id, entries, e.Songs?.totalPages);

    }

    private void OnXivSongList(object sender, XIVMidiXIVSongsEvent e)
    {
        if (e.Arguments is int id)
        {

        }
        else return;

        var data = e.Songs?.data ?? [];

        List<OnlineFileEntry> entries = [];
        foreach (var song in data)
        {
            entries.Add(OnlineEntryMapper.Map(song));
        }

        OnPageReceived(EntrySource.Xivmidi, id, entries, (e.Songs?.meta?.total + PageSize  - 1) / PageSize);

    }

    private void OnPageReceived(EntrySource source, int id, List<OnlineFileEntry> entries, int? announcedPages)
    {
        int nextPage = 0;
        SearchFilters? f = null;
        bool askNext = false;

        lock (_lock)
        {
            ref var progress = ref (source == EntrySource.Xivmidi ? ref _xivProgress : ref _bmpProgress);

            if (id != _search_id) return;
            if (progress is null) return;
            if (progress.Finished == true) return;

            foreach (var entry in entries)
            {
                _entries.Add(entry);
            }

            var loadedPages = progress.LoadedPages + 1;
            var totalPages = Math.Max(loadedPages, announcedPages ?? 0);

            askNext = loadedPages < totalPages && loadedPages < _maxPagesPerSource && entries.Count > 0;
            var truncated = loadedPages >= _maxPagesPerSource && loadedPages < totalPages;
            if (truncated) _truncated = true;


            progress = progress with
            {
                LoadedPages = loadedPages,
                TotalPages = totalPages,
                Finished = !askNext
            };


            var bmpProgressFinished = _bmpProgress is null || _bmpProgress.Finished is true;
            var xivProgressFinished = _xivProgress is null || _xivProgress.Finished is true;

            if (bmpProgressFinished && xivProgressFinished)
            {
                _running = false;
            }

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
            PublishedText = file.published_at == DateTime.MinValue ? null : file.published_at?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",

            DownloadsCount = file.download_count,
            LikesCount = file.like_count,

            Url = file.download_url ?? "",

            Source = EntrySource.Xivmidi
        };
    }


    private static string NormalizeEnsemble(string? raw, out int? Index)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            Index = null;
            return "";
        }

        var index = Array.FindIndex(EnsembleLabels, l =>
            string.Equals(l, raw, StringComparison.OrdinalIgnoreCase)
        );


        if (index > 0)
        {
            Index = index;
            return EnsembleLabels[index];
        }

        Index = null;
        return raw;
    }
}
