using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
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
    private int _selectedTagIndex = -1;

    // Edit state
    private string _editName = string.Empty;
    private string _newTagName = string.Empty;

    // Search
    private readonly List<int> _searchIndexes = new();
    private string _search = string.Empty;

    private bool _isLoading;

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

    private async Task LoadTagsAsync()
    {
        var tagRepo = Playlist.ServiceContainer.TryGet<ITagRepository>();
        if (tagRepo == null) return;

        _isLoading = true;
        try
        {
            _tags = await tagRepo.GetAllAsync();
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
            ImGui.Text("Loading...");
            return;
        }

        DrawTagTable();

        // Edit panel
        DrawEditPanel();
    }

    private void DrawTagTable()
    {
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();

        // Search bar
        if (ImGui.InputText("Search", ref _search, 200))
        {
            Search();
        }
        ImGui.Separator();

        // Create new tag section
        ImGui.Text("Create New Tag:");
        ImGui.InputText("Name##CreateTagNameInput", ref _newTagName, 200);
        ImGui.SameLine();
        if (ImGui.Button("Create##newTag"))
        {
            if (!string.IsNullOrWhiteSpace(_newTagName))
            {
                _ = CreateTagAsync(_newTagName);
            }
        }

        ImGui.Separator();

        // Table configuration
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                        ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV;
        var tableColumnCount = 3;

        if (ImGui.BeginTable("##TagsTable", tableColumnCount, tableFlags))
        {
            // Setup columns with headers
            ImGui.TableSetupColumn("##col_num", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);

            // Draw header row
            ImGui.TableHeadersRow();

            // Use clipper for performance with large lists
            var clipper = new ImGuiListClipper();
            clipper.Begin(_searchIndexes.Count, lineHeight);

            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    if (i >= _searchIndexes.Count) break;

                    var tagIndex = _searchIndexes[i];
                    if (tagIndex >= _tags.Count) continue;

                    var tag = _tags[tagIndex];
                    DrawTagRow(i, tag, tagIndex);
                }
            }

            clipper.End();
            ImGui.EndTable();
        }
    }

    private void DrawTagRow(int displayIndex, Tag tag, int tagIndex)
    {
        ImGui.PushID($"##tag_{tagIndex}");

        var isSelected = _selectedTagIndex == tagIndex;

        // Table row
        ImGui.TableNextRow();

        // # column
        ImGui.TableNextColumn();
        ImGui.Text($"{displayIndex + 1:00}");

        // Name column
        ImGui.TableNextColumn();
        if (ImGui.Selectable(tag.Name ?? "Unknown", isSelected))
        {
            _selectedTagIndex = tagIndex;
            _selectedTag = tag;
            LoadEditFields(tag);
        }

        // Actions column
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditTagBtn_{tagIndex}", "Edit"))
        {
            _selectedTagIndex = tagIndex;
            _selectedTag = tag;
            LoadEditFields(tag);
        }
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteTagBtn_{tagIndex}", "Delete"))
        {
            _ = DeleteTagAsync(tag.Id);
        }

        ImGui.PopID();
    }

    private void LoadEditFields(Tag tag)
    {
        _editName = tag.Name ?? "";
    }

    private void DrawEditPanel()
    {
        ImGui.Separator();

        // Edit existing tag section
        ImGui.Text("Edit Selected Tag:");

        if (_selectedTag != null)
        {
            ImGui.InputText("Name##EditTagNameInput", ref _editName, 200);

            ImGui.Separator();

            if (ImGui.Button("Save Changes"))
            {
                _ = SaveTagAsync();
            }

            ImGui.SameLine();

            if (ImGui.Button("Delete Tag"))
            {
                _ = DeleteTagAsync(_selectedTag.Id);
            }
        }
        else
        {
            ImGui.Text("Select a tag to edit");
        }
    }

    private async Task CreateTagAsync(string name)
    {
        var tagRepo = Playlist.ServiceContainer.TryGet<ITagRepository>();
        if (tagRepo == null || string.IsNullOrWhiteSpace(name)) return;

        await tagRepo.CreateAsync(name);
        _newTagName = "";

        await LoadTagsAsync();
    }

    private async Task SaveTagAsync()
    {
        var tagRepo = Playlist.ServiceContainer.TryGet<ITagRepository>();
        if (tagRepo == null || _selectedTag == null) return;

        _selectedTag.Name = _editName;
        await tagRepo.UpdateAsync(_selectedTag);

        await LoadTagsAsync();
    }

    private async Task DeleteTagAsync(int tagId)
    {
        var tagRepo = Playlist.ServiceContainer.TryGet<ITagRepository>();
        if (tagRepo == null) return;

        await tagRepo.DeleteAsync(tagId);

        _selectedTag = null;
        _selectedTagIndex = -1;

        await LoadTagsAsync();
    }
}
