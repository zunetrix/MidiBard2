using System.Net;
using System.Net.Http.Headers;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Playlist.Services;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Control.MidiControl.Editing;

public class MidiForgeSourceImporterTests
{
    private readonly MidiFileService _midiFileService;

    public MidiForgeSourceImporterTests()
    {
        DalamudTestSetup.Initialize();
        _midiFileService = new MidiFileService();
    }

    [Fact]
    public async Task ImportDirectUrlAsync_LoadsDownloadedMidiThroughNormalization()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add("https://example.test/song.mid", _ => BinaryResponse(CreateMidiBytes(), "downloaded.mid"));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var result = await importer.ImportDirectUrlAsync(
            "https://example.test/song.mid",
            new MidiForgeImportOptions(OverwriteTrackNames: true));

        result.SourceKind.ShouldBe(MidiForgeImportSourceKind.DirectUrl);
        result.DisplayName.ShouldBe("downloaded.mid");
        result.FilePath.ShouldBeNull();
        result.IsDirty.ShouldBeTrue();
        result.NormalizationResult.RenamedTracks.ShouldBe(1);
        result.MidiFile.GetTrackChunks().SelectMany(chunk => chunk.GetNotes()).Count().ShouldBe(1);
    }

    [Fact]
    public async Task ImportMuseScoreUrlAsync_UsesLibreScoreStyleAuthAndDownloadsMidi()
    {
        var scriptUrl = "https://musescore.com/static/public/build/musescore/foo/2026.1234.js";
        var html = $"""
                   <html>
                   <head>
                     <meta property="al:ios:url" content="musescore://score/12345">
                     <meta property="og:title" content="Cool Song">
                     <link href="{scriptUrl}" rel="preload">
                   </head>
                   </html>
                   """;
        var expectedAuth = MidiForgeSourceImporter.CreateMuseScoreAuthorizationCode("12345", "salt");

        var handler = new StubHttpMessageHandler();
        handler.Add("https://musescore.com/user/cool-song", _ => TextResponse(html, "text/html"));
        handler.Add(scriptUrl, _ => TextResponse("""const token=(e,t,n)=>md5(e+t+n+"salt").substr(0,4);""", "application/javascript"));
        handler.Add("https://musescore.com/api/jmuse?id=12345&type=midi&index=0", request =>
        {
            request.Headers.TryGetValues("Authorization", out var values).ShouldBeTrue();
            values!.Single().ShouldBe(expectedAuth);
            return JsonResponse("""{"info":{"url":"https://cdn.example.test/cool-song.mid"}}""");
        });
        handler.Add("https://cdn.example.test/cool-song.mid", _ => BinaryResponse(CreateMidiBytes()));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var result = await importer.ImportMuseScoreUrlAsync(
            "https://musescore.com/user/cool-song",
            new MidiForgeImportOptions());

        result.SourceKind.ShouldBe(MidiForgeImportSourceKind.MuseScoreUrl);
        result.DisplayName.ShouldBe("Cool Song.mid");
        result.FilePath.ShouldBeNull();
        result.Warnings.ShouldContain(warning => warning.Contains("best-effort", StringComparison.OrdinalIgnoreCase));
        result.MidiFile.GetTrackChunks().SelectMany(chunk => chunk.GetNotes()).Count().ShouldBe(1);
    }

    [Fact]
    public void ImportGuitarTabFile_ConvertsAlphaTabOutputToMidi()
    {
        var tabPath = FindBardForgeFixture("tab-example-1.gp");
        using var httpClient = new HttpClient(new StubHttpMessageHandler());
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var result = importer.ImportGuitarTabFile(
            tabPath,
            new MidiForgeImportOptions(RemoveSequencerSpecificEvents: true));

        result.SourceKind.ShouldBe(MidiForgeImportSourceKind.LocalGuitarTab);
        result.DisplayName.ShouldBe("tab-example-1.mid");
        result.FilePath.ShouldBeNull();
        result.IsDirty.ShouldBeTrue();
        result.MidiFile.GetTrackChunks().SelectMany(chunk => chunk.GetNotes()).Any().ShouldBeTrue();
    }

    [Fact]
    public void ConvertToMidiBytes_InvalidGuitarTabThrows()
    {
        var tabPath = FindBardForgeFixture("tab-example-error.gp3");
        var data = File.ReadAllBytes(tabPath);

        Should.Throw<Exception>(() => MidiForgeGuitarTabImporter.ConvertToMidiBytes(data));
    }

    private static string FindBardForgeFixture(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "docs",
                "bardforge",
                "bard-forge",
                "test",
                "data",
                fileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find BardForge fixture {fileName}.");
    }

    private static byte[] CreateMidiBytes()
    {
        var chunk = new TrackChunk();
        using (var manager = chunk.ManageTimedEvents())
        {
            manager.Objects.Add(new TimedEvent(
                new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 },
                0));
            manager.Objects.Add(new TimedEvent(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0 },
                0));
            manager.Objects.Add(new TimedEvent(
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0 },
                120));
        }

        var midi = new MidiFile(chunk)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

        using var stream = new MemoryStream();
        midi.Write(stream);
        return stream.ToArray();
    }

    private static HttpResponseMessage TextResponse(string text, string contentType)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(text)
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue(contentType),
                },
            },
        };

    private static HttpResponseMessage JsonResponse(string json)
        => TextResponse(json, "application/json");

    private static HttpResponseMessage BinaryResponse(byte[] data, string? fileName = null)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/midi");
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = fileName,
            };
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

        public void Add(string url, Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            => _routes[url] = responseFactory;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.ToString() ?? string.Empty;
            if (!_routes.TryGetValue(key, out var responseFactory))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                    Content = new StringContent($"No stub for {key}"),
                });
            }

            var response = responseFactory(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
