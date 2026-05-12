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

    private void DrawOpenWithOptionsPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##OpenWithOptionsPopup");
        if (!popup) return;

        ImGui.Text("Open With Options");
        ImGui.Separator();
        ImGui.Spacing();

        DrawImportNormalizationOptions();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Open File...##openWithOptions"))
        {
            var options = BuildImportOptions();
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

        if (_sourceImportClosePopup)
        {
            _sourceImportClosePopup = false;
            ImGui.CloseCurrentPopup();
            return;
        }

        ImGui.Text("Import From URL");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(_sourceImportInProgress))
        {
            ImGui.SetNextItemWidth(520f * ImGuiHelpers.GlobalScale);
            ImGui.InputText("URL##sourceImportUrl", ref _sourceImportUrl, 2048);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Supports direct MIDI/tab URLs and best-effort MuseScore score URLs.");

            ImGui.Spacing();
            DrawImportNormalizationOptions();
        }

        if (!string.IsNullOrWhiteSpace(_sourceImportError))
        {
            ImGui.Spacing();
            using var color = ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Red);
            ImGui.TextWrapped(_sourceImportError);
        }

        if (_sourceImportInProgress)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(_sourceImportStatus) ? "Importing..." : _sourceImportStatus);
            if (ImGuiUtil.DangerButton("Cancel##cancelUrlImport"))
                CancelSourceImport();
        }
        else
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGuiUtil.SuccessButton("Import##importUrl"))
                StartSourceImport(new MidiForgeSourceImportRequest(_sourceImportUrl, BuildImportOptions()));

            ImGui.SameLine();

            if (ImGuiUtil.DangerButton("Cancel##cancelImportUrl"))
                ImGui.CloseCurrentPopup();
        }
    }

    private void DrawImportNormalizationOptions()
    {
        ImGui.Checkbox("Split tracks by channel##importSplitByChannel", ref _importSplitTracksByChannel);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Creates separate tracks for source tracks that contain multiple MIDI channels.");

        ImGui.Checkbox("Sort tracks##importSortTracks", ref _importSortTracks);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Moves conductor tracks first, then melody/vocal tracks, other instruments, and drum tracks.");

        ImGui.Checkbox("Overwrite track names##importOverwriteNames", ref _importOverwriteTrackNames);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Replaces existing performance-track names with inferred MIDI program or drum names.");

        ImGui.Checkbox("Remove MIDI metadata##importRemoveMetadata", ref _importRemoveMetadata);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Removes nonessential text, lyric, copyright, marker, cue point, device name, and sequence number events.");

        ImGui.Checkbox("Remove sequencer-specific events##importRemoveSequencerSpecific", ref _importRemoveSequencerSpecificEvents);

        ImGui.Checkbox("Optimize track channels##importOptimizeChannels", ref _importOptimizeChannels);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Assigns non-drum performance tracks to compact MIDI channels while preserving shared program channels.");

        ImGui.Spacing();
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        ImGui.Combo("Trim start##importTrimStart", ref _importTrimStartModeIndex, ImportTrimStartLabels, ImportTrimStartLabels.Length);
    }

    private MidiForgeImportOptions BuildImportOptions()
    {
        var trimMode = _importTrimStartModeIndex switch
        {
            1 => MidiForgeTrimStartMode.UntilFirstNote,
            2 => MidiForgeTrimStartMode.EmptyBars,
            _ => MidiForgeTrimStartMode.Off,
        };

        return new MidiForgeImportOptions(
            _importSplitTracksByChannel,
            _importSortTracks,
            _importOverwriteTrackNames,
            _importRemoveMetadata,
            _importRemoveSequencerSpecificEvents,
            _importOptimizeChannels,
            trimMode);
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
            RemoveMetadata: true,
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
            || result.RemovedMetadataEvents > 0
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
        if (result.RemovedMetadataEvents > 0)
            changes.Add($"removed {result.RemovedMetadataEvents} metadata event(s)");
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
        if (_sourceImportInProgress)
            return;

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            _sourceImportError = "Import source is empty.";
            return;
        }

        _sourceImportCancellation?.Dispose();
        _sourceImportCancellation = new CancellationTokenSource();
        _sourceImportInProgress = true;
        _sourceImportError = string.Empty;
        _sourceImportStatus = "Importing...";

        var token = _sourceImportCancellation.Token;
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
                    _sourceImportInProgress = false;
                    _sourceImportStatus = string.Empty;
                    _sourceImportError = "Import canceled.";
                });
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "[MidiEditor] Source import failed");
                await DalamudApi.Framework.RunOnFrameworkThread(() =>
                {
                    _sourceImportInProgress = false;
                    _sourceImportStatus = string.Empty;
                    _sourceImportError = $"Import failed: {e.Message}";
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

        _sourceImportInProgress = false;
        _sourceImportStatus = string.Empty;
        _sourceImportError = string.Empty;
        _sourceImportClosePopup = true;
    }

    private void CancelSourceImport()
    {
        _sourceImportCancellation?.Cancel();
        _sourceImportStatus = "Canceling...";
    }
}
