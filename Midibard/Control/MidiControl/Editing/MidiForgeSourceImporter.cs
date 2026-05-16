using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Core;

using MidiBard.Playlist.Services;

namespace MidiBard.Control.MidiControl.Editing;

public enum MidiForgeImportSourceKind
{
    Auto,
    DirectUrl,
    MuseScoreUrl,
    LocalGuitarTab,
}

public sealed record MidiForgeSourceImportRequest(
    string Source,
    MidiForgeImportOptions ImportOptions,
    MidiForgeImportSourceKind SourceKind = MidiForgeImportSourceKind.Auto);

public sealed record MidiForgeSourceImportResult(
    MidiFile MidiFile,
    string DisplayName,
    string? FilePath,
    bool IsDirty,
    MidiForgeImportResult NormalizationResult,
    MidiForgeImportSourceKind SourceKind,
    IReadOnlyList<string> Warnings);

public sealed class MidiForgeSourceImporter
{
    private const long DefaultMaxDownloadBytes = 50L * 1024L * 1024L;
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.2535.85";

    private static readonly HashSet<string> SupportedMidiExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mid",
        ".midi",
        ".smf",
        ".rmid",
        ".rmidi",
        ".riff",
        ".rmi",
        ".kar",
        ".mmsong",
    };

    private readonly IMidiFileService _midiFileService;
    private readonly HttpClient _httpClient;
    private readonly long _maxDownloadBytes;
    private readonly IEditorMidiMapProvider _midiMapProvider;

    public MidiForgeSourceImporter(
        IMidiFileService midiFileService,
        HttpClient httpClient,
        long maxDownloadBytes = DefaultMaxDownloadBytes,
        IEditorMidiMapProvider midiMapProvider = null)
    {
        _midiFileService = midiFileService ?? throw new ArgumentNullException(nameof(midiFileService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _maxDownloadBytes = maxDownloadBytes;
        _midiMapProvider = midiMapProvider ?? DefaultEditorMidiMapProvider.Instance;
    }

    public static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
    }

    public async Task<MidiForgeSourceImportResult> ImportAsync(
        MidiForgeSourceImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Source?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Import source is empty.", nameof(request));

        var kind = request.SourceKind == MidiForgeImportSourceKind.Auto
            ? DetectSourceKind(source)
            : request.SourceKind;

        return kind switch
        {
            MidiForgeImportSourceKind.LocalGuitarTab => ImportGuitarTabFile(source, request.ImportOptions),
            MidiForgeImportSourceKind.MuseScoreUrl => await ImportMuseScoreUrlAsync(source, request.ImportOptions, cancellationToken),
            MidiForgeImportSourceKind.DirectUrl => await ImportDirectUrlAsync(source, request.ImportOptions, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported import source kind: {kind}"),
        };
    }

    public MidiForgeSourceImportResult ImportGuitarTabFile(string path, MidiForgeImportOptions importOptions)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Guitar tab path is empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Guitar tab file was not found.", path);
        if (!MidiForgeGuitarTabImporter.IsSupportedExtension(path))
            throw new NotSupportedException($"Unsupported guitar tab extension: {Path.GetExtension(path)}");

        var bytes = File.ReadAllBytes(path);
        var displayName = MidiForgeGuitarTabImporter.GetConvertedMidiFileName(path);
        return ImportBytesAsMidi(bytes, displayName, importOptions, MidiForgeImportSourceKind.LocalGuitarTab, isGuitarTab: true);
    }

    public async Task<MidiForgeSourceImportResult> ImportDirectUrlAsync(
        string url,
        MidiForgeImportOptions importOptions,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateHttpUrl(url);
        var download = await DownloadFileAsync(uri, cancellationToken);
        var fileName = GetDownloadedFileName(uri, download.FileName);
        var isGuitarTab = MidiForgeGuitarTabImporter.IsSupportedExtension(fileName);

        if (!isGuitarTab && !SupportedMidiExtensions.Contains(Path.GetExtension(fileName)))
        {
            // Let DryWetMidi attempt nonstandard MIDI downloads, but keep a useful save-as name.
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            fileName = string.IsNullOrWhiteSpace(baseName) ? "download.mid" : $"{baseName}.mid";
        }

        return ImportBytesAsMidi(download.Data, fileName, importOptions, MidiForgeImportSourceKind.DirectUrl, isGuitarTab);
    }

    public async Task<MidiForgeSourceImportResult> ImportMuseScoreUrlAsync(
        string url,
        MidiForgeImportOptions importOptions,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateHttpUrl(url);
        if (!IsMuseScoreUrl(uri))
            throw new ArgumentException("URL is not a MuseScore score URL.", nameof(url));

        var warnings = new List<string>();
        MidiForgeMuseScoreImport museScoreImport;
        try
        {
            museScoreImport = await new MidiForgeMuseScoreImporter(_httpClient)
                .ResolveMidiDownloadAsync(uri, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"MuseScore metadata request failed: {e.Message}", e);
        }

        warnings.AddRange(museScoreImport.Warnings);

        (byte[] Data, string? FileName) download;
        try
        {
            download = await DownloadFileAsync(museScoreImport.MidiUri, cancellationToken, uri);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"MuseScore MIDI file download failed: {e.Message}", e);
        }

        return ImportBytesAsMidi(
            download.Data,
            museScoreImport.DisplayName,
            importOptions,
            MidiForgeImportSourceKind.MuseScoreUrl,
            isGuitarTab: false,
            warnings);
    }

    public static bool IsMuseScoreUrl(Uri uri)
        => MidiForgeMuseScoreImporter.IsMuseScoreUrl(uri);

    public static string CreateMuseScoreAuthorizationCode(string scoreId, string suffix)
        => MidiForgeMuseScoreImporter.CreateAuthorizationCode(scoreId, suffix);

    private MidiForgeSourceImportResult ImportBytesAsMidi(
        byte[] sourceBytes,
        string displayName,
        MidiForgeImportOptions importOptions,
        MidiForgeImportSourceKind sourceKind,
        bool isGuitarTab,
        List<string>? warnings = null)
    {
        warnings ??= new List<string>();

        var midiBytes = sourceBytes;
        if (isGuitarTab)
        {
            midiBytes = MidiForgeGuitarTabImporter.ConvertToMidiBytes(sourceBytes);
            displayName = MidiForgeGuitarTabImporter.GetConvertedMidiFileName(displayName);
        }

        using var stream = new MemoryStream(midiBytes, writable: false);
        var midi = _midiFileService.LoadMidiFile(stream)
                   ?? throw new InvalidDataException("Imported data could not be parsed as MIDI.");

        if (isGuitarTab)
        {
            var removed = MidiForgeGuitarTabImporter.RemoveAlphaTabHelperEvents(midi);
            if (removed > 0)
                warnings.Add($"Removed {removed} AlphaTab helper event(s).");
        }

        var normalized = MidiForgeImporter.Normalize(midi, importOptions, _midiMapProvider);
        return new MidiForgeSourceImportResult(
            normalized.MidiFile,
            SanitizeFileName(displayName),
            FilePath: null,
            IsDirty: true,
            normalized,
            sourceKind,
            warnings);
    }

    private static MidiForgeImportSourceKind DetectSourceKind(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return IsMuseScoreUrl(uri)
                ? MidiForgeImportSourceKind.MuseScoreUrl
                : MidiForgeImportSourceKind.DirectUrl;
        }

        if (MidiForgeGuitarTabImporter.IsSupportedExtension(source))
            return MidiForgeImportSourceKind.LocalGuitarTab;

        throw new NotSupportedException("Source is not a supported URL or guitar tab file.");
    }

    private Uri ValidateHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("URL is invalid.", nameof(url));
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new NotSupportedException("Only HTTP and HTTPS URLs are supported.");
        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            throw new NotSupportedException("URLs with embedded credentials are not supported.");
        return uri;
    }

    private async Task<(byte[] Data, string? FileName)> DownloadFileAsync(
        Uri uri,
        CancellationToken cancellationToken,
        Uri? referrer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddCommonHeaders(request, "audio/midi,application/octet-stream,*/*", referrer);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Download failed with status {(int)response.StatusCode} {response.ReasonPhrase}.");

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > _maxDownloadBytes)
            throw new InvalidOperationException($"Download is larger than {_maxDownloadBytes / 1024 / 1024} MB.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > _maxDownloadBytes)
                throw new InvalidOperationException($"Download is larger than {_maxDownloadBytes / 1024 / 1024} MB.");

            memory.Write(buffer, 0, read);
        }

        return (memory.ToArray(), GetContentDispositionFileName(response.Content.Headers.ContentDisposition));
    }

    private static void AddCommonHeaders(HttpRequestMessage request, string accept, Uri? referrer = null)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.8");
        if (referrer != null)
            request.Headers.Referrer = referrer;
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.Pragma.TryParseAdd("no-cache");
    }

    private static string GetDownloadedFileName(Uri uri, string? contentDispositionFileName)
    {
        if (!string.IsNullOrWhiteSpace(contentDispositionFileName))
            return SanitizeFileName(contentDispositionFileName);

        var fileName = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(fileName) ? "download.mid" : SanitizeFileName(fileName);
    }

    private static string? GetContentDispositionFileName(ContentDispositionHeaderValue? contentDisposition)
    {
        var fileName = contentDisposition?.FileNameStar;
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = contentDisposition?.FileName;

        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : fileName.Trim().Trim('"');
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
            return "import.mid";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, ' ');

        name = Regex.Replace(name, @"\s\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(name) ? "import.mid" : name;
    }
}
