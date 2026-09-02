using System.Text.Json;

using MidiBard.RemoteControl;

using Shouldly;

namespace MidiBard.Tests.RemoteControl;

public class OpenApiSpecGeneratorTests
{
    [Fact]
    public void GeneratedDocumentUsesRegisteredRoutesAndBearerAuthentication()
    {
        using var document = JsonDocument.Parse(
            OpenApiSpecGenerator.Generate(RemoteControlApiContract.Endpoints));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        foreach (var endpoint in RemoteControlApiContract.Endpoints)
        {
            var operation = paths
                .GetProperty(endpoint.Path)
                .GetProperty(endpoint.Method.ToLowerInvariant());
            operation.GetProperty("operationId").GetString().ShouldBe(endpoint.OperationId);
        }

        paths.EnumerateObject().Count().ShouldBe(
            RemoteControlApiContract.Endpoints.Select(endpoint => endpoint.Path).Distinct().Count());

        var bearer = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("bearerAuth");
        bearer.GetProperty("type").GetString().ShouldBe("http");
        bearer.GetProperty("scheme").GetString().ShouldBe("bearer");
    }

    [Fact]
    public void GeneratedSchemasUseTheSameCamelCaseNamesAsWireSerialization()
    {
        using var document = JsonDocument.Parse(
            OpenApiSpecGenerator.Generate(RemoteControlApiContract.Endpoints));

        AssertSchemaMatchesProperties<StatusResponse>(document);
        AssertSchemaMatchesProperties<PlaybackStatusResponse>(document);
        AssertSchemaMatchesProperties<NowPlayingResponse>(document);
        AssertSchemaMatchesProperties<EnsembleStatusResponse>(document);
        AssertSchemaMatchesProperties<LoadPlaybackRequest>(document);
        AssertSchemaMatchesProperties<LoadPlaybackResponse>(document);
        AssertSchemaMatchesProperties<PlaylistResponse>(document);
        AssertSchemaMatchesProperties<PlaylistSongResponse>(document);
        AssertSchemaMatchesProperties<PlaybackHandleRequest>(document);
        AssertSchemaMatchesProperties<EventPollResponse>(document);
        AssertSchemaMatchesProperties<PlaybackEventResponse>(document);
        AssertSchemaMatchesProperties<ErrorResponse>(document);
    }

    [Fact]
    public void GeneratedSchemasRequireFieldsUnlessTheWireContractMarksThemNullable()
    {
        using var document = JsonDocument.Parse(
            OpenApiSpecGenerator.Generate(RemoteControlApiContract.Endpoints));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        schemas.GetProperty("StatusResponse")
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ShouldBe(new[] { "ensemble", "latestEventSequence", "playback" });

        schemas.GetProperty("PlaybackStatusResponse")
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ShouldBe(new[] { "playMode", "state" });

        schemas.GetProperty("LoadPlaybackRequest")
            .TryGetProperty("required", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void GeneratedDocumentIsDeterministic()
    {
        var first = OpenApiSpecGenerator.Generate(RemoteControlApiContract.Endpoints);
        var second = OpenApiSpecGenerator.Generate(RemoteControlApiContract.Endpoints);

        first.ShouldBe(second);
    }

    private static void AssertSchemaMatchesProperties<T>(JsonDocument document)
    {
        var actual = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(typeof(T).Name)
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = typeof(T)
            .GetProperties()
            .Select(property => RemoteControlJson.Options.PropertyNamingPolicy?.ConvertName(property.Name)
                ?? property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        actual.ShouldBe(expected);
    }
}
