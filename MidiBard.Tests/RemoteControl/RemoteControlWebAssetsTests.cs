using System.Text;

using MidiBard.RemoteControl;

using Shouldly;

namespace MidiBard.Tests.RemoteControl;

public class RemoteControlWebAssetsTests
{
    [Theory]
    [InlineData("/", "text/html")]
    [InlineData("/docs/", "text/html")]
    [InlineData("/app.js", "text/javascript")]
    [InlineData("/styles.css", "text/css")]
    [InlineData("/plugin-icon.png", "image/png")]
    [InlineData("/vendor/preact/index.js", "text/javascript")]
    [InlineData("/vendor/preact/diff/index.js", "text/javascript")]
    [InlineData("/licenses/preact.txt", "text/plain")]
    public void EmbeddedControllerAssetsResolve(string path, string expectedMediaType)
    {
        RemoteControlWebAssets.TryGet(path, out var asset).ShouldBeTrue();
        asset.ContentType.ShouldStartWith(expectedMediaType);
        asset.Content.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ControllerShellUsesOnlyEmbeddedScriptsAndStyles()
    {
        RemoteControlWebAssets.TryGet("/", out var asset).ShouldBeTrue();
        var html = Encoding.UTF8.GetString(asset.Content);

        html.ShouldContain("/app.js");
        html.ShouldContain("/styles.css");
        html.ShouldContain("rel=\"icon\" type=\"image/png\" href=\"/plugin-icon.png\"");
        html.ShouldNotContain("https://");
        html.ShouldNotContain("http://");
    }

    [Fact]
    public void ControllerUsesPluginIconForBrandMark()
    {
        RemoteControlWebAssets.TryGet("/app.js", out var asset).ShouldBeTrue();
        var script = Encoding.UTF8.GetString(asset.Content);

        script.ShouldContain("src: \"/plugin-icon.png\"");
        script.ShouldNotContain("class: \"brand-mark\" }, \"M2\"");
    }

    [Fact]
    public void ControllerUsesSequenceModeAsTheOnlyPostSongControl()
    {
        RemoteControlWebAssets.TryGet("/app.js", out var asset).ShouldBeTrue();
        var script = Encoding.UTF8.GetString(asset.Content);

        script.ShouldContain("After song");
        script.ShouldNotContain("Continue ensemble automatically");
        script.ShouldNotContain("/ensemble/auto-advance");
    }

    [Fact]
    public void ControllerAcceptsAndImmediatelyRemovesUrlTokens()
    {
        RemoteControlWebAssets.TryGet("/app.js", out var asset).ShouldBeTrue();
        var script = Encoding.UTF8.GetString(asset.Content);

        script.ShouldContain("hash.get(\"token\") || query.get(\"token\")");
        script.ShouldContain("window.history.replaceState");
        script.ShouldContain("const urlToken = consumeUrlToken()");
    }

    [Fact]
    public void ControllerDoesNotSurfaceNonJsonProxyBodies()
    {
        RemoteControlWebAssets.TryGet("/app.js", out var asset).ShouldBeTrue();
        var script = Encoding.UTF8.GetString(asset.Content);

        script.ShouldContain("Remote-control server unavailable (HTTP ");
        script.ShouldContain("Unable to reach the MidiBard remote-control server.");
        script.ShouldNotContain("await response.text()");
    }

    [Fact]
    public void ControllerShowsTransportReconnectState()
    {
        RemoteControlWebAssets.TryGet("/app.js", out var asset).ShouldBeTrue();
        var script = Encoding.UTF8.GetString(asset.Content);

        script.ShouldContain("\"reconnecting\"");
        script.ShouldContain("\"Reconnecting\"");
    }

    [Fact]
    public void ControllerFormatsPlaylistDurationsWithHoursWhenNeeded()
    {
        RemoteControlWebAssets.TryGet("/app.js", out var asset).ShouldBeTrue();
        var script = Encoding.UTF8.GetString(asset.Content);

        script.ShouldContain("const hours = Math.floor(totalSeconds / 3600)");
        script.ShouldContain("hours > 0");
        script.ShouldContain("String(minutes).padStart(2, \"0\")");
    }

    [Fact]
    public void PlaylistActionColumnStaysVisibleDuringHorizontalScroll()
    {
        RemoteControlWebAssets.TryGet("/styles.css", out var asset).ShouldBeTrue();
        var styles = Encoding.UTF8.GetString(asset.Content);

        styles.ShouldContain(".song-table th.action-column");
        styles.ShouldContain(".song-table td.action-column { position: sticky; right: 0;");
    }

    [Theory]
    [InlineData("/vendor/preact/../app.js")]
    [InlineData("/vendor/preact/")]
    [InlineData("/missing.js")]
    public void UnknownOrUnsafeAssetsDoNotResolve(string path)
    {
        RemoteControlWebAssets.TryGet(path, out _).ShouldBeFalse();
    }
}
