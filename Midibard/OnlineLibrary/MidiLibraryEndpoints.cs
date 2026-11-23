using System;

namespace MidiBard.OnlineLibrary;

/// <summary>
/// The only place that knows the online MIDI library hosts. Request URLs, web page links and the
/// download whitelist are all built from these values.
/// </summary>
public sealed record MidiLibraryEndpoints(Uri XivmidiApi, Uri BmpApi, Uri BmpSite)
{
    public static MidiLibraryEndpoints Default { get; } = new(
        new Uri("https://api.xivmidi.com/v2/"),
        new Uri("https://bardmusicplayer.com/api/"),
        new Uri("https://bardmusicplayer.com/"));

    public Uri XivmidiSearch(string query) => new(XivmidiApi, "files" + query);

    public Uri BmpSearch(string query) => new(BmpApi, "midi-search" + query);

    public string BmpPage(int id) => new Uri(BmpSite, $"midis#midi={id}").AbsoluteUri;

    /// <summary>Which library a download URL belongs to, or null when it is not one of the known HTTPS hosts.</summary>
    public EntrySource? SourceOf(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
            return null;
        if (url.Host.Equals(XivmidiApi.Host, StringComparison.OrdinalIgnoreCase))
            return EntrySource.Xivmidi;
        if (url.Host.Equals(BmpApi.Host, StringComparison.OrdinalIgnoreCase))
            return EntrySource.Bmp;
        return null;
    }

    public bool IsTrustedDownload(Uri url) => SourceOf(url) is not null;
}
