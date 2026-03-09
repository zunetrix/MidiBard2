using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Util.Lyrics;
using MidiBard.Extensions.Json;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Playlist;

using Melanchall.DryWetMidi.Interaction;
using MidiBard.Resources;

namespace MidiBard;

public class LyricsEditorWindow : Window
{
    private Plugin Plugin { get; }
    private const string LrcFileFilter = "Lyric file (*.lrc)|*.lrc";
    private string newTagName = "";
    private string newTagValue = "";
    private (int index, LyricEntry) DragDropSource { get; set; }
    private Lyrics EditingLyrics { get; set; }
    private Lyrics LyricsPending { get; set; }
    private Lyrics GetEmptyLyrics => new();
    private bool unsaved { get; set; }
    private Regex LrcTimeFormat { get; } = new(@"(?<min>\d+):(?<sec>\d{1,2})\.(?<ff>\d+)", RegexOptions.Compiled);
    private List<LyricEntry> LrcLines => EditingLyrics.LrcLines;

    public LyricsEditorWindow(Plugin plugin) : base($"{Plugin.Name} {Language.lyrics}###LyricsEditorWindow")
    {
        Plugin = plugin;
        EditingLyrics = GetEmptyLyrics;

        Size = ImGuiHelpers.ScaledVector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.None;
        // SizeCondition = ImGuiCond.Always;
        // Flags = ImGuiWindowFlags.NoResize;
    }

    public override void PreDraw()
    {
        Flags |= unsaved ? ImGuiWindowFlags.UnsavedDocument : ImGuiWindowFlags.None;

        base.PreDraw();
    }

    public override void Draw()
    {
        if (LyricsPending != null)
        {
            ImGui.OpenPopup("Save?");
        }

        var open = true;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowTitleAlign, new Vector2(0.5f));

        var wdl = ImGui.GetWindowDrawList();
        var clipRect = wdl.GetClipRectMin() + wdl.GetClipRectMax();
        clipRect /= 2;
        ImGui.SetNextWindowPos(clipRect, ImGuiCond.Appearing, Vector2.One / 2);

        if (ImGui.BeginPopupModal("Save?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGuiHelpers.ScaledDummy(20);
            ImGuiHelpers.CenteredText("Editor has unsaved changes. Save now?");
            ImGuiHelpers.ScaledDummy(20);
            ImGuiHelpers.ScaledDummy(60, 30);
            ImGui.SameLine();
            if (ImGui.Button("Save", new Vector2(ImGui.GetFrameHeight() * 4, ImGui.GetFrameHeight())))
            {
                AskSave();
                unsaved = false;
                LoadLrcToEditor(LyricsPending);
                LyricsPending = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Discard", new Vector2(ImGui.GetFrameHeight() * 4, ImGui.GetFrameHeight())))
            {
                unsaved = false;
                LoadLrcToEditor(LyricsPending);
                LyricsPending = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            ImGui.Dummy(ImGuiHelpers.ScaledVector2(60, 30));

            ImGui.EndPopup();
        }
        ImGui.PopStyleVar();

        if (!open)
        {
            LyricsPending = null;
        }

        if (ImGui.Button("New"))
        {
            var newLrc = GetLrcFromPlayback(Plugin.CurrentBardPlayback);
            LoadLrcToEditor(newLrc);
        }

        ImGui.SameLine();
        if (ImGui.Button("Open"))
        {
            Plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
            title: "Open",
            filters: LrcFileFilter,
            selectionCountMax: 1,
            callback: (result, selectedPaths) =>
            {
                if (!result || selectedPaths.Count == 0) return;
                if (!File.Exists(selectedPaths[0])) return;

                try
                {
                    LoadLrcToEditor(new Lyrics(selectedPaths[0]));
                    unsaved = false;
                }
                catch (Exception e)
                {
                    DalamudApi.PluginLog.Error(e, "error when opening lrc file");
                }
            }
        );
        }

        //ImGui.SameLine();
        //if (ImGui.Button("Load from playing"))
        //{
        //    LoadLrcToEditor(Plugin.LyricsPlayer?.JsonClone());
        //}

        ImGui.SameLine();
        if (ImGui.Button("Save"))
        {
            AskSave();
        }
        ImGuiUtil.ToolTip(EditingLyrics.FilePath is null ? "Select save location" : $"Save to: {EditingLyrics.FilePath}");

        ImGui.SameLine();
        if (ImGui.Button("Sort"))
        {
            EditingLyrics.Sort();
        }

        ImGuiUtil.ToolTip("Sort lrc lines by time");

        //SameLine();
        //if (Checkbox("AutoSort", ref autosort)) EditingLyrics.Sort();

        ImGui.SameLine();
        if (Plugin.CurrentBardPlayback.IsLoaded)
        {
            ImGui.Text($"Current line: {EditingLyrics.FindLrcIdx(Plugin.CurrentBardPlayback.GetCurrentTimeSpan())}");
        }
        else
        {
            ImGui.NewLine();
        }

        if (ImGui.CollapsingHeader("LRC Metadata", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var id = 0;
            if (ImGui.BeginTable("metadata", 3, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("tag", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch, 3);
                ImGui.TableSetupColumn("##btn", ImGuiTableColumnFlags.WidthFixed);
                var metadatas = EditingLyrics.LrcMetadata;
                foreach (var (idtag, value) in (IEnumerable<KeyValuePair<string, string>>)metadatas)
                {
                    ImGui.PushID(id++);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(idtag);
                    ImGui.TableNextColumn();
                    var editValue = value;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputText("##v", ref editValue, 128))
                    {
                        metadatas[idtag] = editValue;
                        unsaved = true;
                    }

                    ImGui.TableNextColumn();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt))
                    {
                        metadatas.Remove(idtag);
                        unsaved = true;
                    }

                    ImGui.PopID();
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##newtag", "New tag name", ref newTagName, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    AddNewMetadataLine();
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##newtagvalue", "New tag value", ref newTagValue, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    AddNewMetadataLine();
                }

                ImGui.TableNextColumn();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "add"))
                {
                    AddNewMetadataLine();
                }

                ImGui.EndTable();

                void AddNewMetadataLine()
                {
                    if (string.IsNullOrWhiteSpace(newTagName)) return;
                    if (metadatas.TryAdd(newTagName, newTagValue))
                    {
                        newTagName = "";
                        newTagValue = "";
                    }

                    unsaved = true;
                }
            }
        }

        DrawContentEditor();
    }

    private void DrawContentEditor()
    {
        ImGuiHelpers.ScaledDummy(0, 10);

        if (ImGui.BeginChild("contents", new Vector2(-1)))
        {
            ImGui.BeginGroup();
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.Y));
            if (ImGui.BeginTable("lrctable", 4, ImGuiTableFlags.Sortable))
            {
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.PreferSortAscending);
                ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort);
                ImGui.TableSetupColumn("Text", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort);
                ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort);
                ImGui.TableHeadersRow();

                var findPlayingLine = Plugin.CurrentBardPlayback.IsLoaded
                    ? EditingLyrics.FindLrcIdx(Plugin.CurrentBardPlayback.GetCurrentTimeSpan())
                    : -1;

                #region SortByTime

                var sortDirection = ImGui.TableGetSortSpecs().Specs.SortDirection;

                if (sortDirection == ImGuiSortDirection.Ascending)
                {
                    for (var i = 0; i < LrcLines.Count; i++)
                    {
                        Iteration(ref i);
                    }
                }
                else if (sortDirection == ImGuiSortDirection.Descending)
                {
                    for (var i = LrcLines.Count - 1; i >= 0; i--)
                    {
                        Iteration(ref i);
                    }
                }

                #endregion

                //if (shouldSort)
                //{
                //    EditingLyrics.Sort();
                //    DalamudApi.PluginLog.Information("do sort");
                //}

                void Iteration(ref int i)
                {
                    ImGui.PushID(i);
                    var shiftRightClicked = false;
                    try
                    {
                        var entry = LrcLines[i];
                        var entryTimeStamp = entry.TimeStamp;
                        var lrcTime = Lyrics.ToLrcTime(entryTimeStamp);
                        if (findPlayingLine == i) ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Lerp(Plugin.Config.themeColor, Style.Components.FrameBg, 0.4f));

                        ImGui.TableNextColumn();
                        ImGui.PushFont(UiBuilder.MonoFont);
                        if (ImGui.Button($"{i:000}"))
                        {
                            try
                            {
                                Plugin.CurrentBardPlayback.MoveToTime(new MetricTimeSpan(entryTimeStamp));
                            }
                            catch (Exception e)
                            {
                                DalamudApi.PluginLog.Error(e, "error moving playback time");
                            }
                        }

                        ImGui.PopFont();
                        ImGuiUtil.ToolTip($"Jump to {lrcTime}. Drag to move position");

                        if (ImGui.BeginDragDropSource())
                        {
                            DragDropSource = (i, entry.JsonClone());
                            var dragDropPayload = new ReadOnlySpan<byte>([]);
                            ImGui.SetDragDropPayload("dragdropTime", dragDropPayload, 0);
                            ImGui.PushFont(UiBuilder.MonoFont);
                            ImGui.Text($"{Lyrics.ToLrcTime(DragDropSource.Item2.TimeStamp),10} ");
                            ImGui.PopFont();
                            ImGui.SameLine();
                            ImGui.Text(DragDropSource.Item2.Text);
                            ImGui.EndDragDropSource();
                        }

                        if (ImGui.BeginDragDropTarget())
                        {
                            ImGui.AcceptDragDropPayload("dragdropTime");
                            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                            {
                                LrcLines[DragDropSource.index] = LrcLines[i];
                                LrcLines[i] = DragDropSource.Item2;
                                unsaved = true;
                            }

                            ImGui.EndDragDropTarget();
                        }

                        ImGui.TableNextColumn();


                        ImGui.PushFont(UiBuilder.MonoFont);
                        ImGui.SetNextItemWidth(ImGui.CalcTextSize("000:00.000").X + ImGui.GetStyle().FramePadding.X * 2);
                        var timeString = $"{lrcTime,10}";
                        ImGui.InputText("##timestamp", ref timeString, 10);

                        if (ImGui.IsItemDeactivatedAfterEdit())
                        {
                            if (TryParseLrcTimeSpan(timeString, out var timeSpan))
                            {
                                DalamudApi.PluginLog.Information($"{timeString}");
                                entry.TimeStamp = timeSpan;
                                unsaved = true;
                            }
                        }

                        ImGui.PopFont();
                        shiftRightClicked |= ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right);

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputText("##lyrictext", ref entry.Text, 400))
                        {
                            unsaved = true;
                        }

                        shiftRightClicked |= ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right);

                        ImGui.TableNextColumn();

                        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt))
                        {
                            LrcLines.Remove(entry);
                            unsaved = true;
                        }

                        if (shiftRightClicked)
                        {
                            try
                            {
                                LrcLines.Insert(i + 1, new LyricEntry { TimeStamp = entryTimeStamp });
                                unsaved = true;
                            }
                            catch
                            {
                                // ignored
                            }
                        }

                        if (findPlayingLine == i) ImGui.PopStyleColor();
                    }
                    catch (Exception e)
                    {
                        DalamudApi.PluginLog.Warning(e.ToString());
                    }

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            ImGuiHelpers.CenteredText("Right click to add a new line");
            ImGuiHelpers.CenteredText("Shift+Right click to insert a new line");

            ImGuiHelpers.ScaledDummy(-1, ImGui.GetWindowContentRegionMax().Y - ImGui.GetCursorPosY());

            ImGui.PopStyleVar();
            ImGui.EndGroup();

            var rightClicked = !ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right);
            if (rightClicked)
            {
                var currentPlaybackTime = Plugin.CurrentBardPlayback.GetCurrentTimeSpan();
                var newLine = new LyricEntry { TimeStamp = currentPlaybackTime };
                LrcLines.Insert(EditingLyrics.FindLrcIdx(currentPlaybackTime) + 1, newLine);
                unsaved = true;
            }

            ImGui.EndChild();
        }
    }

    internal Lyrics GetLrcFromPlayback(BardPlayback currentPlayback)
    {
        var newLrc = GetEmptyLyrics;
        if (currentPlayback.IsLoaded)
        {
            newLrc.LrcMetadata["ti"] = currentPlayback.DisplayName;
            newLrc.LrcMetadata["length"] = Lyrics.ToLrcTime(currentPlayback.GetDuration<MetricTimeSpan>());
            newLrc.FilePath = Path.ChangeExtension(currentPlayback.FilePath, "lrc");
        }

        return newLrc;
    }

    internal Lyrics GetLrcFromPlaylistSong(PlaylistSong playlistSong)
    {
        var newLrc = GetEmptyLyrics;
        if (playlistSong?.Song is null) return newLrc;

        var lrcPath = playlistSong.GetLrcPath();
        if (File.Exists(lrcPath))
        {
            return new Lyrics(lrcPath);
        }
        DalamudApi.PluginLog.Information("file not exist, create new lrc");

        var midiFileService = ServiceContainer.MidiFileService;
        newLrc.LrcMetadata["ti"] = playlistSong.GetFileName();
        newLrc.LrcMetadata["length"] = Lyrics.ToLrcTime(midiFileService?.LoadMidiFile(playlistSong.GetFilePath())?.GetDurationTimeSpan() ?? TimeSpan.Zero);
        newLrc.FilePath = Path.ChangeExtension(playlistSong.GetFilePath(), "lrc");

        return newLrc;
    }

    public void LoadLrcToEditor(Lyrics lrc)
    {
        if (lrc is null)
        {
            return;
        }

        if (unsaved)
        {
            LyricsPending = lrc;
            return;
        }

        EditingLyrics = lrc;
        unsaved = false;
    }

    private bool TryParseLrcTimeSpan(string input, out TimeSpan timeSpan)
    {
        var match = LrcTimeFormat.Match(input);
        var minutes = match.Groups["min"];
        var seconds = match.Groups["sec"];
        var ff = match.Groups["ff"];
        if (match.Success)
        {
            var fractionSecond = double.Parse($"0.{ff.Value}", CultureInfo.InvariantCulture);
            timeSpan = TimeSpan.FromMinutes(int.Parse(minutes.Value)) + TimeSpan.FromSeconds(int.Parse(seconds.Value) + fractionSecond);
            return true;
        }

        timeSpan = TimeSpan.Zero;
        return false;
    }

    private void AskSave()
    {
        var path = EditingLyrics.FilePath;
        if (path is null)
        {
            OpenExportFileDialog();
        }
        else
        {
            SaveLrc(path);
        }
    }

    private void OpenExportFileDialog(string defalutPath = null)
    {
        Plugin.Ui.FileDialogService.FileDialogManager.SaveFileDialog(
        "Save", "All files (*.*)|*.*",
        Plugin.CurrentBardPlayback?.DisplayName,
        ".lrc",
        (result, selectedPath) =>
        {
            if (!result) return;

            SaveLrc(selectedPath);

        }, defalutPath);
    }

    private void SaveLrc(string filePathToSave)
    {
        var exportString = EditingLyrics.GetLrcExportString();
        File.WriteAllText(filePathToSave, exportString, Encoding.UTF8);
        ImGuiUtil.AddNotification(NotificationType.Success, "Lrc Saved " + filePathToSave);
        unsaved = false;

        ReloadLrc();
    }

    private void ReloadLrc()
    {
        Plugin.IpcProvider.ReloadLyrics(EditingLyrics.FilePath);
    }
}
