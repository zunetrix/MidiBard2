using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

using ProtoBuf;

namespace MidiBard;

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
