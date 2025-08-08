using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using BardMusicPlayer.XIVMIDI;
using BardMusicPlayer.XIVMIDI.IO;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

using MidiBard.Control.MidiControl;
using MidiBard.IPC;
using MidiBard.Managers.Ipc;

namespace MidiBard;

public record BMLEntry
{
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Editor { get; set; } = "";
    public string Filename { get; set; } = "";
}

enum BMLDownload
{
    Playback,
    ToPlaylist
}

public partial class PluginUI
{
    private bool showBMLWindow = false;
    private List<BMLEntry> _bmlsonglist = new List<BMLEntry>();
    private List<BMLEntry> _bmlcachedsonglist = new List<BMLEntry>();
    private BMLDownload _downloadType = BMLDownload.Playback;

    private static string BMLDownloadUrl { get; } = "https://xivmidi.com";
    private string bmlSearchString = "";
    public void ToggleBMLWindow()
    {
        if (showBMLWindow)
            CloseBMLWindow();
        else
            OpenBMLWindow();
    }

    public void OpenBMLWindow()
    {
        showBMLWindow = true;
    }

    public void CloseBMLWindow()
    {
        showBMLWindow = false;
    }

    private void DrawBMLWindow()
    {
        if (!showBMLWindow) return;

        ImGui.SetNextWindowSize(new(ImGui.GetWindowSize().Y), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() - new Vector2(2, 0), ImGuiCond.FirstUseEver, new Vector2(1, 0));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Style.Components.WindowBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Style.Components.WindowBg);
        if (ImGui.Begin("BML Browser", ref showBMLWindow))
        {
            DrawContent();
        }

        ImGui.PopStyleColor(2);
        ImGui.End();

        void DrawContent()
        {
            DrawBMLSearch();
            ImGui.NewLine();
            DrawBMLTable();

        }
    }

    public string bmlpresearch = "";
    private int bmlPerfSize = 0;
    private static readonly List<string> bmlPerfSizeData = new List<string>() { "None", "Solo", "Duet", "Trio", "Quartet", "Quintet", "Sextet", "Septet", "Octet" };

    private void DrawBMLSearch()
    {
        Vector4? color = MidiBard.config.SearchUseRegex ? MidiBard.config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Recycle, "getList", "Load Song-List", color))
        {
            SendRequest();
        }

        ImGui.SameLine();

        if (ImGui.InputTextWithHint("##searchplaylist", "Type to search", ref bmlSearchString, 255, ImGuiInputTextFlags.AutoSelectAll))
        {
            if (bmlSearchString == "" || (bmlpresearch.Length > bmlSearchString.Length))
            {
                _bmlsonglist = new List<BMLEntry>(_bmlcachedsonglist);
                searchBMLList();
            }
            else
                searchBMLList();

            bmlpresearch = bmlSearchString;
        }
        ImGuiUtil.HelpMarker("Advance search:\n t: search by title\n a: search by artist\n e: serach by editor");

        ImGui.NewLine();
        ImGui.Text("Perfomer size");
        if (ImGui.BeginCombo("##combo", bmlPerfSizeData[bmlPerfSize]))
        {
            for (int n = 0; n < bmlPerfSizeData.Count; n++)
            {
                bool is_selected = (bmlPerfSize == n);
                if (ImGui.Selectable(bmlPerfSizeData[n], is_selected))
                    bmlPerfSize = n;
                if (is_selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

    }

    private void searchBMLList()
    {
        string serachstring = bmlSearchString.ToLower();
        if (serachstring.StartsWith("t:"))
            _bmlsonglist = _bmlsonglist.Where(x => x.Title.ToLower().Contains(serachstring.Replace("t:", ""))).ToList();
        else if (serachstring.StartsWith("a:"))
            _bmlsonglist = _bmlsonglist.Where(x => x.Artist.ToLower().Contains(serachstring.Replace("a:", ""))).ToList();
        else if (serachstring.StartsWith("e:"))
            _bmlsonglist = _bmlsonglist.Where(x => x.Editor.ToLower().Contains(serachstring.Replace("e:", ""))).ToList();
        else
            _bmlsonglist = _bmlsonglist.Where(x => x.Filename.ToLower().Contains(serachstring)).ToList();
    }

    private void DrawBMLTable()
    {
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV;
        if (ImGui.BeginTable($"##BMLTableHead", 4, tableFlags))
        {
            ImGui.TableSetupColumn("       ", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Editor", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableHeadersRow();

            ImGui.EndTable();
        }

        bool beginChild;
        beginChild = ImGui.BeginChild("bmlchild");
        if (beginChild)
        {
            if (ImGui.BeginTable("##BMLTable", 4, tableFlags, ImGui.GetWindowSize()))
            {
                ImGui.TableSetupColumn("##songNumberColumn", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("##artistColumn", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##titleColumn", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##editorColumn", ImGuiTableColumnFlags.WidthStretch);

                ImGuiListClipperPtr clipper;
                unsafe
                {
                    clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper());
                }

                clipper.Begin(_bmlsonglist.Count());

                while (clipper.Step())
                {
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        if (i >= _bmlsonglist.Count()) break;
                        ImGui.PushID(i);

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();

                        ImGui.Text($"({i + 1})");
                        ImGui.TableNextColumn();

                        ImGui.Selectable(_bmlsonglist.ElementAt(i).Artist, false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.AllowItemOverlap);
                        if (ImGui.IsItemHovered())
                        {
                            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            {
                                PartyChatCommand.SendDownloadSong(BMLDownloadUrl + Uri.EscapeUriString(_bmlsonglist.ElementAt(i).Filename));
                                XIVMIDI.Instance.AddToQueue(new GetRequest()
                                {
                                    Url = BMLDownloadUrl + Uri.EscapeUriString(_bmlsonglist.ElementAt(i).Filename),
                                    Host = "xivmidi.com",
                                    Accept = "audio/midi",
                                    Requester = Requester.DOWNLOAD
                                });
                            }
                        }
                        ImGui.TableNextColumn();

                        DrawBMLlistContextMenu(i);

                        ImGui.Text(_bmlsonglist.ElementAt(i).Title);
                        ImGui.TableNextColumn();

                        ImGui.Text(_bmlsonglist.ElementAt(i).Editor);

                        ImGui.PopID();
                    }
                }

                clipper.End();
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();


        void DrawBMLlistContextMenu(int i)
        {
            ImGui.OpenPopupOnItemClick($"##bmllistRightClickMenu", ImGuiPopupFlags.MouseButtonRight);
            ImGui.PushStyleColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1);

            if (ImGui.BeginPopup($"##bmllistRightClickMenu"))
            {
                // menu title
                ImGui.PushStyleColor(ImGuiCol.Button, Style.Components.ButtonInfoNormal);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Style.Components.ButtonInfoNormal);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Style.Components.ButtonInfoNormal);
                float fullWidth = ImGui.GetContentRegionAvail().X;
                ImGui.Button($"({i + 1}) {_bmlsonglist.ElementAt(i).Artist} - {_bmlsonglist.ElementAt(i).Title}", new Vector2(fullWidth, 0));
                ImGui.PopStyleColor(3);
                ImGui.Separator();
                if (ImGui.Button("Add to playlist"))
                {
                    this._downloadType = BMLDownload.ToPlaylist;
                    XIVMIDI.Instance.AddToQueue(new GetRequest()
                    {
                        Url = BMLDownloadUrl + Uri.EscapeUriString(_bmlsonglist.ElementAt(i).Filename),
                        Host = "xivmidi.com",
                        Accept = "audio/midi",
                        Requester = Requester.DOWNLOAD
                    });
                }
                ImGui.EndPopup();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }
    }



    private void SendRequest()
    {
        XIVMIDI.Instance.AddToQueue(new GetRequest()
        {
            Url = new RequestBuilder() { bandSize = bmlPerfSize }.BuildRequest(),
            Host = "xivmidi.com",
            Requester = Requester.JSON
        });
    }

    public void Instance_RequestFinished(object sender, object e)
    {
        if (e == null)
            return;

        if (e is GetRequest)
        {
            _bmlsonglist.Add(new BMLEntry() { Artist = "Service not available." });
        }

        if (e is ResponseContainer.ApiResponse)
        {
            var data = e as ResponseContainer.ApiResponse;
            _bmlsonglist = new List<BMLEntry>();
            foreach (var file in data.data.files)
            {
                try
                {
                    if (file.websiteFilePath == null)
                        continue;
                    _bmlsonglist.Add(new BMLEntry()
                    {
                        Artist = file.artist,
                        Title = file.title,
                        Editor = file.editor,
                        Filename = file.websiteFilePath
                    });
                }
                catch { }
            }
            _bmlcachedsonglist = new List<BMLEntry>(_bmlsonglist);
        }
        else if (e is ResponseContainer.MidiFile)
        {
            if (_downloadType == BMLDownload.ToPlaylist)
            {
                _downloadType = BMLDownload.Playback;
                var data = e as ResponseContainer.MidiFile;

                if (PlaylistManager.FilePathList.Count() > 0)
                {
                    string path = Path.GetDirectoryName(PlaylistManager.FilePathList.First().FilePath);
                    File.WriteAllBytes(path + "/" + data.Filename, data.data);
                    _ = PlaylistManager.AddAsync(new List<string> { path + "/" + data.Filename }.AsEnumerable());
                }
                else
                {
                    fileDialogManager.OpenFolderDialog("Open folder", (result, folderPath) =>
                    {
                        if (result && Directory.Exists(folderPath))
                        {
                            File.WriteAllBytes(folderPath + "/" + data.Filename, data.data);
                            _ = PlaylistManager.AddAsync(new List<string> { folderPath + "/" + data.Filename }.AsEnumerable());
                        }
                    });
                }
            }
            else
            {
                var data = e as ResponseContainer.MidiFile;
                if (api.PartyList.IsPartyLeader())
                    IPCHandles.SendDownloadedSong(data.Filename, data.data);
                _ = FilePlayback.LoadPlayback(data.Filename, new MemoryStream(data.data));
            }
        }
    }
}
