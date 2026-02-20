using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Dalamud.Interface.ImGuiNotification;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Util;

namespace MidiBard;

internal class PlaylistManagerFile
{
    private Plugin Plugin { get; }
    public List<SongEntry> FilePathList => CurrentContainer.SongPaths;
    private PlaylistContainer _currentContainer;
    internal readonly ReadingSettings readingSettings;

    public PlaylistContainer CurrentContainer
    {
        get => _currentContainer ??= LoadLastPlaylist();
        set
        {
            _currentContainer = value;
            Plugin.IpcProvider.SyncPlaylist();
        }
    }

    public int CurrentSongIndex
    {
        get => CurrentContainer.CurrentSongIndex;
        private set => CurrentContainer.CurrentSongIndex = value;
    }

    public PlaylistManagerFile(Plugin plugin)
    {
        Plugin = plugin;

        readingSettings = new ReadingSettings
        {
            NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
            NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
            InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.ReadValid,
            InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
            InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
            MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
            UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
            ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read,
            UnknownChunkIdPolicy = UnknownChunkIdPolicy.ReadAsUnknownChunk,
            SilentNoteOnPolicy = SilentNoteOnPolicy.NoteOff,
            TextEncoding = Plugin.Config.UiLanguage == "zh-Hans" || Plugin.Config.UiLanguage == "zh-Hant"
            ? Encoding.GetEncoding("gb18030")
            : Encoding.Default,
            InvalidSystemCommonEventParameterValuePolicy = InvalidSystemCommonEventParameterValuePolicy.SnapToLimits
        };
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

    internal PlaylistContainer LoadLastPlaylist()
    {
        var lastPlaylistFilePath = Plugin.Config.RecentUsedPlaylists.LastOrDefault();

        if (!string.IsNullOrEmpty(lastPlaylistFilePath) && File.Exists(lastPlaylistFilePath))
        {
            DalamudApi.PluginLog.Information($"Load playlist: {lastPlaylistFilePath}");
            RecordToRecentUsed(lastPlaylistFilePath);

            // reload
            if (_currentContainer != null && _currentContainer.FilePathWhenLoading == lastPlaylistFilePath)
            {
                if (_currentContainer.ReloadFromFile(lastPlaylistFilePath))
                {
                    return _currentContainer;
                }
            }

            // frist time create
            _currentContainer = new PlaylistContainer(lastPlaylistFilePath);
            return _currentContainer.LoadOrUpdate(lastPlaylistFilePath);
        }

        ImGuiUtil.AddNotification(NotificationType.Error,
            $"Latest playlist NOT exist: {lastPlaylistFilePath}, using default playlist instead!");

        var defaultPath = Path.Combine(Plugin.Config.defaultPlaylistFolder ?? DalamudApi.PluginInterface.GetPluginConfigDirectory(), "DefaultPlaylist.mpl");
        DalamudApi.PluginLog.Information($"Load Default playlist: {defaultPath}");
        RecordToRecentUsed(defaultPath);

        // path already exists = reload
        if (_currentContainer != null && _currentContainer.FilePathWhenLoading == defaultPath)
        {
            if (_currentContainer.ReloadFromFile(defaultPath))
            {
                return _currentContainer;
            }
        }

        // frist time create
        _currentContainer = new PlaylistContainer(defaultPath);
        return _currentContainer.LoadOrUpdate(defaultPath, true);
    }

    internal void SetContainerPrivate(PlaylistContainer newContainer) => _currentContainer = newContainer;

    public void SortBy<TKey>(Func<SongEntry, TKey>? orderBy = null, bool descending = false) where TKey : IComparable
    {
        if (orderBy == null) return;

        SongEntry? currentSongItem = null;
        if (CurrentSongIndex >= 0 && CurrentSongIndex < FilePathList.Count)
        {
            currentSongItem = FilePathList[CurrentSongIndex];
        }

        CurrentContainer.SongPaths = descending
            ? CurrentContainer.SongPaths.OrderByDescending(orderBy).ToList()
            : CurrentContainer.SongPaths.OrderBy(orderBy).ToList();

        // update CurrentSongIndex after order
        if (currentSongItem != null)
        {
            CurrentSongIndex = FilePathList.IndexOf(currentSongItem);
        }

        Plugin.IpcProvider.SyncPlaylist();
    }

    public void Clear()
    {
        FilePathList.Clear();
        CurrentSongIndex = -1;
        Plugin.IpcProvider.SyncPlaylist();
    }
    public void RemoveSync(int songIndex)
    {
        var pmdUseChatPlaylistSync = Plugin.Config.playOnMultipleDevices && Plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            Plugin.PartyChatCommand.SendRemoveSong(songIndex);
            return;
        }

        RemoveLocal(songIndex);
        Plugin.IpcProvider.RemoveTrackIndex(songIndex);
        CurrentContainer.Save();
    }

    public void RemoveLocal(int songIndex)
    {
        if (!IsValidSongIndex(songIndex)) return;

        try
        {
            FilePathList.RemoveAt(songIndex);

            // RecalculateCurrentSongIndexAfterRemove
            if (CurrentSongIndex == -1) return;
            if (songIndex < CurrentSongIndex)
            {
                CurrentSongIndex--;
            }
            else if (songIndex == CurrentSongIndex)
            {
                if (CurrentSongIndex >= FilePathList.Count)
                {
                    CurrentSongIndex = FilePathList.Count - 1;
                }
            }

            // DalamudApi.PluginLog.Warning($"RemoveLocal song [{songIndex}]");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, $"error when removing song [{songIndex}]");
        }
    }

    internal void CalculateCurrentSongIndexAfterReorder(int songIndex, int targetIndex)
    {
        // if the item has been moved to a position before the current song index entire playlist shift one position
        if (CurrentSongIndex == -1) return;
        if (songIndex == CurrentSongIndex)
        {
            CurrentSongIndex = targetIndex;
        }
        else if (songIndex < CurrentSongIndex && targetIndex >= CurrentSongIndex)
        {
            CurrentSongIndex--;
        }
        else if (songIndex > CurrentSongIndex && targetIndex <= CurrentSongIndex)
        {
            CurrentSongIndex++;
        }
    }

    public void MoveSongToIndexSync(int songIndex, int targetIndex)
    {
        var pmdUseChatPlaylistSync = Plugin.Config.playOnMultipleDevices && Plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            Plugin.PartyChatCommand.SendChangeSongOrder(songIndex, targetIndex);
            return;
        }

        MoveSongToIndexLocal(songIndex, targetIndex);
        Plugin.IpcProvider.MoveSongToIndex(songIndex, targetIndex);
        CurrentContainer.Save();
    }

    public void MoveSongToIndexLocal(int songIndex, int targetIndex)
    {
        if (!IsValidSongIndex(songIndex)) return;
        if (songIndex == targetIndex) return;

        // clamp index
        targetIndex = Math.Clamp(targetIndex, 0, FilePathList.Count);

        var songItem = FilePathList[songIndex];
        FilePathList.RemoveAt(songIndex);

        FilePathList.Insert(targetIndex, songItem);

        CalculateCurrentSongIndexAfterReorder(songIndex, targetIndex);
        // DalamudApi.PluginLog.Warning($"MoveSongToIndexLocal {FilePathList[targetIndex].FileName} {songIndex} => {targetIndex}");
    }

    public void SetCurrentSongAsPlayed()
    {
        if (Plugin.CurrentBardPlayback.IsLoaded)
        {
            var progress = Plugin.CurrentBardPlayback.GetPlaybackProgress();
            // Mark song as played
            var playedThresholdPercent = 0.85;
            if (progress >= playedThresholdPercent)
            {
                ChangeSongPlayedStatusLocal(CurrentSongIndex, true);
            }
        }
    }

    public void ChangeSongPlayedStatusSync(int songIndex, bool isFilePlayed)
    {
        if (!IsValidSongIndex(songIndex)) return;

        ChangeSongPlayedStatusLocal(songIndex, isFilePlayed);
        Plugin.IpcProvider.ChangeSongPlayedStatus(songIndex, isFilePlayed);
        // required if changing the playlist file structure to save the status in the file
        // CurrentContainer.Save();
    }

    public void ChangeSongPlayedStatusLocal(int songIndex, bool isSongPlayed)
    {
        if (!IsValidSongIndex(songIndex)) return;
        var fileItem = FilePathList.ElementAtOrDefault(songIndex);
        if (fileItem != null)
        {
            fileItem.IsFilePlayed = isSongPlayed;
            // TODO:
            // trigger a interface update for playlist redraw
            // if you have filter show only unplayed songs and mark one as played it wont reload the list
        }
    }

    public void ResetAllSongsPlayedStatusSync()
    {
        ResetAllSongsPlayedStatusLocal();
        Plugin.IpcProvider.ResetAllSongsPlayedStatus();
    }

    public void ResetAllSongsPlayedStatusLocal()
    {
        if (FilePathList.Count == 0) return;

        foreach (var fileItem in FilePathList.Where(item => item.IsFilePlayed))
        {
            fileItem.IsFilePlayed = false;
        }
        CurrentContainer.Save();
    }

    internal async Task AddAsync(IEnumerable<string> filePaths)
    {
        var success = 0;
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            foreach (var (file, path) in CheckValidFiles(filePaths))
            {
                try
                {
                    var songLength = file.GetDurationTimeSpan();
                    FilePathList.Add(new SongEntry { FilePath = path, SongLength = songLength ?? TimeSpan.Zero, IsFilePlayed = false });

                    success++;
                }
                catch (Exception e)
                {
                    DalamudApi.PluginLog.Warning(e, "error when getting duration");
                }
            }

            CalculateDurationAll();
        });

        RecordToRecentUsed(CurrentContainer.FilePathWhenLoading);
        Plugin.IpcProvider.SyncPlaylist();
        CurrentContainer.Save();
        DalamudApi.PluginLog.Information($"File import all complete in {sw.Elapsed.TotalMilliseconds} ms! success: {success}");
    }

    internal void CalculateDurationAll()
    {
        var parallelQuery = Plugin.PlaylistManager.FilePathList.AsParallel();
        parallelQuery.ForAll(i =>
        {
            if (i.SongLength == default)
            {
                try
                {
                    i.SongLength = Plugin.PlaylistManager.LoadSongFile(i.FilePath).GetDuration<MetricTimeSpan>();
                }
                catch (Exception e)
                {
                    DalamudApi.PluginLog.Warning(e, $"error when getting {i.FilePath} duration");
                }
            }
        });
    }

    internal void CalculateSongDuration(int songIndex)
    {
        if (!IsValidSongIndex(songIndex)) return;

        try
        {
            // if file doesnt exits remove it from playlist
            if (!File.Exists(FilePathList[songIndex].FilePath))
            {
                RemoveSync(songIndex);
                ImGuiUtil.AddNotification(NotificationType.Warning, $"The song file no longer exists and has been removed from the playlist");
                return;
            }

            FilePathList[songIndex].SongLength = Plugin.PlaylistManager.LoadSongFile(FilePathList[songIndex].FilePath).GetDuration<MetricTimeSpan>();
            CurrentContainer.Save();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, $"error when getting {FilePathList[songIndex].FilePath} duration");
        }
    }

    internal bool IsValidSongIndex(int songIndex)
    {
        var isEmptyList = FilePathList == null || FilePathList.Count == 0;
        var isInvalidIndex = songIndex < 0 || songIndex >= FilePathList.Count;

        if (isEmptyList || isInvalidIndex)
            return false;

        return true;
    }

    private IEnumerable<(MidiFile, string)> CheckValidFiles(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            MidiFile file = null;

            file = LoadSongFile(path);
            if (file is not null) yield return (file, path);
        }
    }

    internal MidiFile LoadSongFile(string path)
    {
        if (Path.GetExtension(path).Equals(".mid") || Path.GetExtension(path).Equals(".midi"))
            return LoadMidiFile(path);
        return null;
    }

    private MidiFile LoadMidiFile(string filePath)
    {
        DalamudApi.PluginLog.Debug($"[LoadMidiFile] -> {filePath} START");
        MidiFile loaded = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!File.Exists(filePath))
            {
                DalamudApi.PluginLog.Warning($"File not exist! path: {filePath}");
                return null;
            }

            using (var f = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                loaded = MidiFile.Read(f, readingSettings);
            }

            DalamudApi.PluginLog.Debug($"[LoadMidiFile] -> {filePath} OK! in {stopwatch.Elapsed.TotalMilliseconds} ms");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Warning(ex, "Failed to load file at {0}", filePath);
        }

        return loaded;
    }

    public MidiFile LoadMidiFile(Stream midi)
    {
        DalamudApi.PluginLog.Debug($"[LoadMidiFile] -> START");
        MidiFile loaded = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (midi == null)
            {
                DalamudApi.PluginLog.Warning($"Stream was empty");
                return null;
            }

            loaded = MidiFile.Read(midi, readingSettings);

            DalamudApi.PluginLog.Debug($"[LoadMidiFile] -> OK! in {stopwatch.Elapsed.TotalMilliseconds} ms");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Warning(ex, "Failed to load from stream.");
        }

        return loaded;
    }

    public async Task<bool> LoadPlayback(int? index = null, bool startPlaying = false, bool sync = true)
    {
        // if (index < 0 || index >= FilePathList.Count)
        // {
        //    DalamudApi.PluginLog.Warning($"LoadPlaybackIndex: invalid playlist index {index}");
        //    return false;
        // }

        if (index is int songIndex)
        {
            CurrentSongIndex = songIndex;
        }

        if (sync)
        {
            Plugin.IpcProvider.LoadPlayback(CurrentSongIndex);
        }

        if (await LoadPlaybackPrivate())
        {
            if (startPlaying)
            {
                Plugin.MidiPlayerControl.DoPlay();
            }

            return true;
        }

        return false;
    }

    public static string ExtractSongName(string input, string capturePattern, string capturedOutputReplacement, string findPattern, string replacement)
    {
        if (string.IsNullOrEmpty(capturePattern) || string.IsNullOrEmpty(capturedOutputReplacement))
            return input;

        try
        {
            return Regex.Replace(input, capturePattern, match =>
            {
                string result = capturedOutputReplacement;

                // replace matching groups
                for (int i = match.Groups.Count - 1; i >= 1; i--)
                {
                    result = result.Replace($"${i}", match.Groups[i].Value);
                }

                // remove any group not found
                result = Regex.Replace(result, @"\$\d+", "");

                // sanitize result using the provided pattern
                if (!string.IsNullOrEmpty(findPattern))
                {
                    result = Regex.Replace(result, findPattern, replacement);
                }

                return result;
            });
        }
        catch
        {
            // ignored
            return input;
        }
    }

    public string GetPostSongName(int songIndex)
    {
        if (!IsValidSongIndex(songIndex))
        {
            return string.Empty;
        }

        var songName = ExtractSongName(
            FilePathList[songIndex].FileName,
            Plugin.Config.postSongNameCaptureRegex,
            Plugin.Config.postSongNameCaptureOutputFormat,
            Plugin.Config.postSongNameFindRegex,
            Plugin.Config.postSongNameReplacement);

        return songName;
    }

    public int FindSongIndex(string songName)
    {
        if (string.IsNullOrWhiteSpace(songName))
            return -1;

        return FilePathList.FindIndex(f =>
            f.FileName.Contains(songName, StringComparison.OrdinalIgnoreCase)
        );
    }

    public void SendSongToChat(int songIndex)
    {
        if (DalamudApi.PartyList.IsInParty() && !DalamudApi.PartyList.IsPartyLeader()) return;
        if (!IsValidSongIndex(songIndex)) return;

        // prevent send again after pausing song
        if (Plugin.MidiPlayerControl._status != MidiPlayerControl.MidiPlayerStatus.Paused)
        {
            var songName = GetPostSongName(songIndex);
            if (songName == "") return;

            var chatComand = Plugin.Config.GetChatCommand(Plugin.Config.SongNameChatTarget);

            var chatText = $"{chatComand}{songName}";
            Chat.SendMessage(chatText);
        }
    }

    private async Task<bool> LoadPlaybackPrivate()
    {
        try
        {
            var songEntry = FilePathList[CurrentSongIndex];
            return await Plugin.FilePlayback.LoadPlayback(songEntry.FilePath);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString());
            return false;
        }
    }
}
