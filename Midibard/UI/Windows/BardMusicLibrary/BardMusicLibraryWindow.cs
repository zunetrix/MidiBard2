using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using BardMusicPlayer.XIVMIDI;
using BardMusicPlayer.XIVMIDI.IO;
using BardMusicPlayer.XIVMIDI.Events;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Managers;
using MidiBard.Playlist;
using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

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

record PreparedFilters(string[] Artist, string[] Title, string[] Arranger, int Ensemble);

public class BardMusicLibraryWindow : ThemedWindow
{
    private static readonly Vector4 XivTagText = Style.HexToVector4("#6DA7EC");
    private static readonly Vector4 XivTagBg = Style.HexToVector4("#032042");
    private static readonly Vector4 BmpTagText = Style.HexToVector4("#A096EB");
    private static readonly Vector4 BmpTagBg = Style.HexToVector4("#1D1649");

    private Plugin Plugin { get; }

    // Request (sent to the server)
    private string _title = "";
    private string _artist = "";
    private string _editor = "";
    private int _ensemble = 0;
    private int _source = (int)SearchSource.Both;
    private int _sort = 0;

    // Result view (cache and sort)
    private SearchSnapshot _lastSnap; // Set to null to force a rebuild of _localEntries on the next frame (snapshot, sort or filter changed)
    private IReadOnlyList<OnlineFileEntry> _localEntries = [];
    private Column _sortColumn;
    private bool _sortDescending;
    private static readonly StringComparer TextComparer = StringComparer.InvariantCultureIgnoreCase;

    // Local filters
    private string _localFilterQ = "";
    private string _localArtistFilter = "";
    private string _localTitleFilter = "";
    private string _localArrangerFilter = "";
    private int _localEnsembleFilter = 0;

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

        DrawLocalFilter(currSnap);

        DrawTable(currSnap);
    }
    private void DrawFilters()
    {
        float availW = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float thirdW = (availW - 2 * spacing) / 3;

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
            Plugin.OnlineSearch.Start(new SearchFilters(_artist, _title, _editor, _ensemble, (SearchSource)_source, (SortOrder)_sort));
        }
        if (snapshot.Entries.Count > 0)
        {
            var loadingParts = new List<string>();
            if (snapshot.XivmidiProgress is not null)
            {
                loadingParts.Add(FormatProgress("XIVMIDI", snapshot.XivmidiProgress));
            }
            if (snapshot.BmpProgress is not null)
            {
                loadingParts.Add(FormatProgress("BMP", snapshot.BmpProgress));
            }

            ImGui.SameLine();
            ImGui.TextDisabled(string.Join(" · ", loadingParts));


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


        if (!string.IsNullOrWhiteSpace(snapshot.BmpProgress?.Error))
        {
            ImGui.TextColored(Style.Colors.Red, "bmp: " + snapshot.BmpProgress.Error);
        }
        if (!string.IsNullOrWhiteSpace(snapshot.XivmidiProgress?.Error))
        {
            ImGui.TextColored(Style.Colors.Red, "xivmidi: " + snapshot.XivmidiProgress.Error);
        }

    }
    private void DrawLocalFilter(SearchSnapshot snapshot)
    {
        if (ImGui.InputTextWithHint("##LocalQFilter", "Filter results...", ref _localFilterQ))
        {
            _lastSnap = null;
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Eraser, "Clear"))
        {
            _lastSnap = null;
            _localFilterQ = "";
            _localArtistFilter = "";
            _localTitleFilter = "";
            _localArrangerFilter = "";
            _localEnsembleFilter = 0;
        }
        if (snapshot.Entries.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"{_localEntries.Count} / {snapshot.Entries.Count} shown");
        }
    }
    private void DrawTable(SearchSnapshot snapshot)
    {
        RebuildLocalEntriesIfNeeded(snapshot);

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                    ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.Hideable |
                    ImGuiTableFlags.Sortable;

        const int Cols = 11;        // Must match the number of TableSetupColumn calls in SetupColumns

         // Renamed from ##bodyTbl so layouts saved before sorting existed do not override the default sort.
        using var body = ImRaii.Table("##bodyTbl2", Cols, flags);
        if (!body) return;

        SetupColumns();
        ImGui.TableSetupScrollFreeze(0, 2);
        ImGui.TableHeadersRow();

        ImGui.TableNextRow();
        ImGui.TableNextColumn();                // Src: no filter
        ImGui.TableNextColumn();                // #: no filter
        if (ImGui.TableNextColumn())            // Artist
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##Local Artist", "Artist...", ref _localArtistFilter))
                _lastSnap = null;
        }
        if (ImGui.TableNextColumn())            // Title
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##Local Title", "Title...", ref _localTitleFilter))
                _lastSnap = null;
        }
        if (ImGui.TableNextColumn())            // Arranger
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##Local Arranger", "Arranger...", ref _localArrangerFilter))
                _lastSnap = null;
        }
        if (ImGui.TableNextColumn())            // Ensemble
        {
            ImGui.SetNextItemWidth(-1);
            if (DrawCombo("##Local Ensemble", OnlineEntryMapper.EnsembleLabels, ref _localEnsembleFilter))
                _lastSnap = null;
        }

        var sortSpec = ImGui.TableGetSortSpecs();
        if (!sortSpec.IsNull)
        {
            if (sortSpec.SpecsDirty && sortSpec.SpecsCount > 0)
            {
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
            ImGuiUtil.DrawTag("XIV", XivTagText, XivTagBg);
        }
        else if (entry.Source is EntrySource.Bmp)
        {
            ImGuiUtil.DrawTag("BMP", BmpTagText, BmpTagBg);
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
        TextRightAligned(entry.DownloadsCount.ToString());

        // Likes
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        TextRightAligned(entry.LikesCount.ToString());

        // Options
        ImGui.TableNextColumn();

        using (ImRaii.Disabled(entry.PageUrl.IsNullOrWhitespace()))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "##url", "Open in browser"))
            {
                WindowsApi.OpenUrl(entry.PageUrl);
            }
            if (entry.PageUrl.IsNullOrWhitespace() && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("No web page for this file");
            }
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
    private void RebuildLocalEntriesIfNeeded(SearchSnapshot snapshot)
    {
        if (ReferenceEquals(_lastSnap, snapshot))
            return;

        var globalWords = SplitWords(_localFilterQ);
        var prepared = new PreparedFilters(
            SplitWords(_localArtistFilter),
            SplitWords(_localTitleFilter),
            SplitWords(_localArrangerFilter),
            _localEnsembleFilter);

        var filteredEntries = snapshot.Entries
            .Where(e => Matches(e, globalWords) && PassesColumnFilters(e, prepared))
            .ToList();

        _localEntries = _sortColumn switch
        {
            Column.Uploaded => SortBy(filteredEntries, e => e.PublishedAt, _sortDescending),
            Column.Artist => SortBy(filteredEntries, e => BlankToNull(e.Artist), _sortDescending, TextComparer),
            Column.Title => SortBy(filteredEntries, e => BlankToNull(e.Title), _sortDescending, TextComparer),
            Column.Arranger => SortBy(filteredEntries, e => BlankToNull(e.Arranger), _sortDescending, TextComparer),
            Column.Ensemble => SortBy(filteredEntries, e => e.EnsembleSize, _sortDescending),
            Column.Duration => SortBy(filteredEntries, e => e.DurationSeconds, _sortDescending),
            Column.DL => SortBy(filteredEntries, e => e.DownloadsCount, _sortDescending),
            Column.Likes => SortBy(filteredEntries, e => e.LikesCount, _sortDescending),
            Column.Src => SortBy(filteredEntries, e => e.Source, _sortDescending),
            _ => filteredEntries
        };

        _lastSnap = snapshot;
    }


    //  Helpers
    private static bool DrawCombo(string id, string[] labels, ref int index)
    {
        using var combo = ImRaii.Combo(id, labels[index]);
        if (!combo) return false;
        bool changed = false;

        for (int n = 0; n < labels.Length; n++)
        {
            bool sel = index == n;
            if (ImGui.Selectable(labels[n], sel))
            {
                if (index != n) changed = true;
                index = n;
            }
            if (sel) ImGui.SetItemDefaultFocus();
        }
        return changed;
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
    private static bool Matches(OnlineFileEntry entry, string[] words)
    {
        foreach (var word in words)
        {
            if (
                !ContainsWord(entry.Artist, word) &&
                !ContainsWord(entry.Title, word) &&
                !ContainsWord(entry.Arranger , word)
            ) return false;

        }

        return true;
    }
    private static bool ContainsAllWords(string field, string[] words)
    {
        foreach (var word in words)
        {
            if (!ContainsWord(field, word))
                return false;
        }

        return true;
    }
    private static bool ContainsWord(string field, string word)
    {
        return CultureInfo.InvariantCulture.CompareInfo.IndexOf(field, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }
    private static string[] SplitWords(string s)
    {
        return s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
    private static bool PassesColumnFilters(OnlineFileEntry e, PreparedFilters f)
    {
        return ContainsAllWords(e.Artist, f.Artist) &&
            ContainsAllWords(e.Title, f.Title) &&
            ContainsAllWords(e.Arranger, f.Arranger) &&
            (f.Ensemble == 0 || e.EnsembleSize == f.Ensemble);
    }
    private static string FormatProgress(string name, SourceProgress progress)
    {
        var totalPages = progress.TotalPages == 0 ? "?" : progress.TotalPages.ToString();
        return $"{name} {progress.LoadedPages}/{totalPages}";
    }
    private static void TextRightAligned(string text)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0,
            ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X));
        ImGui.TextDisabled(text);
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

