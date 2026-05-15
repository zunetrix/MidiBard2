using System.Net;
using System.Net.Http.Headers;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Playlist;
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
    public async Task ImportDirectUrlAsync_UsesConfiguredMapProviderDuringNormalization()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        settings.InstrumentMaps.Single(map => map.TrackName == "Piano").MidiPrograms.Remove(0);
        settings.InstrumentMaps.Single(map => map.TrackName == "Harp").MidiPrograms.Add(0);
        var handler = new StubHttpMessageHandler();
        handler.Add("https://example.test/song.mid", _ => BinaryResponse(CreateMidiBytes(), "downloaded.mid"));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(
            _midiFileService,
            httpClient,
            midiMapProvider: new ConfigurationEditorMidiMapProvider(settings));

        var result = await importer.ImportDirectUrlAsync(
            "https://example.test/song.mid",
            new MidiForgeImportOptions(OverwriteTrackNames: true));

        result.MidiFile.GetTrackChunks()
            .Select(chunk => chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text ?? string.Empty)
            .ShouldBe(new[] { "Harp" });
    }

    [Fact]
    public async Task ImportAsync_AutoDetectsDirectUrlMuseScoreUrlAndLocalGuitarTab()
    {
        var scriptUrl = "https://musescore.com/static/public/build/musescore/foo/2026.1234.js";
        var handler = new StubHttpMessageHandler();
        handler.Add("https://example.test/direct.mid", _ => BinaryResponse(CreateMidiBytes()));
        handler.Add("https://musescore.com/user/score", _ => TextResponse(CreateMuseScoreHtml("2468", "Muse Song", scriptUrl), "text/html"));
        handler.Add(scriptUrl, _ => TextResponse("""const token=()=>md5("suffix").substr(0,4);""", "application/javascript"));
        handler.Add("https://musescore.com/api/jmuse?id=2468&type=midi&index=0", _ =>
            JsonResponse("""{"info":{"url":"https://cdn.example.test/muse.mid"}}"""));
        handler.Add("https://cdn.example.test/muse.mid", _ => BinaryResponse(CreateMidiBytes()));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var direct = await importer.ImportAsync(new MidiForgeSourceImportRequest(
            "https://example.test/direct.mid",
            new MidiForgeImportOptions()));
        var museScore = await importer.ImportAsync(new MidiForgeSourceImportRequest(
            "https://musescore.com/user/score",
            new MidiForgeImportOptions()));
        var guitarTab = await importer.ImportAsync(new MidiForgeSourceImportRequest(
            FindDataFile("tab-example-1.gp"),
            new MidiForgeImportOptions(RemoveSequencerSpecificEvents: true)));

        direct.SourceKind.ShouldBe(MidiForgeImportSourceKind.DirectUrl);
        museScore.SourceKind.ShouldBe(MidiForgeImportSourceKind.MuseScoreUrl);
        guitarTab.SourceKind.ShouldBe(MidiForgeImportSourceKind.LocalGuitarTab);
    }

    [Fact]
    public async Task ImportDirectUrlAsync_RejectsEmbeddedCredentials()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler());
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        await Should.ThrowAsync<NotSupportedException>(() => importer.ImportDirectUrlAsync(
            "https://user:pass@example.test/song.mid",
            new MidiForgeImportOptions()));
    }

    [Fact]
    public async Task ImportDirectUrlAsync_EnforcesMaxDownloadBytesFromContentLength()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add("https://example.test/large.mid", _ => BinaryResponse(new byte[5]));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient, maxDownloadBytes: 4);

        await Should.ThrowAsync<InvalidOperationException>(() => importer.ImportDirectUrlAsync(
            "https://example.test/large.mid",
            new MidiForgeImportOptions()));
    }

    [Fact]
    public async Task ImportDirectUrlAsync_EnforcesMaxDownloadBytesFromStream()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add("https://example.test/chunked.mid", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new NoLengthByteArrayContent(new byte[5]),
        });
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient, maxDownloadBytes: 4);

        await Should.ThrowAsync<InvalidOperationException>(() => importer.ImportDirectUrlAsync(
            "https://example.test/chunked.mid",
            new MidiForgeImportOptions()));
    }

    [Fact]
    public async Task ImportDirectUrlAsync_UnknownExtensionKeepsUsefulMidiDisplayName()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add("https://example.test/song.download", _ => BinaryResponse(CreateMidiBytes()));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var result = await importer.ImportDirectUrlAsync(
            "https://example.test/song.download",
            new MidiForgeImportOptions());

        result.DisplayName.ShouldBe("song.mid");
        result.MidiFile.GetTrackChunks().SelectMany(chunk => chunk.GetNotes()).Count().ShouldBe(1);
    }

    [Fact]
    public async Task ImportDirectUrlAsync_DefaultCleanupPreservesLyricsForLrcExport()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add("https://example.test/lyrics.mid", _ => BinaryResponse(CreateMidiBytes()));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(new StubMidiFileService(CreateMidiFileWithLyrics()), httpClient);

        var result = await importer.ImportDirectUrlAsync(
            "https://example.test/lyrics.mid",
            new MidiForgeImportOptions(
                RemoveNonLyricMetadata: true,
                RemoveSequencerSpecificEvents: true));

        result.NormalizationResult.RemovedNonLyricMetadataEvents.ShouldBe(1);
        result.NormalizationResult.RemovedLyricTextEvents.ShouldBe(0);
        result.MidiFile.GetTrackChunks()
            .SelectMany(chunk => chunk.Events)
            .ShouldContain(e => e is LyricEvent || e is TextEvent);
        MidiForgeLyricsExporter.Export(result.MidiFile, "lyrics").HasLyrics.ShouldBeTrue();
    }

    [Fact]
    public async Task ImportMuseScoreUrlAsync_UsesLibreScoreStyleAuthAndDownloadsMidi()
    {
        var scriptUrl = "https://musescore.com/static/public/build/musescore/foo/2026.1234.js";
        var html = CreateMuseScoreHtml("12345", "Cool Song", scriptUrl);
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
    public async Task ImportMuseScoreUrlAsync_UsesFallbackAuthorizationWhenPrimaryHasNoUrl()
    {
        var scriptUrl = "https://musescore.com/static/public/build/musescore/foo/2026.1234.js";
        var primaryAuth = MidiForgeSourceImporter.CreateMuseScoreAuthorizationCode("12345", "salt");
        var fallbackAuth = MidiForgeSourceImporter.CreateMuseScoreAuthorizationCode("12345", "9654,4e");
        var handler = new StubHttpMessageHandler();
        handler.Add("https://musescore.com/user/fallback", _ =>
            TextResponse(CreateMuseScoreHtml("12345", "Fallback Song", scriptUrl), "text/html"));
        handler.Add(scriptUrl, _ => TextResponse("""const token=(e,t,n)=>md5(e+t+n+"salt").substr(0,4);""", "application/javascript"));
        handler.Add("https://musescore.com/api/jmuse?id=12345&type=midi&index=0", request =>
        {
            var authorization = request.Headers.GetValues("Authorization").Single();
            if (authorization == primaryAuth)
                return JsonResponse("""{"info":{}}""");

            authorization.ShouldBe(fallbackAuth);
            return JsonResponse("""{"info":{"url":"https://cdn.example.test/fallback.mid"}}""");
        });
        handler.Add("https://cdn.example.test/fallback.mid", _ => BinaryResponse(CreateMidiBytes()));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var result = await importer.ImportMuseScoreUrlAsync(
            "https://musescore.com/user/fallback",
            new MidiForgeImportOptions());

        result.DisplayName.ShouldBe("Fallback Song.mid");
        result.Warnings.ShouldContain("Used fallback MuseScore authorization suffix.");
    }

    [Fact]
    public async Task ImportMuseScoreUrlAsync_UsesUrlScoreIdAndFallbackAuthorizationWhenScorePageIsForbidden()
    {
        var fallbackAuth = MidiForgeSourceImporter.CreateMuseScoreAuthorizationCode("98765", "9654,4e");
        var handler = new StubHttpMessageHandler();
        handler.Add("https://musescore.com/user/example/scores/98765", request =>
        {
            request.Headers.GetValues("Sec-Fetch-Dest").Single().ShouldBe("document");
            return StatusResponse(HttpStatusCode.Forbidden, "Forbidden");
        });
        handler.Add("https://musescore.com/api/jmuse?id=98765&type=midi&index=0", request =>
        {
            request.Headers.GetValues("Authorization").Single().ShouldBe(fallbackAuth);
            return JsonResponse("""{"info":{"url":"https://cdn.example.test/forbidden-fallback.mid"}}""");
        });
        handler.Add("https://cdn.example.test/forbidden-fallback.mid", _ => BinaryResponse(CreateMidiBytes()));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var result = await importer.ImportMuseScoreUrlAsync(
            "https://musescore.com/user/example/scores/98765",
            new MidiForgeImportOptions());

        result.DisplayName.ShouldBe("musescore-98765.mid");
        result.Warnings.ShouldContain(warning => warning.Contains("403 Forbidden", StringComparison.OrdinalIgnoreCase));
        result.Warnings.ShouldContain("Used fallback MuseScore authorization suffix.");
        result.MidiFile.GetTrackChunks().SelectMany(chunk => chunk.GetNotes()).Count().ShouldBe(1);
    }

    [Fact]
    public async Task ImportMuseScoreUrlAsync_ThrowsClearErrorWhenScorePageIsForbiddenAndUrlHasNoScoreId()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add("https://musescore.com/user/no-score", _ => StatusResponse(HttpStatusCode.Forbidden, "Forbidden"));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => importer.ImportMuseScoreUrlAsync(
            "https://musescore.com/user/no-score",
            new MidiForgeImportOptions()));

        exception.Message.ShouldContain("/scores/{id}");
    }

    [Fact]
    public async Task ImportMuseScoreUrlAsync_AddsBrowserLikeHeadersForMuseScoreRequests()
    {
        var scriptUrl = "https://musescore.com/static/public/build/musescore/foo/2026.1234.js";
        var pageUrl = "https://musescore.com/user/headers/scores/13579";
        var html = CreateMuseScoreHtml("13579", "Header Song", scriptUrl);

        var handler = new StubHttpMessageHandler();
        handler.Add(pageUrl, request =>
        {
            request.Headers.UserAgent.ToString().ShouldContain("Chrome/125");
            string.Join(",", request.Headers.GetValues("Accept")).ShouldContain("text/html");
            request.Headers.GetValues("Sec-Fetch-Dest").Single().ShouldBe("document");
            return TextResponse(html, "text/html");
        });
        handler.Add(scriptUrl, request =>
        {
            request.Headers.Referrer!.ToString().ShouldBe(pageUrl);
            string.Join(",", request.Headers.GetValues("Accept")).ShouldContain("javascript");
            request.Headers.GetValues("Sec-Fetch-Dest").Single().ShouldBe("script");
            return TextResponse("""const token=(e,t,n)=>md5(e+t+n+"salt").substr(0,4);""", "application/javascript");
        });
        handler.Add("https://musescore.com/api/jmuse?id=13579&type=midi&index=0", request =>
        {
            request.Headers.Referrer!.ToString().ShouldBe(pageUrl);
            request.Headers.GetValues("Origin").Single().ShouldBe("https://musescore.com");
            request.Headers.GetValues("Sec-Fetch-Dest").Single().ShouldBe("empty");
            return JsonResponse("""{"info":{"url":"https://cdn.example.test/header-song.mid"}}""");
        });
        handler.Add("https://cdn.example.test/header-song.mid", request =>
        {
            request.Headers.Referrer!.ToString().ShouldBe(pageUrl);
            return BinaryResponse(CreateMidiBytes());
        });
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var result = await importer.ImportMuseScoreUrlAsync(pageUrl, new MidiForgeImportOptions());

        result.DisplayName.ShouldBe("Header Song.mid");
    }

    [Fact]
    public async Task ImportMuseScoreUrlAsync_ThrowsWhenScoreIdIsMissing()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add("https://musescore.com/user/no-score", _ =>
            TextResponse("""<html><head><title>No Score</title></head></html>""", "text/html"));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        await Should.ThrowAsync<InvalidOperationException>(() => importer.ImportMuseScoreUrlAsync(
            "https://musescore.com/user/no-score",
            new MidiForgeImportOptions()));
    }

    [Fact]
    public async Task ImportMuseScoreUrlAsync_UsesFallbackAuthorizationWhenAuthSuffixIsMissing()
    {
        var scriptUrl = "https://musescore.com/static/public/build/musescore/foo/2026.1234.js";
        var fallbackAuth = MidiForgeSourceImporter.CreateMuseScoreAuthorizationCode("12345", "9654,4e");
        var handler = new StubHttpMessageHandler();
        handler.Add("https://musescore.com/user/no-auth", _ =>
            TextResponse(CreateMuseScoreHtml("12345", "No Auth", scriptUrl), "text/html"));
        handler.Add(scriptUrl, _ => TextResponse("const noTokenHere = true;", "application/javascript"));
        handler.Add("https://musescore.com/api/jmuse?id=12345&type=midi&index=0", request =>
        {
            request.Headers.GetValues("Authorization").Single().ShouldBe(fallbackAuth);
            return JsonResponse("""{"info":{"url":"https://cdn.example.test/no-auth.mid"}}""");
        });
        handler.Add("https://cdn.example.test/no-auth.mid", _ => BinaryResponse(CreateMidiBytes()));
        using var httpClient = new HttpClient(handler);
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var result = await importer.ImportMuseScoreUrlAsync(
            "https://musescore.com/user/no-auth",
            new MidiForgeImportOptions());

        result.DisplayName.ShouldBe("No Auth.mid");
        result.Warnings.ShouldContain("Could not find MuseScore auth suffix; trying fallback authorization.");
        result.Warnings.ShouldContain("Used fallback MuseScore authorization suffix.");
    }

    [Fact]
    public void ImportGuitarTabFile_ConvertsAlphaTabOutputToMidi()
    {
        var tabPath = FindDataFile("tab-example-1.gp");
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

    [Theory]
    [InlineData("alphatab-gp7-serenade.gp")]
    [InlineData("alphatab-gp8-slash.gp")]
    public void ImportGuitarTabFile_ConvertsOfficialAlphaTabGpFixturesToMidi(string fileName)
    {
        var tabPath = FindDataFile(fileName);
        using var httpClient = new HttpClient(new StubHttpMessageHandler());
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        var result = importer.ImportGuitarTabFile(
            tabPath,
            new MidiForgeImportOptions(RemoveSequencerSpecificEvents: true));

        result.SourceKind.ShouldBe(MidiForgeImportSourceKind.LocalGuitarTab);
        result.DisplayName.ShouldBe(Path.ChangeExtension(fileName, ".mid"));
        result.FilePath.ShouldBeNull();
        result.IsDirty.ShouldBeTrue();
        result.MidiFile.GetTrackChunks().SelectMany(chunk => chunk.GetNotes()).Any().ShouldBeTrue();
    }

    [Theory]
    [InlineData("alphatab-gp7-serenade.gp", ".gp7")]
    [InlineData("alphatab-gp8-slash.gp", ".gp8")]
    public void ImportGuitarTabFile_ConvertsGp7AndGp8ExtensionAliasesToMidi(string sourceFileName, string aliasExtension)
    {
        var sourcePath = FindDataFile(sourceFileName);
        var aliasPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{aliasExtension}");
        File.Copy(sourcePath, aliasPath);
        using var httpClient = new HttpClient(new StubHttpMessageHandler());
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        try
        {
            var result = importer.ImportGuitarTabFile(
                aliasPath,
                new MidiForgeImportOptions(RemoveSequencerSpecificEvents: true));

            result.SourceKind.ShouldBe(MidiForgeImportSourceKind.LocalGuitarTab);
            result.DisplayName.ShouldBe($"{Path.GetFileNameWithoutExtension(aliasPath)}.mid");
            result.MidiFile.GetTrackChunks().SelectMany(chunk => chunk.GetNotes()).Any().ShouldBeTrue();
        }
        finally
        {
            File.Delete(aliasPath);
        }
    }

    [Theory]
    [InlineData(".gp")]
    [InlineData(".gp7")]
    [InlineData(".gp8")]
    [InlineData(".gp3")]
    [InlineData(".gp4")]
    [InlineData(".gp5")]
    [InlineData(".gpx")]
    public void IsSupportedExtension_AllowsVerifiedGuitarTabExtensionsAndAliases(string extension)
        => MidiForgeGuitarTabImporter.IsSupportedExtension($"song{extension}").ShouldBeTrue();

    [Theory]
    [InlineData(".musicxml")]
    [InlineData(".xml")]
    [InlineData(".mxl")]
    [InlineData(".mscz")]
    public void IsSupportedExtension_RejectsUnverifiedImportExtensions(string extension)
        => MidiForgeGuitarTabImporter.IsSupportedExtension($"song{extension}").ShouldBeFalse();

    [Fact]
    public void ImportGuitarTabFile_RejectsUnsupportedExtensionsBeforeConversion()
    {
        var tabPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.musicxml");
        File.WriteAllBytes(tabPath, new byte[] { 1, 2, 3 });
        using var httpClient = new HttpClient(new StubHttpMessageHandler());
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        try
        {
            var exception = Should.Throw<NotSupportedException>(() => importer.ImportGuitarTabFile(
                tabPath,
                new MidiForgeImportOptions()));

            exception.Message.ShouldContain(".musicxml");
        }
        finally
        {
            File.Delete(tabPath);
        }
    }

    [Fact]
    public async Task ImportAsync_RejectsUnsupportedLocalImportSource()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.musicxml");
        await File.WriteAllTextAsync(sourcePath, "<score-partwise />");
        using var httpClient = new HttpClient(new StubHttpMessageHandler());
        var importer = new MidiForgeSourceImporter(_midiFileService, httpClient);

        try
        {
            await Should.ThrowAsync<NotSupportedException>(() => importer.ImportAsync(
                new MidiForgeSourceImportRequest(sourcePath, new MidiForgeImportOptions())));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public void ConvertToMidiBytes_InvalidGuitarTabThrows()
    {
        var tabPath = FindDataFile("tab-example-error.gp3");
        var data = File.ReadAllBytes(tabPath);

        Should.Throw<Exception>(() => MidiForgeGuitarTabImporter.ConvertToMidiBytes(data));
    }

    private static string FindDataFile(string fileName)
    {
        var outputCandidate = Path.Combine(AppContext.BaseDirectory, "data", fileName);
        if (File.Exists(outputCandidate))
            return outputCandidate;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "MidiBard.Tests",
                "Data",
                fileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find test data file {fileName}.");
    }

    private static string CreateMuseScoreHtml(string scoreId, string title, string scriptUrl)
        => $"""
           <html>
           <head>
             <meta property="al:ios:url" content="musescore://score/{scoreId}">
             <meta property="og:title" content="{WebUtility.HtmlEncode(title)}">
             <link href="{scriptUrl}" rel="preload">
           </head>
           </html>
           """;

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

    private static MidiFile CreateMidiFileWithLyrics()
    {
        var chunk = new TrackChunk();
        using (var manager = chunk.ManageTimedEvents())
        {
            manager.Objects.Add(new TimedEvent(new CopyrightNoticeEvent("copyright"), 0));
            manager.Objects.Add(new TimedEvent(new TextEvent("Hel"), 120));
            manager.Objects.Add(new TimedEvent(new LyricEvent("lo"), 240));
            manager.Objects.Add(new TimedEvent(
                new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 },
                0));
            manager.Objects.Add(new TimedEvent(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0 },
                360));
            manager.Objects.Add(new TimedEvent(
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0 },
                480));
        }

        return new MidiFile(chunk)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };
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

    private static HttpResponseMessage StatusResponse(HttpStatusCode statusCode, string reasonPhrase)
        => new(statusCode)
        {
            ReasonPhrase = reasonPhrase,
            Content = new StringContent(string.Empty),
        };

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

    private sealed class NoLengthByteArrayContent(byte[] data) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(data, 0, data.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
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

    private sealed class StubMidiFileService(MidiFile midiFile) : IMidiFileService
    {
        public MidiFile? LoadMidiFile(string filePath) => midiFile;

        public MidiFile? LoadMidiFile(Stream midiStream) => midiFile;

        public TimeSpan CalculateDuration(MidiFile midiFile) => TimeSpan.Zero;

        public Task<TimeSpan> CalculateDurationFromFileAsync(string filePath) => Task.FromResult(TimeSpan.Zero);

        public Task CalculateAllDurationsAsync(List<Song> songs) => Task.CompletedTask;

        public (bool isValid, string errorMessage) ValidateMidiFile(string filePath) => (true, string.Empty);

        public string ExtractSongNameFromMidi(string filePath) => string.Empty;
    }
}
