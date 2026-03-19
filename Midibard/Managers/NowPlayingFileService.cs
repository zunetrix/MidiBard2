using System;
using System.IO;
using System.Threading.Tasks;

namespace MidiBard.Managers;

internal static class NowPlayingFileService
{
    internal static async Task WriteAsync(string filePath, string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(filePath, content);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[NowPlayingFileService] Failed to write now-playing file.");
        }
    }
}
