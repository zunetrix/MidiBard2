using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Playlist;
using MidiBard.Resources;

namespace MidiBard;

public class TagsWindow : Window
{
    private Plugin Plugin { get; }

    // UI State
    private List<Tag> _tags = new();
    private Tag? _selectedTag;

    // Edit state
    private string _newTagName = string.Empty;
    private string _editTagName = string.Empty;

    // Search
    private readonly List<int> _searchIndexes = new();
    private string _search = string.Empty;

    private bool _isLoading;
    private bool _openEditPopup;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    public TagsWindow(Plugin plugin) : base($"{Plugin.Name} {Language.TagsTitle}###TagsWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(600, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _ = LoadTagsAsync();
    }

    public override void PreDraw()
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(350, 300),
        };
        base.PreDraw();
    }

    private async Task LoadTagsAsync()
    {
        _isLoading = true;
        try
        {
            _tags = await ServiceContainer.TagService.GetAllAsync();
            _searchIndexes.Clear();
            _searchIndexes.AddRange(Enumerable.Range(0, _tags.Count));
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Search()
    {
        _searchIndexes.Clear();

        if (string.IsNullOrWhiteSpace(_search))
        {
            _searchIndexes.AddRange(Enumerable.Range(0, _tags.Count));
            return;
        }

        _searchIndexes.AddRange(
            _tags
                .Select((tag, index) => new { tag, index })
                .Where(x => x.tag.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index)
        );
    }

    public override void Draw()
    {
        if (_isLoading)
        {
            ImGuiUtil.DrawColoredBanner("Loading...", Style.Colors.Violet);
            return;
        }

        // Fixed header at top
        ImGui.BeginGroup();
        DrawHeader();
        ImGui.EndGroup();

        // Scrollable content area
        ImGui.BeginChild("##TagsScrollableContent", ImGuiHelpers.ScaledVector2(-1, 0), false);
        DrawTagsTable();
        ImGui.EndChild();

        DrawEditTagPopup();
    }

    private void DrawHeader()
    {
        _messageDisplay.Draw();

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonBlueActive))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##NewTagBtn", "New Tag"))
            {
                ImGui.OpenPopup("##NewTagPopup");
            }
        }

        ImGui.SameLine();
        if (ImGui.InputTextWithHint("##TagsSearchInput", Language.SearchInputLabel, ref _search, 200))
        {
            Search();
        }
        ImGui.Separator();
        DrawNewTagPopup();
        ImGuiHelpers.ScaledDummy(0, 5);
    }

    private void DrawNewTagPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("##NewTagPopup");
        if (!popUp) return;

        ImGui.Text("New Tag");
        ImGui.InputTextWithHint("##NewTagNameInput", "Tag Name", ref _newTagName, 100);

        if (ImGui.Button("Create##createTagPopup"))
        {
            if (!string.IsNullOrWhiteSpace(_newTagName))
            {
                _ = CreateTagAsync(_newTagName);
                _newTagName = string.Empty;
            }
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##cancelCreateTagPopup"))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawTagsTable()
    {
        if (!ImGui.BeginTable("##TagsTable", 3,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV))
            return;

        ImGui.TableSetupColumn("##col_num", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var clipper = new ImGuiListClipper();
        clipper.Begin(_searchIndexes.Count);

        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= _searchIndexes.Count) break;
                var tagIndex = _searchIndexes[i];
                if (tagIndex >= _tags.Count) continue;
                DrawTagRow(i, _tags[tagIndex], tagIndex);
            }
        }

        clipper.End();
        ImGui.EndTable();

        ImGuiHelpers.ScaledDummy(0, 10);
    }

    private void DrawTagRow(int displayIndex, Tag tag, int tagIndex)
    {
        ImGui.PushID($"##Tag_{tagIndex}");
        ImGui.TableNextRow();

        // # column
        ImGui.TableNextColumn();
        ImGui.Text($"{displayIndex + 1:000}");

        // Actions column
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteTagBtn_{tagIndex}", Language.ConfirmInstructionTooltip))
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                _ = DeleteTagAsync(tag.Id);
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditTagBtn_{tagIndex}", "Edit"))
        {
            _selectedTag = tag;
            _editTagName = tag.Name ?? string.Empty;
            _openEditPopup = true;
        }

        // Name column
        ImGui.TableNextColumn();
        ImGui.Selectable(tag.Name, false);

        ImGui.PopID();
    }

    private void DrawEditTagPopup()
    {
        if (_openEditPopup)
        {
            ImGui.OpenPopup("##EditTagPopup");
            _openEditPopup = false;
        }

        ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(300, 0));
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);

        using var popUp = ImRaii.Popup("##EditTagPopup");
        if (!popUp) return;

        ImGui.Text("Edit Tag");
        ImGui.Separator();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint("##EditTagNameInput", "Tag Name", ref _editTagName, 200);

        if (ImGui.Button("Save") || ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            if (_selectedTag != null && !string.IsNullOrWhiteSpace(_editTagName))
            {
                _selectedTag.Name = _editTagName;
                _ = SaveTagAsync();
            }
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private async Task CreateTagAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var existingTag = _tags.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existingTag != null)
        {
            _messageDisplay.Show($"The {name} tag is already registered");
            return;
        }

        await ServiceContainer.TagService.CreateAsync(name);
        await LoadTagsAsync();
        NotifyOpenWindows();
    }

    private async Task SaveTagAsync()
    {
        if (_selectedTag == null) return;
        await ServiceContainer.TagService.UpdateAsync(_selectedTag);
        await LoadTagsAsync();
        NotifyOpenWindows();
    }

    private async Task DeleteTagAsync(int tagId)
    {
        await ServiceContainer.TagService.DeleteAsync(tagId);
        _selectedTag = null;
        await LoadTagsAsync();
        NotifyOpenWindows();
    }

    private void NotifyOpenWindows()
    {
        Plugin.Ui.RefreshOpenWindows();
        if (Plugin.Ui.SongEditWindow.IsOpen)
            _ = Plugin.Ui.SongEditWindow.RefreshTagsAsync();
        if (Plugin.Ui.PlaylistSongEditWindow.IsOpen)
            _ = Plugin.Ui.PlaylistSongEditWindow.RefreshTagsAsync();
    }
}
