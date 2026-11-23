using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace MidiBard.RemoteSync;

/// <summary>
/// Pure helpers for the RFC 6455 WebSocket opening handshake, used by the
/// remote-control listener to upgrade a plain HTTP connection.
/// </summary>
internal static class WebSocketUpgrade
{
    private const string HandshakeGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>
    /// Whether the request asks for a WebSocket upgrade. Header values are
    /// treated as case-insensitive token lists because tunnels/proxies
    /// (cloudflared, ngrok) rewrite casing and may append tokens.
    /// </summary>
    public static bool IsUpgradeRequest(string method, IReadOnlyDictionary<string, string> headers)
    {
        return string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && headers.TryGetValue("Upgrade", out var upgrade)
            && ContainsToken(upgrade, "websocket")
            && headers.TryGetValue("Sec-WebSocket-Key", out var key)
            && !string.IsNullOrWhiteSpace(key);
    }

    public static string ComputeAcceptKey(string secWebSocketKey)
    {
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(secWebSocketKey.Trim() + HandshakeGuid));
        return Convert.ToBase64String(hash);
    }

    public static byte[] BuildHandshakeResponse(string acceptKey)
    {
        return Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n" +
            "\r\n");
    }

    /// <summary>Extracts a query parameter from a raw request target such as <c>/ws/sync?token=abc</c>.</summary>
    public static bool TryGetQueryParameter(string target, string name, out string value)
    {
        value = string.Empty;
        var queryIndex = target.IndexOf('?');
        if (queryIndex < 0 || queryIndex == target.Length - 1)
            return false;

        foreach (var pair in target[(queryIndex + 1)..].Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
                continue;

            if (!string.Equals(pair[..separator], name, StringComparison.Ordinal))
                continue;

            value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            return true;
        }

        return false;
    }

    private static bool ContainsToken(string headerValue, string token)
    {
        foreach (var part in headerValue.Split(','))
        {
            if (string.Equals(part.Trim(), token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
