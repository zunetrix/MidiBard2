using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Dalamud.Interface.ImGuiNotification;

using MidiBard.Util;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using ProtoBuf;

namespace MidiBard;

[ProtoContract]
public class PlaylistContainer
{
    // private Plugin Plugin { get; }
    [ProtoMember(1)] public string FilePathWhenLoading = null;
    [ProtoMember(2)] public List<SongEntry> SongPaths = new();
    [ProtoMember(3)] private int _currentSongIndex = -1;

    [ProtoMember(4)]
    public int CurrentSongIndex
    {
        get => _currentSongIndex;
        set => _currentSongIndex = value.Clamp(-1, SongPaths.Count - 1);
    }

    private int lastReadDurationtick = 0;
    private TimeSpan _totalDuration;
    public TimeSpan TotalDuration
    {
        get
        {
            try
            {
                var tickCount = Environment.TickCount;
                if (tickCount > lastReadDurationtick + 20)
                {
                    var d = 0L;
                    for (var i = 0; i < SongPaths.Count; i++)
                    {
                        d += SongPaths[i].SongLength.Ticks;
                    }

                    var fromTicks = TimeSpan.FromTicks(d);
                    _totalDuration = fromTicks;
                    lastReadDurationtick = tickCount;
                    return fromTicks;
                }
            }
            catch
            {
                // ignored
            }

            return _totalDuration;
        }
    }

    public SongEntry? CurrentSongEntry
    {
        get
        {
            if (CurrentSongIndex < 0)
            {
                return null;
            }

            try
            {
                return SongPaths[CurrentSongIndex];
            }
            catch
            {
                return null;
            }
        }
    }

    public string DisplayName => Path.GetFileNameWithoutExtension(FilePathWhenLoading);

    // TODO: remove plugin dependency
    public PlaylistContainer(string filePathWhenLoading, Plugin plugin)
    {
        Plugin = plugin;
        FilePathWhenLoading = filePathWhenLoading;
    }
    // private PlaylistContainer() { }


    public PlaylistContainer FromFile(string filePath, bool createIfNotExist = false)
    {
        if (File.Exists(filePath))
        {
            RecordToRecentUsed(filePath);

            if (IsJsonPlaylistFile(filePath))
            {
                try
                {
                    return FromJsonFile(filePath);
                }
                catch (Exception e)
                {
                    ImGuiUtil.AddNotification(NotificationType.Warning, $"Invalid playlist format: {e.Message}");
                    DalamudApi.PluginLog.Warning($"Invalid playlist format: {e.Message}");
                    return null;
                }
            }

            return null;
        }

        if (!createIfNotExist) return null;

        var newContainer = new PlaylistContainer(filePath, Plugin);
        newContainer.Save(filePath);
        RecordToRecentUsed(filePath);
        return newContainer;
    }

    public PlaylistContainer FromJsonFile(string filePath)
    {
        var json = File.ReadAllText(filePath, Encoding.UTF8);
        var root = JObject.Parse(json);
        var songs = root["Songs"] as JArray;

        if (songs == null)
        {
            DalamudApi.PluginLog.Warning("No songs found in file");
            return new PlaylistContainer(filePath, Plugin);
        }

        var container = new PlaylistContainer(filePath, Plugin);

        foreach (var song in songs)
        {
            try
            {
                var filePathValue = song["FilePath"]?.ToString();
                var songLengthStr = song["SongLength"]?.ToString();
                var isPlayed = song["IsFilePlayed"]?.ToObject<bool>() ?? false;

                if (string.IsNullOrEmpty(filePathValue))
                    continue;

                var fullPath = Path.GetFullPath(filePathValue, filePath);
                var songLength = TimeSpan.TryParse(songLengthStr, out var ts) ? ts : TimeSpan.Zero;

                container.SongPaths.Add(new SongEntry
                {
                    FilePath = fullPath,
                    SongLength = songLength,
                    IsFilePlayed = isPlayed
                });
            }
            catch
            {
                //  ignore invalid entry
            }
        }

        // TODO: update total playlist duration
        return container;
    }

    private bool IsJsonPlaylistFile(string filePath)
    {
        try
        {
            var firstNonEmptyLine = File.ReadLines(filePath, Encoding.UTF8)
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();

            return firstNonEmptyLine != null && firstNonEmptyLine.StartsWith("{");
        }
        catch
        {
            return false;
        }
    }

    private void RecordToRecentUsed(string filePath)
    {
        var usedPlaylists = Plugin.Config.RecentUsedPlaylists;
        if (usedPlaylists.Contains(filePath))
        {
            usedPlaylists.Remove(filePath);
        }

        usedPlaylists.Add(filePath);

        const int maxRecentRecordSize = 30;
        if (usedPlaylists.Count > maxRecentRecordSize)
        {
            usedPlaylists.RemoveRange(0, usedPlaylists.Count - maxRecentRecordSize);
        }
    }

    public void Save()
    {
        Save(FilePathWhenLoading, this);
    }

    public void Save(string filePath)
    {
        Save(filePath, this);
    }

    public void Save(string filePath, PlaylistContainer obj)
    {
        try
        {
            RecordToRecentUsed(filePath);
            obj.FilePathWhenLoading = filePath;

            var playlistJson = new PlaylistJson
            {
                PlaylistName = "Midibard playlist",
                PlaylistTotalDuration = obj.TotalDuration,
                Songs = obj.SongPaths.Select(song => new SongEntry
                {
                    FilePath = Path.GetRelativePath(filePath, song.FilePath),
                    SongLength = song.SongLength,
                    IsFilePlayed = song.IsFilePlayed
                }).ToList()
            };

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };

            var json = JsonConvert.SerializeObject(playlistJson, settings);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, "Error when saving playlist");
        }
    }

    public void ExportToCsv(string filePath, string capturePattern, string capturedOutputReplacement, string findPattern, string replacement)
    {
        try
        {
            RecordToRecentUsed(filePath);
            FilePathWhenLoading = filePath;

            var sb = new StringBuilder();

            // header
            sb.AppendLine("Song;Duration");
            sb.AppendLine($"Midibard playlist;{Util.Extensions.GetDurationString(TotalDuration)}");

            // song list
            foreach (var song in SongPaths)
            {
                var songName = PlaylistManager.ExtractSongName(
                    song.FileName,
                    capturePattern,
                    capturedOutputReplacement,
                    findPattern,
                    replacement
                );

                sb.AppendLine($"{songName};{song.SongLengthFormated}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, "Error when saving playlist as CSV");
        }
    }
}

[ProtoContract]
public class SongEntry
{
    [ProtoMember(1)] public string FilePath;
    // [JsonConverter(typeof(BaseNumberConverter))]
    [ProtoMember(2)] public TimeSpan SongLength;
    [ProtoMember(3)] public bool IsFilePlayed;
    [JsonIgnore] private string _name;
    [JsonIgnore] public string FileName => _name ??= Path.GetFileNameWithoutExtension(FilePath);
    [JsonIgnore] public string FileDirectory => Path.GetDirectoryName(FilePath);
    [JsonIgnore] public string SongLengthFormated => $"{(SongLength.Hours != 0 ? SongLength.Hours + ":" : "")}{SongLength.Minutes:00}:{SongLength.Seconds:00}";
    [JsonIgnore] public string LrcPath => Path.ChangeExtension(FilePath, "lrc");
}

public class PlaylistJson
{
    public string PlaylistName { get; set; }
    public TimeSpan PlaylistTotalDuration { get; set; }
    public List<SongEntry> Songs { get; set; } = new();
}


