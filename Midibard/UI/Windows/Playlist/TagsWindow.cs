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
    private string _newTagName = string.Empty;
    private string _editTagName = string.Empty;  // Temporary storage for tag name editing

    // Search
    private readonly List<int> _searchIndexes = new();
    private string _search = string.Empty;

    private bool _isLoading;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();
    private readonly ImGuiModalEditor<Tag> _tagEditorModal = new("##TagEditModal", ImGuiHelpers.ScaledVector2(400, 150));

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
        var WindowSizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(350, 300),
            // MaximumSize = ImGuiHelpers.ScaledVector2(350, float.MaxValue)
        };

        SizeConstraints = WindowSizeConstraints;

        base.PreDraw();
    }

    private async Task LoadTagsAsync()
    {
        var tagRepo = ServiceContainer.GetServiceOrNull<ITagRepository>();
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

        // Modal for editing
        _tagEditorModal.Draw();
    }

    private void DrawHeader()
    {
        // Display message if there's one
        _messageDisplay.Draw();

        // Fixed search input at top
        if (ImGui.InputTextWithHint("##TagsSearchInput", Language.SearchInputLabel, ref _search, 200))
        {
            Search();
        }
        ImGui.Separator();

        // Fixed create section
        ImGui.Text("Create New Tag:");
        ImGui.InputTextWithHint("##CreateTagNameInput", "Tag Name", ref _newTagName, 200);
        ImGui.SameLine();
        if (ImGui.Button("Create##newTag"))
        {
            if (!string.IsNullOrWhiteSpace(_newTagName))
            {
                _ = CreateTagAsync(_newTagName);
            }
        }
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 5);
    }

    private void DrawTagsTable()
    {
        var tableColumnCount = 3;

        if (ImGui.BeginTable("##TagsTable", tableColumnCount, ImGuiTableFlags.Resizable))
        {
            // Setup columns with headers
            ImGui.TableSetupColumn("##col_num", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);

            ImGui.TableSetupScrollFreeze(0, 1);

            // Draw header row
            ImGui.TableHeadersRow();

            // Use clipper for performance with large lists
            var clipper = new ImGuiListClipper();
            clipper.Begin(_searchIndexes.Count);

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

        // Table row
        ImGui.TableNextRow();

        // # column
        ImGui.TableNextColumn();
        ImGui.Text($"{displayIndex + 1:00}");

        // Name column
        ImGui.TableNextColumn();
        if (ImGui.Selectable(tag.Name ?? "Unknown", false))
        {
            // Nothing on select
        }

        // Actions column
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditTagBtn_{tagIndex}", "Edit"))
        {
            _selectedTag = tag;
            _selectedTagIndex = tagIndex;
            _editTagName = tag.Name ?? string.Empty;
            _tagEditorModal.Show(
                "Edit Tag",
                tag,
                (modal, tagData) =>
                {
                    // Custom content renderer
                    ImGui.Text("Tag Name:");
                    ImGui.InputText("##TagNameInput", ref _editTagName, 200);
                },
                (tagData) =>
                {
                    // On save callback - copy edited name back to tag
                    tagData.Name = _editTagName;
                    _ = SaveTagAsync();
                }
            );
        }
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteTagBtn_{tagIndex}", "Delete"))
        {
            _ = DeleteTagAsync(tag.Id);
        }

        ImGui.PopID();
    }

    private async Task CreateTagAsync(string name)
    {
        var tagRepo = ServiceContainer.GetServiceOrNull<ITagRepository>();
        if (tagRepo == null || string.IsNullOrWhiteSpace(name)) return;

        // Check if tag already exists
        var existingTag = _tags.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existingTag != null)
        {
            _messageDisplay.Show($"The {name} tag is already registered");
            return;
        }

        await tagRepo.CreateAsync(name);
        await LoadTagsAsync();
    }

    private async Task SaveTagAsync()
    {
        var tagRepo = ServiceContainer.GetServiceOrNull<ITagRepository>();
        if (tagRepo == null || _selectedTag == null) return;

        await tagRepo.UpdateAsync(_selectedTag);

        await LoadTagsAsync();
    }

    private async Task DeleteTagAsync(int tagId)
    {
        var tagRepo = ServiceContainer.GetServiceOrNull<ITagRepository>();
        if (tagRepo == null) return;

        await tagRepo.DeleteAsync(tagId);

        _selectedTag = null;
        _selectedTagIndex = -1;

        await LoadTagsAsync();
    }
}
