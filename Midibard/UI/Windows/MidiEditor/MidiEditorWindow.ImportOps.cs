using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private const string ImportPopupStateKey = "import.options.popup";
    private const string MidiOpenDialogExtensions = ".mid,.midi,.smf,.rmid,.rmidi,.riff,.rmi,.kar,.mmsong";
    private const string MidiOpenDialogWin32Filter =
        "MIDI files (*.mid;*.midi;*.smf;*.rmid;*.rmidi;*.riff;*.rmi;*.kar;*.mmsong)|*.mid;*.midi;*.smf;*.rmid;*.rmidi;*.riff;*.rmi;*.kar;*.mmsong";
    private const string GuitarTabOpenDialogExtensions = ".gp,.gp3,.gp4,.gp5,.gpx";
    private const string GuitarTabOpenDialogWin32Filter =
        "Guitar Pro files (*.gp;*.gp3;*.gp4;*.gp5;*.gpx)|*.gp;*.gp3;*.gp4;*.gp5;*.gpx";

    private static readonly string[] ImportTrimStartLabels =
    {
        "Off",
        "Until first note",
        "Remove empty bars",
    };

    private ImportPopupState GetImportPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            ImportPopupStateKey,
            static () => new ImportPopupState());

    private void DrawOpenWithOptionsPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##OpenWithOptionsPopup");
        if (!popup) return;

        ImGui.Text("Open With Options");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.OpenWithOptions);

        DrawImportNormalizationOptions(GetImportPopupState());

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Open File...##openWithOptions"))
        {
            var options = BuildImportOptions(GetImportPopupState());
            ImGui.CloseCurrentPopup();
            OpenMidiFileWithOptionsDialog(options);
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelOpenWithOptions"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawImportFromUrlPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##ImportFromUrlPopup");
        if (!popup) return;

        var state = GetImportPopupState();

        if (state.ClosePopup)
        {
            state.ClosePopup = false;
            ImGui.CloseCurrentPopup();
            return;
        }

        ImGui.Text("Import From URL");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.ImportFromUrl);

        using (ImRaii.Disabled(state.InProgress))
        {
            ImGui.SetNextItemWidth(520f * ImGuiHelpers.GlobalScale);
            ImGui.InputText("URL##sourceImportUrl", ref state.SourceUrl, 2048);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.ImportFromUrl);

            ImGui.Spacing();
            DrawImportNormalizationOptions(state);
        }

        if (!string.IsNullOrWhiteSpace(state.Error))
        {
            ImGui.Spacing();
            using var color = ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Red);
            ImGui.TextWrapped(state.Error);
        }

        if (state.InProgress)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(state.Status) ? "Importing..." : state.Status);
            if (ImGuiUtil.DangerButton("Cancel##cancelUrlImport"))
                CancelSourceImport();
        }
        else
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGuiUtil.SuccessButton("Import##importUrl"))
                StartSourceImport(new MidiForgeSourceImportRequest(state.SourceUrl, BuildImportOptions(state)));

            ImGui.SameLine();

            if (ImGuiUtil.DangerButton("Cancel##cancelImportUrl"))
                ImGui.CloseCurrentPopup();
        }
    }

    private void DrawImportNormalizationOptions(ImportPopupState state)
    {
        ImGui.Checkbox("Split tracks by channel##importSplitByChannel", ref state.SplitTracksByChannel);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ImportSplitTracksByChannel);

        ImGui.Checkbox("Sort tracks##importSortTracks", ref state.SortTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ImportSortTracks);

        ImGui.Checkbox("Overwrite track names##importOverwriteNames", ref state.OverwriteTrackNames);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ImportOverwriteTrackNames);

        ImGui.Checkbox("Remove non-lyric metadata##importRemoveNonLyricMetadata", ref state.RemoveNonLyricMetadata);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ImportRemoveNonLyricMetadata);

        ImGui.Checkbox("Remove lyrics/text events##importRemoveLyricsText", ref state.RemoveLyricsAndText);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ImportRemoveLyricsAndText);

        ImGui.Checkbox("Remove sequencer-specific events##importRemoveSequencerSpecific", ref state.RemoveSequencerSpecificEvents);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ImportRemoveSequencerSpecificEvents);

        ImGui.Checkbox("Optimize track channels##importOptimizeChannels", ref state.OptimizeChannels);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ImportOptimizeChannels);

        ImGui.Spacing();
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        ImGui.Combo("Trim start##importTrimStart", ref state.TrimStartModeIndex, ImportTrimStartLabels, ImportTrimStartLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ImportTrimStart);
    }

    private static MidiForgeImportOptions BuildImportOptions(ImportPopupState state)
    {
        var trimMode = state.TrimStartModeIndex switch
        {
            1 => MidiForgeTrimStartMode.UntilFirstNote,
            2 => MidiForgeTrimStartMode.EmptyBars,
            _ => MidiForgeTrimStartMode.Off,
        };

        return new MidiForgeImportOptions(
            SplitTracksByChannel: state.SplitTracksByChannel,
            SortTracks: state.SortTracks,
            OverwriteTrackNames: state.OverwriteTrackNames,
            RemoveNonLyricMetadata: state.RemoveNonLyricMetadata,
            RemoveLyricsAndText: state.RemoveLyricsAndText,
            RemoveSequencerSpecificEvents: state.RemoveSequencerSpecificEvents,
            OptimizeChannels: state.OptimizeChannels,
            TrimStartMode: trimMode);
    }

    private void OpenMidiFileWithOptionsDialog(MidiForgeImportOptions options)
    {
        var initDir = _plugin.Config.lastOpenedFolderPath;

        if (_plugin.Config.useLegacyFileDialog)
        {
            Win32.FileDialogs.OpenFileDialog((result, path, paths) =>
            {
                if (result && !string.IsNullOrEmpty(path))
                    OpenFileWithOptions(path, options);
            }, initDir, multiselect: false, filter: MidiOpenDialogWin32Filter);
        }
        else
        {
            _plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
                "Open MIDI File With Options", MidiOpenDialogExtensions,
                (result, paths) =>
                {
                    if (result && paths.Count > 0)
                        OpenFileWithOptions(paths[0], options);
                },
                1, initDir);
        }
    }

    private void OpenGuitarTabDialog()
    {
        var initDir = _plugin.Config.lastOpenedFolderPath;
        var options = new MidiForgeImportOptions(
            RemoveNonLyricMetadata: true,
            RemoveSequencerSpecificEvents: true);

        if (_plugin.Config.useLegacyFileDialog)
        {
            Win32.FileDialogs.OpenFileDialog((result, path, paths) =>
            {
                if (result && !string.IsNullOrEmpty(path))
                {
                    StartSourceImport(new MidiForgeSourceImportRequest(
                        path,
                        options,
                        MidiForgeImportSourceKind.LocalGuitarTab));
                }
            }, initDir, multiselect: false, filter: GuitarTabOpenDialogWin32Filter);
        }
        else
        {
            _plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
                "Import Guitar Tab", GuitarTabOpenDialogExtensions,
                (result, paths) =>
                {
                    if (result && paths.Count > 0)
                    {
                        StartSourceImport(new MidiForgeSourceImportRequest(
                            paths[0],
                            options,
                            MidiForgeImportSourceKind.LocalGuitarTab));
                    }
                },
                1, initDir);
        }
    }

    private void OpenFileWithOptions(string path, MidiForgeImportOptions options)
    {
        if (!File.Exists(path))
        {
            DalamudApi.PluginLog.Warning($"[MidiEditorWindow] File not found: {path}");
            return;
        }

        try
        {
            var midi = ServiceContainer.MidiFileService.LoadMidiFile(path);
            if (midi == null)
            {
                DalamudApi.PluginLog.Error($"[MidiEditorWindow] Failed to load MIDI file: {path}");
                return;
            }

            var result = MidiForgeImporter.Normalize(midi, options);
            OpenLoadedMidiFile(result.MidiFile, path, ImportResultHasChanges(result));
            DalamudApi.PrintEcho(BuildImportSummary(result));
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[MidiEditorWindow] Failed to open MIDI file with options");
            DalamudApi.PrintError("Failed to open MIDI file with options. See plugin log for details.");
        }
    }

    private static bool ImportResultHasChanges(MidiForgeImportResult result)
        => result.RemovedEmptyTracks > 0
            || result.RemovedNonLyricMetadataEvents > 0
            || result.RemovedLyricTextEvents > 0
            || result.RemovedSequencerSpecificEvents > 0
            || result.SplitSourceTracks > 0
            || result.CreatedSplitTracks > 0
            || result.RenamedTracks > 0
            || result.OptimizedTracks > 0
            || result.TrimmedTicks > 0;

    private static string BuildImportSummary(MidiForgeImportResult result)
    {
        var changes = new List<string>();
        if (result.RemovedEmptyTracks > 0)
            changes.Add($"removed {result.RemovedEmptyTracks} empty track(s)");
        if (result.RemovedNonLyricMetadataEvents > 0)
            changes.Add($"removed {result.RemovedNonLyricMetadataEvents} non-lyric metadata event(s)");
        if (result.RemovedLyricTextEvents > 0)
            changes.Add($"removed {result.RemovedLyricTextEvents} lyric/text event(s)");
        if (result.RemovedSequencerSpecificEvents > 0)
            changes.Add($"removed {result.RemovedSequencerSpecificEvents} sequencer event(s)");
        if (result.CreatedSplitTracks > 0)
            changes.Add($"split {result.SplitSourceTracks} source track(s) into {result.CreatedSplitTracks} channel track(s)");
        if (result.RenamedTracks > 0)
            changes.Add($"renamed {result.RenamedTracks} track(s)");
        if (result.OptimizedTracks > 0)
            changes.Add($"optimized {result.OptimizedTracks} track channel(s)");
        if (result.TrimmedTicks > 0)
            changes.Add($"trimmed {result.TrimmedTicks} tick(s)");

        return changes.Count == 0
            ? "Opened MIDI with import options; no normalization changes were needed."
            : $"Opened MIDI with import options: {string.Join(", ", changes)}.";
    }

    private void StartSourceImport(MidiForgeSourceImportRequest request)
    {
        var state = GetImportPopupState();

        if (state.InProgress)
            return;

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            state.Error = "Import source is empty.";
            return;
        }

        state.Cancellation?.Dispose();
        state.Cancellation = new CancellationTokenSource();
        state.InProgress = true;
        state.Error = string.Empty;
        state.Status = "Importing...";

        var token = state.Cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                using var httpClient = MidiForgeSourceImporter.CreateDefaultHttpClient();
                var importer = new MidiForgeSourceImporter(ServiceContainer.MidiFileService, httpClient);
                var result = await importer.ImportAsync(request, token);

                await DalamudApi.Framework.RunOnFrameworkThread(() =>
                {
                    CompleteSourceImport(result);
                });
            }
            catch (OperationCanceledException)
            {
                await DalamudApi.Framework.RunOnFrameworkThread(() =>
                {
                    state.InProgress = false;
                    state.Status = string.Empty;
                    state.Error = "Import canceled.";
                });
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "[MidiEditor] Source import failed");
                await DalamudApi.Framework.RunOnFrameworkThread(() =>
                {
                    state.InProgress = false;
                    state.Status = string.Empty;
                    state.Error = $"Import failed: {e.Message}";
                    DalamudApi.PrintError("Import failed. See plugin log for details.");
                });
            }
        });
    }

    private void CompleteSourceImport(MidiForgeSourceImportResult result)
    {
        OpenLoadedMidiFile(result.MidiFile, result.FilePath, result.IsDirty, result.DisplayName);

        foreach (var warning in result.Warnings)
            DalamudApi.PrintEcho(warning);

        DalamudApi.PrintEcho($"Imported {result.DisplayName}. {BuildImportSummary(result.NormalizationResult)}");

        var state = GetImportPopupState();
        state.InProgress = false;
        state.Status = string.Empty;
        state.Error = string.Empty;
        state.ClosePopup = true;
    }

    private void CancelSourceImport()
    {
        var state = GetImportPopupState();
        state.Cancellation?.Cancel();
        state.Status = "Canceling...";
    }

    private sealed class ImportPopupState
    {
        public bool SplitTracksByChannel = false;
        public bool SortTracks = false;
        public bool OverwriteTrackNames = false;
        public bool RemoveNonLyricMetadata = false;
        public bool RemoveLyricsAndText = false;
        public bool RemoveSequencerSpecificEvents = false;
        public bool OptimizeChannels = false;
        public int TrimStartModeIndex = 0;
        public string SourceUrl = string.Empty;
        public bool InProgress = false;
        public bool ClosePopup = false;
        public string Status = string.Empty;
        public string Error = string.Empty;
        public CancellationTokenSource? Cancellation;

        public void ResetNormalizationDefaults()
        {
            SplitTracksByChannel = false;
            SortTracks = false;
            OverwriteTrackNames = false;
            RemoveNonLyricMetadata = true;
            RemoveLyricsAndText = false;
            RemoveSequencerSpecificEvents = true;
            OptimizeChannels = false;
            TrimStartModeIndex = 0;
        }

        public void ResetSourceImportForOpen()
        {
            SourceUrl = string.Empty;
            Error = string.Empty;
            Status = string.Empty;
            ClosePopup = false;
            ResetNormalizationDefaults();
        }
    }
}
