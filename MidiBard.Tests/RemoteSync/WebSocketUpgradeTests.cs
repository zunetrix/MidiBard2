using System;
using System.Collections.Generic;
using System.Text;

using MidiBard.RemoteSync;

namespace MidiBard.Tests.RemoteSync;

public class WebSocketUpgradeTests
{
    [Fact]
    public void ComputeAcceptKeyMatchesRfc6455Vector()
    {
        // Example handshake from RFC 6455 section 1.3.
        WebSocketUpgrade.ComputeAcceptKey("dGhlIHNhbXBsZSBub25jZQ==")
            .ShouldBe("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");
    }

    [Theory]
    [InlineData("websocket")]
    [InlineData("WebSocket")]
    [InlineData("WEBSOCKET")]
    [InlineData("h2c, websocket")]
    public void UpgradeHeaderIsCaseInsensitiveTokenList(string upgradeValue)
    {
        var headers = Headers(
            ("Upgrade", upgradeValue),
            ("Connection", "Upgrade"),
            ("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ=="));

        WebSocketUpgrade.IsUpgradeRequest("GET", headers).ShouldBeTrue();
    }

    [Fact]
    public void NonUpgradeRequestsAreRejected()
    {
        WebSocketUpgrade.IsUpgradeRequest("GET", Headers()).ShouldBeFalse();

        WebSocketUpgrade.IsUpgradeRequest(
            "POST",
            Headers(
                ("Upgrade", "websocket"),
                ("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ=="))).ShouldBeFalse();

        WebSocketUpgrade.IsUpgradeRequest(
            "GET",
            Headers(("Upgrade", "websocket"))).ShouldBeFalse();

        WebSocketUpgrade.IsUpgradeRequest(
            "GET",
            Headers(
                ("Upgrade", "h2c"),
                ("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ=="))).ShouldBeFalse();
    }

    [Fact]
    public void HandshakeResponseContainsAcceptKeyAndTerminator()
    {
        var response = Encoding.ASCII.GetString(
            WebSocketUpgrade.BuildHandshakeResponse("s3pPLMBiTxaQ9kYGzzhZRbK+xOo="));

        response.ShouldStartWith("HTTP/1.1 101 Switching Protocols\r\n");
        response.ShouldContain("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\n");
        response.ShouldEndWith("\r\n\r\n");
    }

    [Theory]
    [InlineData("/ws/sync?token=abc", "token", true, "abc")]
    [InlineData("/ws/sync?a=1&token=x%20y&b=2", "token", true, "x y")]
    [InlineData("/ws/sync?token=", "token", true, "")]
    [InlineData("/ws/sync", "token", false, "")]
    [InlineData("/ws/sync?other=abc", "token", false, "")]
    [InlineData("/ws/sync?", "token", false, "")]
    public void QueryParameterExtraction(string target, string name, bool expectedFound, string expectedValue)
    {
        var found = WebSocketUpgrade.TryGetQueryParameter(target, name, out var value);

        found.ShouldBe(expectedFound);
        if (expectedFound)
            value.ShouldBe(expectedValue);
    }

    private static Dictionary<string, string> Headers(params (string Key, string Value)[] entries)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
            headers[key] = value;
        return headers;
    }
}
