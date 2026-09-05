using System;
using System.IO;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

public sealed class StreamSupportWidget : Widget
{
    public override string Title => Language.setting_stream_title;
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Stream;

    public StreamSupportWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var cfg = Context.Plugin.Config;

        if (ImGui.Checkbox(Language.setting_pref_stream_wite_song_name_to_file, ref cfg.EnableNowPlayingFileOutput))
            Context.Plugin.IpcProvider.SyncAllSettings();

        ImGui.Text(Language.common_output_folder_label);
        var folder = Path.GetDirectoryName(cfg.NowPlayingFilePath) ?? "";
        using (ImRaii.Disabled())
            ImGui.InputText("##NowPlayingFolderPath", ref folder, 512, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Folder, "##BtnPickNowPlayingFolder", Language.common_action_change_folder))
            _ = PickFolderAsync();

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenNowPlayingFolder", Language.common_action_open_folder))
            WindowsApi.OpenFolder(Path.GetDirectoryName(cfg.NowPlayingFilePath));

        ImGui.Text(Language.common_file_name_label);
        var fileName = Path.GetFileName(cfg.NowPlayingFilePath);
        ImGui.InputText("##NowPlayingFileName", ref fileName, 256);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            var valid = string.IsNullOrWhiteSpace(fileName)
                ? "midibard-now-playing.txt"
                : !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                    ? fileName.TrimEnd('.') + ".txt"
                    : fileName;
            cfg.NowPlayingFilePath = Path.Combine(folder, valid);
            Context.Plugin.IpcProvider.SyncAllSettings();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.File, "##BtnOpenNowPlayingFile", Language.common_action_open_file))
            WindowsApi.OpenFile(cfg.NowPlayingFilePath);

        ImGui.Separator();
        ImGui.TextUnformatted("Remote Control");

        var remoteEnabled = cfg.RemoteControlEnabled;
        if (ImGui.Checkbox("Enable on this client##RemoteControlEnabled", ref remoteEnabled))
        {
            cfg.RemoteControlEnabled = remoteEnabled;
            Context.Plugin.RefreshRemoteControlServer();
        }

        var remotePort = cfg.RemoteControlPort;
        ImGui.InputInt("Port##RemoteControlPort", ref remotePort);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            cfg.RemoteControlPort = Math.Clamp(remotePort, 1, 65535);
            Context.Plugin.SaveConfig();
            Context.Plugin.RefreshRemoteControlServer();
        }

        ImGui.TextUnformatted($"Status: {Context.Plugin.RemoteControlStatus}");

        var token = cfg.RemoteControlToken;
        using (ImRaii.Disabled())
            ImGui.InputText("Token##RemoteControlToken", ref token, 256, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(cfg.RemoteControlToken)))
        {
            if (ImGui.Button("Copy##RemoteControlTokenCopy"))
                ImGui.SetClipboardText(cfg.RemoteControlToken);
        }

        ImGui.SameLine();
        if (ImGui.Button("Regenerate##RemoteControlTokenRegenerate"))
            Context.Plugin.RegenerateRemoteControlToken();

        if (cfg.RemoteControlEnabled)
        {
            var controllerUrl = $"http://localhost:{cfg.RemoteControlPort}/";
            var controllerAccessUrl = string.IsNullOrWhiteSpace(cfg.RemoteControlToken)
                ? controllerUrl
                : controllerUrl + "#token=" + Uri.EscapeDataString(cfg.RemoteControlToken);
            var docsUrl = controllerUrl + "docs/";

            ImGui.TextUnformatted($"Controller: {controllerUrl}");
            ImGui.SameLine();
            if (ImGui.Button("Copy URL##RemoteControlControllerUrlCopy"))
                ImGui.SetClipboardText(controllerUrl);

            ImGui.SameLine();
            if (ImGui.Button("Open##RemoteControlControllerUrlOpen"))
                WindowsApi.OpenUrl(controllerAccessUrl);

            ImGui.SameLine();
            using (ImRaii.Disabled(string.IsNullOrWhiteSpace(cfg.RemoteControlToken)))
            {
                if (ImGui.Button("Copy Access URL##RemoteControlControllerAccessUrlCopy"))
                    ImGui.SetClipboardText(controllerAccessUrl);
            }

            ImGui.TextUnformatted($"API docs: {docsUrl}");
            ImGui.SameLine();
            if (ImGui.Button("Copy URL##RemoteControlDocsUrlCopy"))
                ImGui.SetClipboardText(docsUrl);
        }
    }

    private async Task PickFolderAsync()
    {
        var tcs = new TaskCompletionSource<string?>();
        var currentFolder = Path.GetDirectoryName(Context.Plugin.Config.NowPlayingFilePath) ?? "";

        Context.Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
            "Select Now Playing Output Folder",
            (result, path) => tcs.TrySetResult(result && Directory.Exists(path) ? path : null),
            currentFolder);

        var selected = await tcs.Task;
        if (selected == null) return;

        var currentFileName = Path.GetFileName(Context.Plugin.Config.NowPlayingFilePath);
        Context.Plugin.Config.NowPlayingFilePath = Path.Combine(selected, currentFileName);
        Context.Plugin.IpcProvider.SyncAllSettings();
    }
}
