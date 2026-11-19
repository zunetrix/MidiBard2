using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

namespace MidiBard;

public class BardMusicLibraryWindow : ThemedWindow
{
    private Plugin Plugin { get; }

    // Song list
    private readonly List<BMLEntry> _songList = new();
    private SearchResult? _pendingResult;

    // Filter state
    private string _title = "";
    private string _artist = "";
    private string _editor = "";
    private int _ensemble = 0;
    private int _source = 0;
    private int _sort = 0;
    private int _page = 1;
    private int _totalPages;
    private int? _totalResults;
    private int _pageSize;
    private SearchFilters? _activeFilters;

    // Combo labels
    private static readonly string[] EnsembleLabels =
        { "Any", "Solo", "Duo", "Trio", "Quartet", "Quintet", "Sextet", "Septet", "Octet" };

    // private static readonly string[] SourceLabels =
    //     { "All sources", "xivmidi.com", "bardmusicplayer.com" };

    private static readonly string[] SourceLabels =
        { "xivmidi.com", "bardmusicplayer.com" };

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
        var pending = Interlocked.Exchange(ref _pendingResult, null);

        if (pending != null)
        {
            _songList.Clear();
            _songList.AddRange(pending.Songs);
            _totalPages = pending.TotalPages;
            _totalResults = pending.TotalResults;
            _pageSize = pending.PageSize;
        }

        DrawFilters();
        DrawPageControls(currSnap);

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
                ImGui.SetNextItemWidth(-1);
                DrawCombo("##ensemble", EnsembleLabels, ref _ensemble);

                ImGui.TableNextColumn();
                ImGui.TextDisabled("Source");
                ImGui.SetNextItemWidth(-1);
                DrawCombo("##source", SourceLabels, ref _source);

                ImGui.TableNextColumn();
                ImGui.TextDisabled("Sort");
                ImGui.SetNextItemWidth(-1);
                DrawCombo("##sort", SortLabels, ref _sort);

            }
        }
    }

    //  Page controls
    private void DrawPageControls(SearchSnapshot snapshot)
    {
        bool prevDisabled = _page <= 1;

        using (ImRaii.Disabled(prevDisabled))
        {
            if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.ChevronLeft, "##prev", "Previous page"))
            {
                _page = Math.Max(1, _page - 1);
                SendRequest();
            }
        }

        ImGui.SameLine();
        ImGui.Text(_totalPages == 0 ? $"Page _ / _" : $"Page {_page} / {_totalPages}");

        ImGui.SameLine();
        using (ImRaii.Disabled(_page >= _totalPages))
        {
            if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.ChevronRight, "##next", "Next page"))
            {
                _page ++;
            SendRequest();
        }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!snapshot.Running))
        {
            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, "##cancel", Language.common_action_cancel))
            {
                XIVMidiApi.Instance.CancelDownloads();
            }
        }
        ImGui.SameLine();

        if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Sync, "##doSearch", "Search / reload"))
        {
            _page = 1;
            _activeFilters = new SearchFilters(_artist, _title, _editor, _ensemble, _source, _sort);
            Plugin.OnlineSearch.Start(_activeFilters);
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
            ImGui.Spacing();
        }

    }

    //  Table
    private void DrawTable(SearchSnapshot snapshot)
    {
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                    ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                    ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;

        const int Cols = 7;


        using var body = ImRaii.Table("##bodyTbl", Cols, flags, new Vector2(0,0));
        if (!body) return;

        SetupColumns();
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        ImGuiListClipperPtr clipper;
        unsafe { clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper()); }
        clipper.Begin(snapshot.Entries.Count);

        while (clipper.Step())
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= snapshot.Entries.Count) break;
                DrawRow(i, snapshot);
            }

        clipper.End();
        clipper.Destroy();
    }

    private static void SetupColumns()
    {
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 36f);
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Arranger", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Ensemble", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 70f);
        ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 52f);
        ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 100f);
    }

    private void DrawRow(int i, SearchSnapshot snapshot)
    {
        var e = snapshot.Entries[i];
        using var id = ImRaii.PushId(i);

        ImGui.TableNextRow();

        // #
        ImGui.TableNextColumn();
        ImGui.TextDisabled($"{i + 1:0000}");

        // Artist - double-click → playback
        ImGui.TableNextColumn();
        if (ImGui.Selectable(e.Artist, false, ImGuiSelectableFlags.AllowDoubleClick)
            && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            EnqueueDownload(e, BMLDownload.Playback);

        // Title
        ImGui.TableNextColumn();
        ImGui.Text(e.Title);


        // Arranger
        ImGui.TableNextColumn();
        ImGui.Text(e.Arranger);

        // Ensemble
        ImGui.TableNextColumn();
        ImGui.Text(e.EnsembleText);

        // Duration
        ImGui.TableNextColumn();
        ImGui.TextDisabled(e.DurationText);

        // Options
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "##url", "Open in browser"))
        {
            WindowsApi.OpenUrl($"https://bardmusicplayer.com/midis#midi={e.Id}");
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Download, "##dl", "Add to playlist"))
        {
            EnqueueDownload(e, BMLDownload.ToPlaylist);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, "##play", "Load to playback"))
        {
            EnqueueDownload(e, BMLDownload.Playback);
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

    //  Requests

    private void SendRequest()
    {
        var f = _activeFilters;
        if (f is null)
            return;

        const int limit = 100; // from RequestBuilder.BuildRequest   var request = ApiBaseUrl + "?limit=100";

        _songList.Clear();

        if (f.Source == 0)
        {
            var request = new XIVMIDIRequestBuilder
            {
                Artist = f.Artist,
                Title = f.Title,
                Credit = f.Editor,
                bandSize = f.Ensemble,
                skip = (_page - 1) * limit,
                Sort = (SortOrder)f.Sort
            };

            XIVMidiApi.Instance.GetSonglist(request);
        }
        else
        {
            var request = new BMPAPIRequestBuilder
            {
                Search = (f.Artist + " " + f.Title).Trim(),
                Editor = f.Editor,
                bandSize = f.Ensemble,
                page = _page,
                Sort = (SortOrder)f.Sort
            };

            XIVMidiApi.Instance.GetSonglist(request);
        }
    }

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
        var list = new List<BMLEntry> { new BMLEntry
                {
                    Artist = $"Error {e.ErrorCode}: {e.Message}"
                }};
        _pendingResult = new SearchResult(list, 0, 0, null);
    }


    public void OnMidiFile(object sender, XIVMidiFileEvent e)
    {
        var type = e.Arguments is BMLDownload t ? t : BMLDownload.Playback;
        HandleMidiDownload(e.MidiData, type);
    }
}

//  Model
public record BMLEntry
{
    public string Id { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Arranger { get; set; } = "";
    public string EnsembleSize { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Url { get; set; } = "";
    public string Filename { get; set; } = "";
    public EntrySource Source { get; set; }
}

public record SearchResult(List<BMLEntry> Songs, int TotalPages, int PageSize, int? TotalResults);
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
