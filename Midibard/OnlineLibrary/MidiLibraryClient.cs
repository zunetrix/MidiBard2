using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

namespace MidiBard.OnlineLibrary;

/// <summary>
/// HTTP client for the xivmidi and bardmusicplayer MIDI libraries. Every call is async and cancellable;
/// <see cref="Dispose"/> aborts the requests in flight.
/// </summary>
public sealed class MidiLibraryClient : IDisposable
{
    public const int PageSize = 100;
    private const int MaxResponseBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    private volatile bool _disposed;

    public MidiLibraryEndpoints Endpoints { get; }

    public MidiLibraryClient(MidiLibraryEndpoints endpoints, string userAgent)
    {
        Endpoints = endpoints;

        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            MaxAutomaticRedirections = 3,
            MaxConnectionsPerServer = 4,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30), // Up to the response headers; the body read has its own limit.
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The token source is cancelled but not disposed: callers still holding its token must not throw ObjectDisposedException.
        _lifetime.Cancel();
        _http.Dispose();
    }

    // Search
    public Task<SearchPage> SearchAsync(EntrySource source, SearchFilters filters, int page, CancellationToken ct) =>
        source switch
        {
            EntrySource.Xivmidi => SearchXivmidiAsync(filters, page, ct),
            EntrySource.Bmp => SearchBmpAsync(filters, page, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private async Task<SearchPage> SearchXivmidiAsync(SearchFilters f, int page, CancellationToken ct)
    {
        var sort = f.Sort switch
        {
            SortOrder.Oldest => "date_asc",
            SortOrder.TitleAsc => "title_asc",
            SortOrder.TitleDesc => "title_desc",
            SortOrder.MostDownloaded => "downloads_desc",
            _ => null,
        };

        var query = new QueryBuilder()
            .Add("limit", PageSize)
            .Add("skip", page > 1 ? (page - 1) * PageSize : null)
            .Add("credit", f.Editor)
            .Add("artist", f.Artist)
            .Add("title", f.Title)
            .Add("bandsize", EnsembleSizes.IsValid(f.Ensemble) ? f.Ensemble : null)
            .Add("sort", sort);

        var response = await GetJsonAsync<XivmidiDto.Response>(Endpoints.XivmidiSearch(query.ToString()), ct).ConfigureAwait(false);
        var data = response?.data ?? [];
        var entries = data.Where(d => !string.IsNullOrWhiteSpace(d.download_url)).Select(Map).ToList();
        int? totalPages = response?.meta is { } meta ? (meta.total + PageSize - 1) / PageSize : null;

        return new SearchPage(entries, totalPages, data.Count);
    }

    private async Task<SearchPage> SearchBmpAsync(SearchFilters f, int page, CancellationToken ct)
    {
        var sort = f.Sort switch
        {
            SortOrder.Oldest => "createdAt",
            SortOrder.TitleAsc => "titleSort",
            SortOrder.TitleDesc => "-titleSort",
            SortOrder.MostDownloaded => "-downloads",
            _ => null,
        };

        // BMP has no separate artist/title parameters: both go in the free-text search.
        var query = new QueryBuilder()
            .Add("limit", PageSize)
            .Add("search", $"{f.Artist} {f.Title}".Trim())
            .Add("editor", f.Editor)
            .Add("ensemble", EnsembleSizes.IsValid(f.Ensemble) ? EnsembleSizes.Labels[f.Ensemble].ToLowerInvariant() : null)
            .Add("sort", sort)
            .Add("page", page);

        var response = await GetJsonAsync<BmpDto.Response>(Endpoints.BmpSearch(query.ToString()), ct).ConfigureAwait(false);
        var data = response?.docs ?? [];
        var entries = data.Where(d => !string.IsNullOrWhiteSpace(d.url)).Select(Map).ToList();

        return new SearchPage(entries, response?.totalPages, data.Count);
    }

    // Download
    /// <summary>Downloads a MIDI file. Only HTTPS URLs on the known library hosts are accepted.</summary>
    public async Task<DownloadedMidi> DownloadAsync(Uri url, CancellationToken ct)
    {
        if (!Endpoints.IsTrustedDownload(url))
            throw new MidiLibraryException($"Refused download from untrusted host: {url.Host}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("audio/midi");
        request.Headers.Accept.ParseAdd("audio/mid");
        request.Headers.Accept.ParseAdd("application/octet-stream;q=0.8");

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        var data = await ReadBytesAsync(response, ct).ConfigureAwait(false);
        return new DownloadedMidi(ResolveFileName(response, url), data);
    }

    // HTTP plumbing
    private async Task<T?> GetJsonAsync<T>(Uri url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(await ReadBytesAsync(response, ct).ConfigureAwait(false));
        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (JsonException e)
        {
            throw new MidiLibraryException("Invalid response from server", e);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException e)
        {
            throw new MidiLibraryException("Request timed out", e);
        }
        catch (HttpRequestException e)
        {
            throw new MidiLibraryException(e.InnerException?.Message ?? e.Message, e);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new MidiLibraryException($"{(int)status} {status}");
        }
        return response;
    }

    private async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new MidiLibraryException("Response too large");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        linked.CancelAfter(BodyTimeout);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, linked.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes)
                    throw new MidiLibraryException("Response too large");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException e)
        {
            throw new MidiLibraryException("Request timed out", e);
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            throw new MidiLibraryException(e.InnerException?.Message ?? e.Message, e);
        }
    }

    // Mapping
    private OnlineFileEntry Map(BmpDto.Doc doc)
    {
        var ensemble = Array.FindIndex(EnsembleSizes.Labels, l => string.Equals(l, doc.ensembleSize, StringComparison.OrdinalIgnoreCase));

        return new OnlineFileEntry
        {
            Id = doc.id.ToString(CultureInfo.InvariantCulture),
            Artist = doc.artist ?? "",
            Title = doc.title ?? "",
            Arranger = doc.arranger ?? "",

            EnsembleSize = ensemble > 0 ? ensemble : null,
            EnsembleText = ensemble > 0 ? EnsembleSizes.Labels[ensemble] : doc.ensembleSize ?? "",

            DurationSeconds = doc.songDurationMs > 0 ? (int)(doc.songDurationMs.Value / 1000) : null,
            DurationText = doc.duration ?? "",

            PublishedAt = doc.createdAt,
            PublishedText = FormatDate(doc.createdAt),

            DownloadsCount = doc.downloads ?? 0,
            LikesCount = doc.hearts ?? 0,

            Url = doc.url ?? "",
            PageUrl = Endpoints.BmpPage(doc.id),

            Source = EntrySource.Bmp,
        };
    }

    private static OnlineFileEntry Map(XivmidiDto.File file)
    {
        var bandsize = file.bandsize ?? 0;
        var duration = file.duration ?? 0;

        return new OnlineFileEntry
        {
            Id = file.id ?? "",
            Artist = file.artist ?? "",
            Title = file.title ?? "",
            Arranger = file.credit ?? "",

            EnsembleSize = EnsembleSizes.IsValid(bandsize) ? bandsize : null,
            EnsembleText = EnsembleSizes.IsValid(bandsize) ? EnsembleSizes.Labels[bandsize] : bandsize > 0 ? bandsize.ToString(CultureInfo.InvariantCulture) : "",

            DurationSeconds = duration > 0 ? duration : null,
            DurationText = duration > 0 ? $"{duration / 60}:{duration % 60:00}" : "",

            PublishedAt = file.published_at,
            PublishedText = FormatDate(file.published_at),

            DownloadsCount = file.download_count ?? 0,
            LikesCount = file.like_count ?? 0,

            Url = file.download_url ?? "",
            PageUrl = "",

            Source = EntrySource.Xivmidi,
        };
    }

    private static string FormatDate(DateTime? date) =>
        date is { } d && d != DateTime.MinValue ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";

    /// <summary>Picks the file name from Content-Disposition, falling back to the last URL segment.</summary>
    private static string ResolveFileName(HttpResponseMessage response, Uri url)
    {
        var cd = response.Content.Headers.ContentDisposition;
        var name = !string.IsNullOrEmpty(cd?.FileNameStar) ? cd.FileNameStar
            : !string.IsNullOrEmpty(cd?.FileName) ? cd.FileName.Trim('"')
            : Uri.UnescapeDataString(url.Segments.Last());
        return SanitizeFileName(name);
    }

    /// <summary>Makes a server-provided name safe to write to disk: no directory part, no invalid characters, a .mid/.midi extension.</summary>
    private static string SanitizeFileName(string name)
    {
        var str = Path.GetFileName(name);
        foreach (var c in Path.GetInvalidFileNameChars())
            str = str.Replace(c, '_');

        if (string.IsNullOrEmpty(str.Trim('.', ' ')))
            str = "song.mid";

        var ext = Path.GetExtension(str);
        if (!ext.Equals(".mid", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".midi", StringComparison.OrdinalIgnoreCase))
            str += ".mid";

        return str;
    }

    private sealed class QueryBuilder
    {
        private readonly StringBuilder _sb = new();

        public QueryBuilder Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return this;
            _sb.Append(_sb.Length == 0 ? '?' : '&').Append(key).Append('=').Append(Uri.EscapeDataString(value.Trim()));
            return this;
        }

        public QueryBuilder Add(string key, int? value) =>
            value is null ? this : Add(key, value.Value.ToString(CultureInfo.InvariantCulture));

        public override string ToString() => _sb.ToString();
    }
}

// Wire formats. Numeric fields are nullable: BMP sends some numbers as strings, which Json.NET converts, and an empty string becomes null.
#pragma warning disable IDE1006 // Names match the JSON fields.

internal static class XivmidiDto
{
    public sealed class Response
    {
        public Meta? meta { get; set; }
        public List<File>? data { get; set; }
    }

    public sealed class Meta
    {
        public int total { get; set; }
    }

    public sealed class File
    {
        public string? id { get; set; }
        public string? artist { get; set; }
        public string? title { get; set; }
        public string? credit { get; set; }
        public string? download_url { get; set; }
        public int? bandsize { get; set; }
        public int? duration { get; set; }
        public DateTime? published_at { get; set; }
        public int? download_count { get; set; }
        public int? like_count { get; set; }
    }
}

internal static class BmpDto
{
    public sealed class Response
    {
        public List<Doc>? docs { get; set; }
        public int? totalPages { get; set; }
    }

    public sealed class Doc
    {
        public int id { get; set; }
        public string? title { get; set; }
        public string? artist { get; set; }
        public string? arranger { get; set; }
        public string? ensembleSize { get; set; }
        public string? duration { get; set; }
        public int? downloads { get; set; }
        public int? hearts { get; set; }
        public long? songDurationMs { get; set; }
        public DateTime? createdAt { get; set; }
        public string? url { get; set; }
    }
}

#pragma warning restore IDE1006
