using System.IO;
using System.Linq;
using System.Numerics;

using BardMusicPlayer.XIVMIDI;
using BardMusicPlayer.XIVMIDI.IO;
using BardMusicPlayer.XIVMIDI.Events;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Resources;
using MidiBard.Playlist;
using MidiBard.Util;
using MidiBard.Managers;
using System;
using System.Collections.Generic;
using Dalamud.Utility;

namespace MidiBard;

public class BardMusicLibraryWindow : ThemedWindow
{
    private Plugin Plugin { get; }




    // Filter state
    private string _title = "";
    private string _artist = "";
    private string _editor = "";
    private int _ensemble = 0;
    private int _source = 2;
    private int _sort = 0;

    // Local Filter
    private SearchSnapshot _lastSnap;
    private IReadOnlyList<OnlineFileEntry> _localEntries;
    private Column _sortColumn;
    private bool _sortDescending;
    static readonly StringComparer TextComparer = StringComparer.InvariantCultureIgnoreCase;

    // Combo labels
    private static readonly string[] SourceLabels =
        { "xivmidi.com", "bardmusicplayer.com", "Both" };

    private static readonly string[] SortLabels =
        { "Newest first", "Oldest first", "A → Z", "Z → A", "Most downloaded" };


    public BardMusicLibraryWindow(Plugin plugin)
        : base($"{Plugin.Name} {Language.window_online_midi_browser}###BardMusicLibraryWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(640, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    //  Draw
    public override void Draw()
    {
        var currSnap = Plugin.OnlineSearch.Snapshot;

        DrawFilters();
        DrawSearchState(currSnap);

        ImGui.Spacing();

        if (currSnap.Running)
            ImGuiUtil.DrawColoredBanner("Loading...", Style.Colors.Violet);
        ImGui.Spacing();


        DrawTable(currSnap);
    }

    private void DrawFilters()
    {
        float availW = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float thirdW = (availW - 2 * spacing) / 3;
        float btnW = ImGui.GetFrameHeight();

        //  Row 1: Artist - Title - Editor | third width
        ImGui.SetNextItemWidth(thirdW);
        ImGui.InputTextWithHint("##artist", "Artist", ref _artist, 255,
            ImGuiInputTextFlags.AutoSelectAll);

        ImGui.SameLine();

        ImGui.SetNextItemWidth(thirdW);
        ImGui.InputTextWithHint("##title", "Title", ref _title, 128,
            ImGuiInputTextFlags.AutoSelectAll);

        ImGui.SameLine();

        ImGui.SetNextItemWidth(thirdW);
        ImGui.InputTextWithHint("##editor", "Editor / Arranger", ref _editor, 128,
            ImGuiInputTextFlags.AutoSelectAll);

        //  Row 2: ensemble + source + sort, with labels above
        using (var table = ImRaii.Table("##filterCols", 3,
                   ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.SizingStretchSame))
        {
            if (table)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextDisabled("Ensemble");

                ImGui.TableNextColumn();
                ImGui.TextDisabled("Source");

                ImGui.TableNextColumn();
                ImGui.TextDisabled("Sort");


                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                DrawCombo("##ensemble", OnlineEntryMapper.EnsembleLabels, ref _ensemble);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                DrawCombo("##source", SourceLabels, ref _source);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                DrawCombo("##sort", SortLabels, ref _sort);

            }
        }
    }

    //  Page controls
    private void DrawSearchState(SearchSnapshot snapshot)
    {

        using (ImRaii.Disabled(!snapshot.Running))
        {
            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, "##cancel", Language.common_action_cancel))
            {
                Plugin.OnlineSearch.Cancel();
            }
        }
        ImGui.SameLine();

        if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Sync, "##doSearch", "Search / reload"))
        {
            Plugin.OnlineSearch.Start(new SearchFilters(_artist, _title, _editor, _ensemble, _source, _sort));
        }
        if (snapshot.Entries.Count > 0)
        {
            if (snapshot.Entries.Count == 1)
            {
                ImGui.TextDisabled($"Result {snapshot.Entries.Count}" );
            }
            else
            {
                ImGui.TextDisabled($"Results {snapshot.Entries.Count}" );
            }

            if (snapshot.SkippedNoUrl > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"-   (skipped {snapshot.SkippedNoUrl} results found without download link)" );
            }
        }
        else
        {
            if (snapshot.SkippedNoUrl > 0)
            {
                ImGui.TextDisabled($"skipped {snapshot.SkippedNoUrl} results found without download link" );
            }
        }


        if (snapshot.Truncated)
        {
            ImGui.Spacing();
            var text = "/!\\ Partial results: refine your search";
            ImGuiUtil.DrawTag(text, Style.Colors.WarningText, Style.Colors.WarningBg);
        }


        if (!string.IsNullOrWhiteSpace(snapshot.BMP_Progress?.Error))
        {
            ImGui.TextColored(Style.Colors.Red, "bmp: " + snapshot.BMP_Progress.Error);
        }
        if (!string.IsNullOrWhiteSpace(snapshot.XIVMIDI_Progress?.Error))
        {
            ImGui.TextColored(Style.Colors.Red, "xivmidi: " + snapshot.XIVMIDI_Progress.Error);
        }

    }

    //  Table
    private void DrawTable(SearchSnapshot snapshot)
    {

        if (!ReferenceEquals(_lastSnap, snapshot))
        {
            _localEntries = _sortColumn switch
            {
                Column.Uploaded => SortBy(snapshot.Entries, e => e.PublishedAt, _sortDescending),
                Column.Artist => SortBy(snapshot.Entries, e => BlankToNull(e.Artist), _sortDescending, TextComparer),
                Column.Title => SortBy(snapshot.Entries, e => BlankToNull(e.Title), _sortDescending, TextComparer),
                Column.Arranger => SortBy(snapshot.Entries, e => BlankToNull(e.Arranger), _sortDescending, TextComparer),

                Column.Ensemble => SortBy(snapshot.Entries, e => e.EnsembleSize, _sortDescending),
                Column.Duration => SortBy(snapshot.Entries, e => e.DurationSeconds, _sortDescending),
                Column.DL => SortBy(snapshot.Entries, e => e.DownloadsCount, _sortDescending),
                Column.Likes => SortBy(snapshot.Entries, e => e.LikesCount, _sortDescending),
                Column.Src => SortBy(snapshot.Entries, e => e.Source, _sortDescending),

                _ => snapshot.Entries
            };

            _lastSnap = snapshot;
        }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                    ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.Hideable |
                    ImGuiTableFlags.Sortable;

        const int Cols = 11;


        using var body = ImRaii.Table("##bodyTbl2", Cols, flags, new Vector2(0,0));
        if (!body) return;

        SetupColumns();
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var sortSpec = ImGui.TableGetSortSpecs();
        if (!sortSpec.IsNull)
        {
            if (sortSpec.SpecsDirty && sortSpec.SpecsCount > 0)
            {
                DalamudApi.PluginLog.Debug("Column User ID: {c}, sort direction: {d}", sortSpec.Specs.ColumnUserID, sortSpec.Specs.SortDirection);
                _sortColumn = (Column)sortSpec.Specs.ColumnUserID;
                _sortDescending = sortSpec.Specs.SortDirection == ImGuiSortDirection.Descending;
                _lastSnap = null;
                sortSpec.SpecsDirty = false;
            }
        }



        ImGuiListClipperPtr clipper;
        unsafe { clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper()); }
        clipper.Begin(_localEntries.Count);

        while (clipper.Step())
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= _localEntries.Count) break;
                DrawRow(i, _localEntries[i]);
            }

        clipper.End();
        clipper.Destroy();
    }

    private static void SetupColumns()
    {
        ImGui.TableSetupColumn("Src", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 36, (uint)Column.Src);
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.NoSort, 36f);
        ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide, 1.4f, (uint)Column.Artist);
        ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide, 2f, (uint)Column.Title);
        ImGui.TableSetupColumn("Arranger", ImGuiTableColumnFlags.WidthStretch, 1f, (uint)Column.Arranger);
        ImGui.TableSetupColumn("Ensemble", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 70f, (uint)Column.Ensemble);
        ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 52f, (uint)Column.Duration);
        ImGui.TableSetupColumn("Uploaded", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, 76, (uint)Column.Uploaded);
        ImGui.TableSetupColumn("DL", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.PreferSortDescending, 36f, (uint)Column.DL);
        ImGui.TableSetupColumn("Likes", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.PreferSortDescending, 36f, (uint)Column.Likes);
        ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.NoSort, 100f);
    }

    private void DrawRow(int i, OnlineFileEntry entry)
    {
        using var id = ImRaii.PushId(i);

        ImGui.TableNextRow();

        // Src
        ImGui.TableNextColumn();
        if (entry.Source is EntrySource.Xivmidi)
        {
            ImGuiUtil.DrawTag("XIV", new Vector4(0.427f, 0.655f, 0.925f, 1f), new Vector4(0.012f, 0.125f, 0.259f, 1f) );

        }
        else if (entry.Source is EntrySource.Bmp)
        {
            ImGuiUtil.DrawTag("BMP", new Vector4(0.627f, 0.588f, 0.922f, 1f), new Vector4(0.114f, 0.086f, 0.286f, 1f) );
        }

        // #
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled($"{i + 1:0000}");

        // Artist - double-click → playback
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        if (ImGui.Selectable(entry.Artist, false, ImGuiSelectableFlags.AllowDoubleClick)
            && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            EnqueueDownload(entry, BMLDownload.Playback);

        // Title
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(entry.Title);


        // Arranger
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(entry.Arranger);

        // Ensemble
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(entry.EnsembleText);

        // Duration
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(entry.DurationText);

        // Uploaded
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(entry.PublishedText);

        // Downloads
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        var downloadCount = entry.DownloadsCount.ToString();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(downloadCount).X));
        ImGui.TextDisabled(downloadCount);

        // Likes
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        var likeCount = entry.LikesCount.ToString();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(likeCount).X));
        ImGui.TextDisabled(likeCount);

        // Options
        ImGui.TableNextColumn();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "##url", "Open in browser"))
        {
            WindowsApi.OpenUrl($"https://bardmusicplayer.com/midis#midi={entry.Id}");
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Download, "##dl", "Add to playlist"))
        {
            EnqueueDownload(entry, BMLDownload.ToPlaylist);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, "##play", "Load to playback"))
        {
            EnqueueDownload(entry, BMLDownload.Playback);
        }
    }

    //  Helpers
    private static void DrawCombo(string id, string[] labels, ref int index)
    {
        using var combo = ImRaii.Combo(id, labels[index]);
        if (!combo) return;

        for (int n = 0; n < labels.Length; n++)
        {
            bool sel = index == n;
            if (ImGui.Selectable(labels[n], sel)) index = n;
            if (sel) ImGui.SetItemDefaultFocus();
        }
    }
    private static List<OnlineFileEntry> SortBy<TKey>(IReadOnlyList<OnlineFileEntry> entries, Func<OnlineFileEntry, TKey> key, bool descending, IComparer<TKey>? comparer = null)
    {
        if (descending)
        {
            return entries
                .OrderBy(e => key(e) is null)
                .ThenByDescending(key, comparer)
                .ToList();
        }
        else
        {
            return entries
                .OrderBy(e => key(e) is null)
                .ThenBy(key, comparer)
                .ToList();
        }
    }
    private static string? BlankToNull(string s)
    {
        return s.IsNullOrWhitespace() ? null : s;
    }
    //  Requests
    private void EnqueueDownload(OnlineFileEntry entry, BMLDownload type)
    {

        if (type == BMLDownload.Playback)
            Plugin.ChatWatcher.SendDownloadSong(entry.Url);

        XIVMidiApi.Instance.GetMidiFile(entry.Url, type, entry.Source == EntrySource.Bmp);
    }

    private void HandleMidiDownload(XIVMIDIResponseContainer.MidiFile midi, BMLDownload type)
    {
        if (type == BMLDownload.ToPlaylist)
        {

            if ((Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0) > 0)
            {
                var dir = Path.GetDirectoryName(Plugin.PlaylistManager.CurrentPlaylist!.Songs.First().GetFilePath());
                var path = Path.Combine(dir!, midi.Filename);
                File.WriteAllBytes(path, midi.data);
                _ = Plugin.PlaylistManager.AddSongsAsync(new[] { path }.AsEnumerable());
            }
            else
            {
                Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
                    "Choose save folder", (ok, folder) =>
                    {
                        if (!ok || !Directory.Exists(folder)) return;
                        var path = Path.Combine(folder, midi.Filename);
                        File.WriteAllBytes(path, midi.data);
                        _ = Plugin.PlaylistManager.AddSongsAsync(new[] { path }.AsEnumerable());
                    });
            }
        }
        else
        {
            if (DalamudApi.PartyList.IsPartyLeader())
                Plugin.IpcProvider.SendDownloadedSong(midi.Filename, midi.data);

            _ = Plugin.FilePlayback.LoadPlayback(midi.Filename, new MemoryStream(midi.data));
        }
    }

    public void OnRequestError(object sender, XIVMidiApiErrorEvent e)
    {
        if (e.RequestKind is RequestKind.MidiDownload)
        {
            ImGuiUtil.AddNotification(Dalamud.Interface.ImGuiNotification.NotificationType.Error, $"Download failed: [{e.ErrorCode}] {e.Message}");
        }
    }

    public void OnMidiFile(object sender, XIVMidiFileEvent e)
    {
        var type = e.Arguments is BMLDownload t ? t : BMLDownload.Playback;
        HandleMidiDownload(e.MidiData, type);
    }
}

public record SearchFilters(string Artist, string Title, string Editor, int Ensemble, int Source, int Sort);
public enum EntrySource
{
    Unknown,
    Xivmidi,
    Bmp
}

enum BMLDownload
{
    Playback,
    ToPlaylist
}

enum Column : uint
{
    None,
    Src,
    Artist,
    Title,
    Arranger,
    Ensemble,
    Duration,
    Uploaded,
    DL,
    Likes
}
