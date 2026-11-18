using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

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

namespace MidiBard;

public class BardMusicLibraryWindow : ThemedWindow
{
    private Plugin Plugin { get; }

    // Song list
    private readonly List<BMLEntry> _songList = new();
    private List<BMLEntry>? _pendingSongs;

    // Filter state
    private string _title = "";
    private string _artist = "";
    private string _editor = "";
    private int _ensemble = 0;
    private int _source = 0;
    private int _sort = 0;
    private int _page = 0;   // 0 = all (no page param); 1+ = paginated

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
        : base($"{Plugin.Name} {Language.window_settings}###BardMusicLibraryWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(640, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    //  Draw
    public override void Draw()
    {
        var pending = Interlocked.Exchange(ref _pendingSongs, null);

        if (pending != null)
        {
            _songList.Clear();
            _songList.AddRange(pending);
        }

        DrawFilters();
        DrawPageControls();

        ImGui.Spacing();

        if (XIVMidiApi.Instance.IsRequestRunning)
            ImGuiUtil.DrawColoredBanner("Loading...", Style.Colors.Violet);
        ImGui.Spacing();


        DrawTable();
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
                using (ImRaii.Disabled(true))
                {
                    DrawCombo("##sort", SortLabels, ref _sort);
                }
            }
        }
    }

    //  Page controls
    private void DrawPageControls()
    {
        bool prevDisabled = _page <= 0;

        using (ImRaii.Disabled(prevDisabled))
        {
            if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.ChevronLeft, "##prev", "Previous page"))
            {
                _page = Math.Max(0, _page - 1);
                SendRequest();
            }
        }

        ImGui.SameLine();
        ImGui.Text(_page == 0 ? "All" : $"Page {_page}");

        ImGui.SameLine();
        if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.ChevronRight, "##next", "Next page"))
        {
            _page = _page == 0 ? 1 : _page + 1;
            SendRequest();
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!XIVMidiApi.Instance.IsRequestRunning))
        {
            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, "##cancel", Language.common_action_cancel))
            {
                XIVMidiApi.Instance.CancelDownloads();
            }
        }
        ImGui.SameLine();

        if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Sync, "##doSearch", "Search / reload"))
            SendRequest();

        if (_songList.Count > 0)
        {
            ImGui.TextDisabled($"{_songList.Count} result(s){(_page > 0 ? $"  -  page {_page}" : "")}");
            ImGui.Spacing();
        }
    }

    //  Table
    private void DrawTable()
    {
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                    ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV;
        const int Cols = 7;

        // Sticky header
        using (var hdr = ImRaii.Table("##hdr", Cols, flags))
        {
            if (hdr)
            {
                SetupColumns(header: true);
                ImGui.TableHeadersRow();
            }
        }

        // Scrollable body
        using var child = ImRaii.Child("##body");
        if (!child) return;

        using var body = ImRaii.Table("##bodyTbl", Cols, flags);
        if (!body) return;

        SetupColumns(header: false);

        ImGuiListClipperPtr clipper;
        unsafe { clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper()); }
        clipper.Begin(_songList.Count);

        while (clipper.Step())
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= _songList.Count) break;
                DrawRow(i);
            }

        clipper.End();
    }

    private static void SetupColumns(bool header)
    {
        if (header)
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 36f);
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Arranger", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Ensemble", ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed, 52f);
            ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed, 100f);
        }
        else
        {
            ImGui.TableSetupColumn("##n", ImGuiTableColumnFlags.WidthFixed, 36f);
            ImGui.TableSetupColumn("##ar", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("##ti", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("##ed", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##en", ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn("##du", ImGuiTableColumnFlags.WidthFixed, 52f);
            ImGui.TableSetupColumn("##op", ImGuiTableColumnFlags.WidthFixed, 100f);
        }
    }

    private void DrawRow(int i)
    {
        var e = _songList[i];
        using var id = ImRaii.PushId(i);

        ImGui.TableNextRow();

        // #
        ImGui.TableNextColumn();
        ImGui.TextDisabled($"{i + 1:0000}");

        // Artist - double-click → playback
        ImGui.TableNextColumn();
        ImGui.Selectable(e.Artist, false);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            EnqueueDownload(e, BMLDownload.Playback);
        }

        // Title - tooltip shows notes
        ImGui.TableNextColumn();
        ImGui.Text(e.Title);
        if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(e.Notes))
            ImGui.SetTooltip(e.Notes);

        // Arranger
        ImGui.TableNextColumn();
        ImGui.Text(e.Arranger);

        // Ensemble
        ImGui.TableNextColumn();
        ImGui.Text(e.EnsembleSize);

        // Duration
        ImGui.TableNextColumn();
        ImGui.TextDisabled(e.Duration);

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
        const int limit = 100; // from RequestBuilder.BuildRequest   var request = ApiBaseUrl + "?limit=100";

        _songList.Clear();

        var effectivePage = Math.Max(1,_page);

        if (_source == 0)
        {
            var request = new XIVMIDIRequestBuilder
            {
                Artist = _artist,
                Title = _title,
                Credit = _editor,
                bandSize = _ensemble,
                skip = (effectivePage - 1) * limit
            };

            XIVMidiApi.Instance.GetSonglist(request);
        }
        else
        {
            var request = new BMPAPIRequestBuilder
            {
                Search = (_artist + " " + _title).Trim(),
                Editor = _editor,
                bandSize = _ensemble,
                page = effectivePage
            };

            XIVMidiApi.Instance.GetSonglist(request);
        }
    }

    private void EnqueueDownload(BMLEntry entry, BMLDownload type)
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
        _pendingSongs = list;
    }

    public void OnBmpSongList(object sender, XIVMidiBMPSongsEvent e)
    {
        if (e.Songs == null || e.Songs.docs == null)
        {
            _pendingSongs = new List<BMLEntry>();
            return;
        }

        _pendingSongs = e.Songs.docs
            .Where(f => !string.IsNullOrWhiteSpace(f.url))
            .Select(f => new BMLEntry
            {
                Id = f.id.ToString() ?? "",
                Artist = f.artist ?? "",
                Title = f.title ?? "",
                Arranger = f.arranger ?? "",
                EnsembleSize = f.ensembleSize ?? "",
                Duration = f.duration ?? "",
                Notes = f.notes ?? "",
                Url = f.url ?? "",
                Filename = f.filename ?? "",
                Source = EntrySource.Bmp
            })
            .ToList();
    }

    public void OnXivSongList(object sender, XIVMidiXIVSongsEvent e)
    {
        if (e.Songs == null || e.Songs.data == null)
        {
            _pendingSongs = new List<BMLEntry>();
            return;
        }

        _pendingSongs = e.Songs.data
            .Where(f => !string.IsNullOrWhiteSpace(f.download_url))
            .Where(f => f.bandsize > 0 && f.bandsize < 9)
            .Select(f => new BMLEntry
            {
                Id = f.id ?? "",
                Artist = f.artist ?? "",
                Title = f.title ?? "",
                Arranger = f.credit ?? "",
                EnsembleSize = EnsembleLabels[f.bandsize],
                Duration = f.duration != 0 ? $"{f.duration / 60}:{(f.duration % 60).ToString("00")}" : "",
                // TODO(lib) Notes = f.notes ?? "",
                Url = f.download_url ?? "",
                // TODO(lib) Filename = f.filename ?? "",
                Source = EntrySource.Xivmidi
            })
            .ToList();
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
