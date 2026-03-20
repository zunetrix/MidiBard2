using System;
using System.IO;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Util;

namespace MidiBard;

public sealed class ObsSupportWidget : Widget
{
    public override string Title => "Obs Support";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Stream;

    public ObsSupportWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var cfg = Context.Plugin.Config;

        if (ImGui.Checkbox("Write Now Playing Song Name To File", ref cfg.EnableNowPlayingFileOutput))
            Context.Plugin.IpcProvider.SyncAllSettings();

        ImGui.Text("Output Folder:");
        var folder = Path.GetDirectoryName(cfg.NowPlayingFilePath) ?? "";
        using (ImRaii.Disabled())
            ImGui.InputText("##NowPlayingFolderPath", ref folder, 512, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Folder, "##BtnPickNowPlayingFolder", "Pick output folder", size: Style.Dimensions.ButtonLarge))
            _ = PickFolderAsync();

        ImGui.Text("File Name:");
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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Open Output Folder"))
            WindowsApi.OpenFolder(Path.GetDirectoryName(cfg.NowPlayingFilePath));

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();
        if (ImGui.Button("Open File"))
            WindowsApi.OpenFile(cfg.NowPlayingFilePath);
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
