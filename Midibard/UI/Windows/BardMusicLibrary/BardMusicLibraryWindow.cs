using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;

using MidiBard.Extensions.String;
using MidiBard.OnlineLibrary;
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

enum DownloadTarget
{
    Playback,
    Playlist
}

public class BardMusicLibraryWindow : ThemedWindow, IDisposable
{
    private static readonly Vector4 XivTagText = Style.HexToVector4("#6DA7EC");
    private static readonly Vector4 XivTagBg = Style.HexToVector4("#032042");
    private static readonly Vector4 BmpTagText = Style.HexToVector4("#A096EB");
    private static readonly Vector4 BmpTagBg = Style.HexToVector4("#1D1649");

    private Plugin Plugin { get; }

    // Created on the first search, so the HTTP client is not created until the user searches.
    private OnlineSearch? _search;

    // Request (sent to the server)
    private string _title = "";
    private string _artist = "";
    private string _editor = "";
    private int _ensemble = 0;
    private SearchSource _source = SearchSource.All;
    private SortOrder _sort = SortOrder.Newest;

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
    private static readonly (string Label, SearchSource Value)[] SourceOptions =
    {
        ("All", SearchSource.All),
        ("xivmidi.com", SearchSource.Xivmidi),
        ("bardmusicplayer.com", SearchSource.Bmp),
    };

    private static readonly (string Label, SortOrder Value)[] SortOptions =
    {
        ("Newest first", SortOrder.Newest),
        ("Oldest first", SortOrder.Oldest),
        ("A → Z", SortOrder.TitleAsc),
        ("Z → A", SortOrder.TitleDesc),
        ("Most downloaded", SortOrder.MostDownloaded),
    };

    private static readonly (string Label, int Value)[] EnsembleOptions =
        EnsembleSizes.Labels.Select((label, size) => (label, size)).ToArray();


    public BardMusicLibraryWindow(Plugin plugin)
        : base($"{Plugin.Name} {Language.window_online_midi_browser}###BardMusicLibraryWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(640, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose()
    {
        _search?.Cancel();
        base.OnClose();
    }

    public void Dispose()
    {
        _search?.Dispose();
    }

    // Draw
    public override void Draw()
    {
        var currSnap = _search?.Snapshot ?? SearchSnapshot.Empty;

        DrawFilters();
        DrawSearchBar(currSnap);
        DrawSourceErrors(currSnap);

        ImGui.Spacing();
        DrawLocalFilter(currSnap);
        DrawTable(currSnap);
    }

    private void DrawFilters()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var thirdW = (ImGui.GetContentRegionAvail().X - 2 * spacing) / 3;
        var submit = false;

        // Row 1: Artist - Title - Editor. Enter in any of them starts the search.
        const ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue;

        ImGui.SetNextItemWidth(thirdW);
        submit |= ImGui.InputTextWithHint("##artist", "Artist", ref _artist, 255, inputFlags);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(thirdW);
        submit |= ImGui.InputTextWithHint("##title", "Title", ref _title, 128, inputFlags);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(thirdW);
        submit |= ImGui.InputTextWithHint("##editor", "Editor / Arranger", ref _editor, 128, inputFlags);

        //  Row 2: Ensemble - Source - Sort, aligned with the row above. The label is part of the combo preview.
        ImGui.SetNextItemWidth(thirdW);
        DrawCombo("##ensemble", EnsembleOptions, ref _ensemble, "Ensemble");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(thirdW);
        DrawCombo("##source", SourceOptions, ref _source, "Source");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(thirdW);
        DrawCombo("##sort", SortOptions, ref _sort, "Sort");

        if (submit)
            StartSearch();
    }

    private void StartSearch()
    {
        _search ??= new OnlineSearch(Plugin.OnlineLibrary.Client);
        _search.Start(new SearchFilters(_artist.Trim(), _title.Trim(), _editor.Trim(), _ensemble, _source, _sort));
    }

    // Row 3: buttons, then the search state on the same line, and the download settings on the right.
    private void DrawSearchBar(SearchSnapshot snapshot)
    {
        using (ImRaii.Disabled(!snapshot.Running))
        {
            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, "##cancel", Language.common_action_cancel))
                _search?.Cancel();
        }

        ImGui.SameLine();
        if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Sync, "##doSearch", "Search / reload"))
            StartSearch();

        if (snapshot.Running)
        {
            ImGui.SameLine();
            ImGui.TextColored(Style.Colors.Violet, "Loading...");
        }

        var hasSearched = snapshot.XivmidiProgress is not null || snapshot.BmpProgress is not null;
        if (hasSearched)
        {
            var progress = new List<string>();
            if (snapshot.XivmidiProgress is not null)
                progress.Add(FormatProgress("XIVMIDI", snapshot.XivmidiProgress));
            if (snapshot.BmpProgress is not null)
                progress.Add(FormatProgress("BMP", snapshot.BmpProgress));

            ImGui.SameLine();
            ImGui.TextDisabled(snapshot.Entries.Count == 1 ? "1 result" : $"{snapshot.Entries.Count} results");
            if (snapshot.SkippedNoUrl > 0)
                ImGuiUtil.ToolTip($"{snapshot.SkippedNoUrl} results without a download link were skipped");

            ImGui.SameLine();
            ImGui.TextDisabled($"({string.Join(" \u00b7 ", progress)})");
        }

        if (snapshot.Truncated)
        {
            ImGui.SameLine();
            ImGuiUtil.DrawTag("Partial results", Style.Colors.WarningText, Style.Colors.WarningBg);
            ImGuiUtil.ToolTip("The page limit was reached or the search was cancelled: refine your search");
        }

        DrawDownloadFolder();
    }

    // Below the search bar, one line per failed source.
    private static void DrawSourceErrors(SearchSnapshot snapshot)
    {
        DrawSourceError("XIVMIDI", snapshot.XivmidiProgress);
        DrawSourceError("BMP", snapshot.BmpProgress);
    }

    private static void DrawSourceError(string name, SourceProgress? progress)
    {
        if (string.IsNullOrWhiteSpace(progress?.Error))
            return;

        ImGui.TextColored(Style.Colors.Red, $"{name}: {progress.Error}");
    }

    // Right-aligned on the search bar: shortened download folder path, then open / change buttons.
    private void DrawDownloadFolder()
    {
        var cfg = Plugin.Config;
        var folder = string.IsNullOrWhiteSpace(cfg.OnlineLibraryDownloadFolder)
            ? Configuration.DefaultOnlineLibraryDownloadFolder
            : cfg.OnlineLibraryDownloadFolder;
        var shortFolder = folder.EllipsisPath(30);

        var style = ImGui.GetStyle();
        var openW = ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.FolderOpen).X + 2 * style.FramePadding.X;
        var changeW = ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.FolderPlus).X + 2 * style.FramePadding.X;
        var groupW = ImGui.CalcTextSize(shortFolder).X + openW + changeW + 2 * style.ItemSpacing.X;

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - groupW));

        ImGui.TextDisabled(shortFolder);
        ImGuiUtil.ToolTip($"Download folder: files added to a playlist are saved here.\n{folder}");

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenDownloadFolder", Language.common_action_open_folder))
        {
            Directory.CreateDirectory(folder);
            WindowsApi.OpenFolder(folder);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderPlus, "##BtnChangeDownloadFolder", Language.common_action_change_folder))
            _ = PickDownloadFolderAsync(folder);
    }

    private async Task PickDownloadFolderAsync(string currentFolder)
    {
        var tcs = new TaskCompletionSource<string?>();

        // The default folder only exists after the first download: create it so the dialog can start there.
        string? startPath;
        try
        {
            startPath = Directory.CreateDirectory(currentFolder).FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            startPath = Directory.Exists(Configuration.DefaultOnlineLibraryDownloadFolder) ? Configuration.DefaultOnlineLibraryDownloadFolder : null;
        }

        Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
            "Select download folder",
            (ok, path) => tcs.TrySetResult(ok && Directory.Exists(path) ? path : null),
            startPath);

        var selected = await tcs.Task;
        if (selected == null) return;

        Plugin.Config.OnlineLibraryDownloadFolder = selected;
        Plugin.IpcProvider.SyncAllSettings();
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
            if (DrawCombo("##Local Ensemble", EnsembleOptions, ref _localEnsembleFilter))
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
            EnqueueDownload(entry, DownloadTarget.Playback);

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
            EnqueueDownload(entry, DownloadTarget.Playlist);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, "##play", "Load to playback"))
        {
            EnqueueDownload(entry, DownloadTarget.Playback);
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

    // Helpers
    private static bool DrawCombo<T>(string id, (string Label, T Value)[] options, ref T value, string? name = null)
    {
        var current = value;
        var preview = options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, current)).Label ?? "";
        if (name != null)
            preview = $"{name}: {preview}";

        using var combo = ImRaii.Combo(id, preview);
        if (!combo) return false;

        var changed = false;
        foreach (var (label, optionValue) in options)
        {
            var selected = EqualityComparer<T>.Default.Equals(optionValue, value);
            if (ImGui.Selectable(label, selected) && !selected)
            {
                value = optionValue;
                changed = true;
            }
            if (selected) ImGui.SetItemDefaultFocus();
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
                !ContainsWord(entry.Arranger, word)
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

    // Requests
    private void EnqueueDownload(OnlineFileEntry entry, DownloadTarget target)
    {
        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var url))
            return;

        if (target == DownloadTarget.Playlist)
        {
            Plugin.OnlineLibrary.DownloadToPlaylist(url);
            return;
        }

        Plugin.ChatWatcher.SendDownloadSong(entry.Url);
        Plugin.OnlineLibrary.DownloadToPlayback(url);
    }
}
