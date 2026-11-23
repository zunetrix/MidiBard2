using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MidiBard.OnlineLibrary;

public sealed record SourceProgress
{
    public int LoadedPages { get; init; }
    public int TotalPages { get; init; }
    public bool Finished { get; init; }
    public string? Error { get; init; }
}

public sealed record SearchSnapshot
{
    public static SearchSnapshot Empty { get; } = new();

    public IReadOnlyList<OnlineFileEntry> Entries { get; init; } = [];
    public SourceProgress? BmpProgress { get; init; }
    public SourceProgress? XivmidiProgress { get; init; }
    public bool Truncated { get; init; }
    public bool Running { get; init; }
    public int SkippedNoUrl { get; init; }
}

/// <summary>
/// Runs multi-page searches against xivmidi and BMP and exposes the results as immutable snapshots.
/// </summary>
/// <remarks>
/// Each source pages in its own task on the thread pool. Starting a search cancels the previous one;
/// <see cref="Dispose"/> cancels whatever is running. Mutable state is guarded by <c>_lock</c>, and the UI
/// only reads <see cref="Snapshot"/>, which is replaced as a whole and never mutated.
/// </remarks>
public sealed class OnlineSearch : IDisposable
{
    private readonly MidiLibraryClient _client;
    private readonly object _lock = new();

    private SearchSnapshot _snapshot = SearchSnapshot.Empty;
    private CancellationTokenSource? _cts;
    private int _generation;
    private int _maxPagesPerSource;
    private List<OnlineFileEntry> _entries = [];
    private SourceProgress? _xivProgress;
    private SourceProgress? _bmpProgress;
    private bool _truncated;
    private bool _running;
    private int _skippedNoUrl;
    private bool _disposed;

    public OnlineSearch(MidiLibraryClient client)
    {
        _client = client;
    }

    public SearchSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void Start(SearchFilters filters)
    {
        int generation;
        CancellationToken token;
        var sources = ResolveSources(filters.Source);

        lock (_lock)
        {
            if (_disposed) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            generation = ++_generation;

            _xivProgress = sources.Contains(EntrySource.Xivmidi) ? new SourceProgress() : null;
            _bmpProgress = sources.Contains(EntrySource.Bmp) ? new SourceProgress() : null;

            // Page cap per source: 10 pages (1,000 entries) for a single source, 8 pages each when searching several.
            _maxPagesPerSource = sources.Count > 1 ? 8 : 10;
            _entries = [];
            _truncated = false;
            _running = true;
            _skippedNoUrl = 0;
            Publish();
        }

        foreach (var source in sources)
            _ = Task.Run(() => RunSourceAsync(source, filters, generation, token), token);
    }

    public void Cancel()
    {
        lock (_lock)
        {
            if (!_running) return;
            _cts?.Cancel();
            _generation++;
            _running = false;
            _truncated = true;
            Publish();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _generation++;
        }
    }

    private static IReadOnlyList<EntrySource> ResolveSources(SearchSource source) => source switch
    {
        SearchSource.All => Enum.GetValues<EntrySource>(),
        SearchSource.Xivmidi => [EntrySource.Xivmidi],
        SearchSource.Bmp => [EntrySource.Bmp],
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private async Task RunSourceAsync(EntrySource source, SearchFilters filters, int generation, CancellationToken ct)
    {
        try
        {
            for (var page = 1; ; page++)
            {
                var result = await _client.SearchAsync(source, filters, page, ct).ConfigureAwait(false);

                lock (_lock)
                {
                    if (generation != _generation) return;
                    var askNext = ApplyPage(source, page, result);
                    Publish();
                    if (!askNext) return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Superseded, cancelled or disposed: the state was already updated by whoever cancelled.
        }
        catch (Exception e)
        {
            if (e is not MidiLibraryException)
                DalamudApi.PluginLog.Warning(e, "[OnlineSearch] {0} search failed", source);

            lock (_lock)
            {
                if (generation != _generation) return;
                SetProgress(source, GetProgress(source)! with { Finished = true, Error = e.Message });
                UpdateRunning();
                Publish();
            }
        }
    }

    // Must be called under _lock. Returns whether the next page should be requested.
    private bool ApplyPage(EntrySource source, int page, SearchPage result)
    {
        _entries.AddRange(result.Entries);
        _skippedNoUrl += result.Skipped;

        var totalPages = Math.Max(page, result.TotalPages ?? 0);

        // An empty raw page stops the loop even if the server announced more, so a wrong total cannot make it run forever.
        // The raw count matters: a page whose entries were all skipped does not mean the server ran out of pages.
        var askNext = page < totalPages && page < _maxPagesPerSource && result.RawCount > 0;
        if (page >= _maxPagesPerSource && page < totalPages)
            _truncated = true;

        SetProgress(source, new SourceProgress { LoadedPages = page, TotalPages = totalPages, Finished = !askNext });
        UpdateRunning();
        return askNext;
    }

    private SourceProgress? GetProgress(EntrySource source) =>
        source == EntrySource.Xivmidi ? _xivProgress : _bmpProgress;

    private void SetProgress(EntrySource source, SourceProgress progress)
    {
        if (source == EntrySource.Xivmidi) _xivProgress = progress;
        else _bmpProgress = progress;
    }

    // Must be called under _lock.
    private void UpdateRunning()
    {
        if ((_xivProgress?.Finished ?? true) && (_bmpProgress?.Finished ?? true))
            _running = false;
    }

    // Must be called under _lock.
    private void Publish()
    {
        Volatile.Write(ref _snapshot, new SearchSnapshot
        {
            Entries = _entries.ToList(),
            BmpProgress = _bmpProgress,
            XivmidiProgress = _xivProgress,
            Truncated = _truncated,
            Running = _running,
            SkippedNoUrl = _skippedNoUrl,
        });
    }
}
