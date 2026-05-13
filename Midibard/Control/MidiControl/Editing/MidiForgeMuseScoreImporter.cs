using System;
using System.Collections.Generic;
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

namespace MidiBard.Control.MidiControl.Editing;

internal sealed record MidiForgeMuseScoreImport(
    Uri MidiUri,
    string DisplayName,
    IReadOnlyList<string> Warnings);

internal sealed class MidiForgeMuseScoreImporter
{
    private const string FallbackAuthSuffix = "9654,4e";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.2535.85";

    private static readonly Regex[] HtmlScoreIdRegexes =
    {
        new(@"<meta[^>]+property=[""']al:ios:url[""'][^>]+content=[""']musescore://score/(\d+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<meta[^>]+(?:property|name)=[""']twitter:app:url:[^""']+[""'][^>]+content=[""']musescore://score/(\d+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"musescore://score/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private static readonly Regex[] UrlScoreIdRegexes =
    {
        new(@"(?:^|/)(?:scores|score)/(\d+)(?:[/?#]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
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

    private readonly HttpClient _httpClient;

    public MidiForgeMuseScoreImporter(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<MidiForgeMuseScoreImport> ResolveMidiDownloadAsync(
        Uri scoreUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scoreUri);
        if (!IsMuseScoreUrl(scoreUri))
            throw new ArgumentException("URL is not a MuseScore score URL.", nameof(scoreUri));

        var warnings = new List<string>
        {
            "MuseScore URL import is best-effort and depends on MuseScore page/API internals.",
        };

        var htmlResult = await TryGetStringAsync(scoreUri, MuseScoreRequestKind.ScorePage, scoreUri, cancellationToken);
        if (!htmlResult.IsSuccess)
            warnings.Add($"MuseScore score page request returned {htmlResult.StatusDescription}; trying URL score ID and fallback authorization.");

        var html = htmlResult.Content;
        var scoreId = string.IsNullOrWhiteSpace(html) ? null : TryExtractScoreId(html);
        scoreId ??= TryExtractScoreIdFromUrl(scoreUri);

        if (string.IsNullOrWhiteSpace(scoreId))
            throw new InvalidOperationException("MuseScore score page was blocked or did not expose a score ID, and the URL does not contain /scores/{id}.");

        var title = string.IsNullOrWhiteSpace(html) ? string.Empty : ExtractTitle(html);
        var authAttempts = new List<MuseScoreAuthAttempt>();

        if (!string.IsNullOrWhiteSpace(html))
        {
            var suffix = await TryGetMuseScoreAuthSuffixAsync(scoreUri, html, warnings, cancellationToken);
            if (!string.IsNullOrWhiteSpace(suffix))
                authAttempts.Add(new MuseScoreAuthAttempt(CreateAuthorizationCode(scoreId, suffix), IsFallback: false));
            else
                warnings.Add("Could not find MuseScore auth suffix; trying fallback authorization.");
        }
        else
        {
            warnings.Add("MuseScore score page was unavailable; trying fallback authorization.");
        }

        var fallbackAuth = CreateAuthorizationCode(scoreId, FallbackAuthSuffix);
        if (!authAttempts.Any(attempt => attempt.AuthorizationCode == fallbackAuth))
            authAttempts.Add(new MuseScoreAuthAttempt(fallbackAuth, IsFallback: true));

        foreach (var attempt in authAttempts)
        {
            var midiUrl = await TryGetMuseScoreFileUrlAsync(scoreUri, scoreId, attempt.AuthorizationCode, cancellationToken);
            if (string.IsNullOrWhiteSpace(midiUrl))
                continue;

            if (attempt.IsFallback)
                warnings.Add("Used fallback MuseScore authorization suffix.");

            return new MidiForgeMuseScoreImport(
                ValidateHttpUrl(midiUrl),
                SanitizeFileName(string.IsNullOrWhiteSpace(title) ? $"musescore-{scoreId}.mid" : $"{title}.mid"),
                warnings);
        }

        throw new InvalidOperationException("Could not retrieve MuseScore MIDI download URL. MuseScore rejected the available authorization tokens, or this score is not available for MIDI download.");
    }

    public static bool IsMuseScoreUrl(Uri uri)
        => uri != null
           && (uri.Host.Equals("musescore.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".musescore.com", StringComparison.OrdinalIgnoreCase));

    public static string CreateAuthorizationCode(string scoreId, string suffix)
    {
        var input = $"{scoreId}midi0{suffix}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant()[..4];
    }

    internal static string? TryExtractScoreIdFromUrl(Uri uri)
    {
        foreach (var regex in UrlScoreIdRegexes)
        {
            var match = regex.Match(uri.PathAndQuery);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null;
    }

    private async Task<string?> TryGetMuseScoreAuthSuffixAsync(
        Uri scoreUri,
        string html,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var scriptUrls = MuseScoreScriptUrlRegex.Matches(html)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var scriptUrl in scriptUrls)
        {
            var result = await TryGetStringAsync(new Uri(scriptUrl), MuseScoreRequestKind.Script, scoreUri, cancellationToken);
            if (!result.IsSuccess)
            {
                warnings.Add($"MuseScore auth script request returned {result.StatusDescription}; trying another script if available.");
                continue;
            }

            var suffix = ExtractAuthSuffix(result.Content ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(suffix))
                return suffix;
        }

        return null;
    }

    private async Task<string?> TryGetMuseScoreFileUrlAsync(
        Uri scoreUri,
        string scoreId,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        var apiUri = new Uri($"https://musescore.com/api/jmuse?id={scoreId}&type=midi&index=0");
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUri);
        AddMuseScoreHeaders(request, MuseScoreRequestKind.Api, scoreUri);
        request.Headers.TryAddWithoutValidation("Authorization", authorizationCode);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("info", out var info)
                || !info.TryGetProperty("url", out var urlElement))
                return null;

            return urlElement.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<MuseScoreStringResult> TryGetStringAsync(
        Uri uri,
        MuseScoreRequestKind requestKind,
        Uri scoreUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddMuseScoreHeaders(request, requestKind, scoreUri);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var statusDescription = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
        if (!response.IsSuccessStatusCode)
            return new MuseScoreStringResult(false, statusDescription, null);

        return new MuseScoreStringResult(
            true,
            statusDescription,
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static string? TryExtractScoreId(string html)
    {
        foreach (var regex in HtmlScoreIdRegexes)
        {
            var match = regex.Match(html);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null;
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

    private static Uri ValidateHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("MuseScore returned an invalid MIDI download URL.");
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("MuseScore returned a non-HTTP MIDI download URL.");

        return uri;
    }

    private static void AddMuseScoreHeaders(HttpRequestMessage request, MuseScoreRequestKind requestKind, Uri scoreUri)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US;q=0.8");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.Pragma.TryParseAdd("no-cache");

        switch (requestKind)
        {
            case MuseScoreRequestKind.ScorePage:
                request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
                break;
            case MuseScoreRequestKind.Script:
                request.Headers.TryAddWithoutValidation("Accept", "application/javascript,text/javascript,*/*");
                request.Headers.Referrer = scoreUri;
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "script");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                break;
            case MuseScoreRequestKind.Api:
                request.Headers.TryAddWithoutValidation("Accept", "application/json,*/*");
                request.Headers.Referrer = scoreUri;
                request.Headers.TryAddWithoutValidation("Origin", "https://musescore.com");
                request.Headers.TryAddWithoutValidation("Alt-Used", "musescore.com");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                break;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = System.IO.Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
            return "musescore.mid";

        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, ' ');

        name = Regex.Replace(name, @"\s\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(name) ? "musescore.mid" : name;
    }

    private sealed record MuseScoreAuthAttempt(string AuthorizationCode, bool IsFallback);

    private sealed record MuseScoreStringResult(bool IsSuccess, string StatusDescription, string? Content);

    private enum MuseScoreRequestKind
    {
        ScorePage,
        Script,
        Api,
    }
}
