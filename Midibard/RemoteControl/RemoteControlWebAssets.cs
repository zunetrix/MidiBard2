using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MidiBard.RemoteControl;

internal sealed record RemoteControlWebAsset(string ContentType, byte[] Content);

internal interface IRemoteControlWebAssetProvider
{
    Task<RemoteControlWebAsset?> GetInstrumentIconAsync(int iconId);
}

internal static class RemoteControlWebAssets
{
    private const string ResourcePathPrefix = "RemoteControl.Web.";
    private static readonly string[] ResourceNames =
        typeof(RemoteControlWebAssets).Assembly.GetManifestResourceNames();

    public static bool TryGet(string requestPath, out RemoteControlWebAsset asset)
    {
        asset = null!;
        if (!TryGetRelativePath(requestPath, out var relativePath))
            return false;

        var resourceSuffix = ResourcePathPrefix + relativePath.Replace('/', '.');
        var resourceName = ResourceNames.SingleOrDefault(name =>
            name.EndsWith(resourceSuffix, StringComparison.Ordinal));
        if (resourceName == null)
            return false;

        using var stream = typeof(RemoteControlWebAssets).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return false;

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        asset = new RemoteControlWebAsset(ContentType(relativePath), memory.ToArray());
        return true;
    }

    private static bool TryGetRelativePath(string requestPath, out string relativePath)
    {
        relativePath = requestPath switch
        {
            "/" or "/index.html" or "/docs" or "/docs/" => "index.html",
            "/app.js" => "app.js",
            "/styles.css" => "styles.css",
            "/licenses/preact.txt" => "vendor/preact/LICENSE.txt",
            _ => string.Empty,
        };

        if (relativePath.Length > 0)
            return true;

        const string preactPrefix = "/vendor/preact/";
        if (!requestPath.StartsWith(preactPrefix, StringComparison.Ordinal))
            return false;

        var vendorPath = requestPath[preactPrefix.Length..];
        if (vendorPath.Length == 0 ||
            vendorPath.Contains("..", StringComparison.Ordinal) ||
            vendorPath.Contains('\\'))
        {
            return false;
        }

        relativePath = "vendor/preact/" + vendorPath;
        return true;
    }

    private static string ContentType(string path)
    {
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            return "text/html; charset=utf-8";
        if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            return "text/javascript; charset=utf-8";
        if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            return "text/css; charset=utf-8";
        return "text/plain; charset=utf-8";
    }
}
