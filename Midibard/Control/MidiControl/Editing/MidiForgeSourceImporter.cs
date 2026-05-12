using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0";

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

    private static readonly Regex[] ScoreIdRegexes =
    {
        new(@"<meta[^>]+property=[""']al:ios:url[""'][^>]+content=[""']musescore://score/(\d+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<meta[^>]+(?:property|name)=[""']twitter:app:url:[^""']+[""'][^>]+content=[""']musescore://score/(\d+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"musescore://score/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private static readonly Regex[] TitleRegexes =
    {
        new(@"<meta[^>]+property=[""']og:title[""'][^>]+content=[""']([^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<meta[^>]+name=[""']twitter:title[""'][^>]+content=[""']([^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
    };

    private static readonly Regex MuseScoreScriptUrlRegex = new(
        @"https://musescore\.com/static/public/build/musescore[^""'\s<]+(?:_es6)?/20[^""'\s<]+\.js",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AuthSuffixRegex = new(
        @"""([^""]+)""\)\.substr\(0,4\)",
        RegexOptions.Compiled);

    private readonly IMidiFileService _midiFileService;
    private readonly HttpClient _httpClient;
    private readonly long _maxDownloadBytes;

    public MidiForgeSourceImporter(
        IMidiFileService midiFileService,
        HttpClient httpClient,
        long maxDownloadBytes = DefaultMaxDownloadBytes)
    {
        _midiFileService = midiFileService ?? throw new ArgumentNullException(nameof(midiFileService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _maxDownloadBytes = maxDownloadBytes;
    }

    public static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = false,
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

        var warnings = new List<string>
        {
            "MuseScore URL import is best-effort and depends on MuseScore page/API internals.",
        };

        var html = await GetStringAsync(uri, "text/html,application/xhtml+xml,*/*", cancellationToken);
        var scoreId = ExtractScoreId(html);
        var title = ExtractTitle(html);
        var suffix = await GetMuseScoreAuthSuffixAsync(uri, html, cancellationToken);
        var auth = CreateMuseScoreAuthorizationCode(scoreId, suffix);
        var midiUrl = await GetMuseScoreFileUrlAsync(scoreId, auth, cancellationToken);

        if (string.IsNullOrWhiteSpace(midiUrl))
        {
            var fallbackAuth = CreateMuseScoreAuthorizationCode(scoreId, "9654,4e");
            midiUrl = await GetMuseScoreFileUrlAsync(scoreId, fallbackAuth, cancellationToken);
            warnings.Add("Used fallback MuseScore authorization suffix.");
        }

        if (string.IsNullOrWhiteSpace(midiUrl))
            throw new InvalidOperationException("Could not retrieve MuseScore MIDI download URL.");

        var downloadUri = ValidateHttpUrl(midiUrl);
        var download = await DownloadFileAsync(downloadUri, cancellationToken);
        var displayName = SanitizeFileName(string.IsNullOrWhiteSpace(title) ? $"musescore-{scoreId}.mid" : $"{title}.mid");

        return ImportBytesAsMidi(
            download.Data,
            displayName,
            importOptions,
            MidiForgeImportSourceKind.MuseScoreUrl,
            isGuitarTab: false,
            warnings);
    }

    public static bool IsMuseScoreUrl(Uri uri)
        => uri != null
           && (uri.Host.Equals("musescore.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".musescore.com", StringComparison.OrdinalIgnoreCase));

    public static string CreateMuseScoreAuthorizationCode(string scoreId, string suffix)
    {
        var input = $"{scoreId}midi0{suffix}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant()[..4];
    }

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

        var normalized = MidiForgeImporter.Normalize(midi, importOptions);
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

    private async Task<string> GetStringAsync(Uri uri, string accept, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddCommonHeaders(request, accept);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Request failed with status {(int)response.StatusCode} {response.ReasonPhrase}.");

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<(byte[] Data, string? FileName)> DownloadFileAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddCommonHeaders(request, "audio/midi,application/octet-stream,*/*");

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

    private async Task<string?> GetMuseScoreFileUrlAsync(
        string scoreId,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        var apiUri = new Uri($"https://musescore.com/api/jmuse?id={scoreId}&type=midi&index=0");
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUri);
        AddCommonHeaders(request, "application/json,*/*");
        request.Headers.TryAddWithoutValidation("Authorization", authorizationCode);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("info", out var info)
            || !info.TryGetProperty("url", out var urlElement))
            return null;

        return urlElement.GetString();
    }

    private async Task<string> GetMuseScoreAuthSuffixAsync(
        Uri scoreUri,
        string html,
        CancellationToken cancellationToken)
    {
        var scriptUrls = MuseScoreScriptUrlRegex.Matches(html)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var scriptUrl in scriptUrls)
        {
            var js = await GetStringAsync(new Uri(scriptUrl), "application/javascript,text/javascript,*/*", cancellationToken);
            var suffix = ExtractAuthSuffix(js);
            if (!string.IsNullOrWhiteSpace(suffix))
                return suffix;
        }

        throw new InvalidOperationException($"Could not find MuseScore auth suffix for {scoreUri.Host}.");
    }

    private static string ExtractScoreId(string html)
    {
        foreach (var regex in ScoreIdRegexes)
        {
            var match = regex.Match(html);
            if (match.Success)
                return match.Groups[1].Value;
        }

        throw new InvalidOperationException("Could not detect MuseScore score ID.");
    }

    private static string ExtractTitle(string html)
    {
        foreach (var regex in TitleRegexes)
        {
            var match = regex.Match(html);
            if (!match.Success)
                continue;

            var title = WebUtility.HtmlDecode(match.Groups[1].Value)
                .Replace(" | MuseScore", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (!string.IsNullOrWhiteSpace(title))
                return title;
        }

        return string.Empty;
    }

    private static string? ExtractAuthSuffix(string javascript)
    {
        var match = AuthSuffixRegex.Match(javascript);
        if (match.Success)
            return match.Groups[1].Value;

        var markerIndex = javascript.IndexOf(".substr(0,4)", StringComparison.Ordinal);
        if (markerIndex < 0)
            return null;

        var closingDoubleQuote = javascript.LastIndexOf('"', markerIndex);
        var openingDoubleQuote = closingDoubleQuote > 0 ? javascript.LastIndexOf('"', closingDoubleQuote - 1) : -1;
        if (openingDoubleQuote >= 0 && closingDoubleQuote > openingDoubleQuote)
            return javascript[(openingDoubleQuote + 1)..closingDoubleQuote];

        var closingSingleQuote = javascript.LastIndexOf('\'', markerIndex);
        var openingSingleQuote = closingSingleQuote > 0 ? javascript.LastIndexOf('\'', closingSingleQuote - 1) : -1;
        return openingSingleQuote >= 0 && closingSingleQuote > openingSingleQuote
            ? javascript[(openingSingleQuote + 1)..closingSingleQuote]
            : null;
    }

    private static void AddCommonHeaders(HttpRequestMessage request, string accept)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.8");
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
