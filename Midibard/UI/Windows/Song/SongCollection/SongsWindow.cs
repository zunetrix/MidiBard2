using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Resources;
using MidiBard.Playlist;

namespace MidiBard;

public partial class SongsWindow : Window
{
    private Plugin Plugin { get; }

    // UI State
    private List<Song> _songs = new();

    // Search
    private readonly List<int> _searchIndexes = new();
    public HashSet<int> _selectedSongIds = new();
    private bool _isGlobalSongsCheckboxChecked = false;
    private string _search = string.Empty;

    // Tag names present in the loaded songs - used to populate the tag filter combo
    private List<string> _availableTagNames = new();

    // Add selected songs to playlist
    private readonly List<Playlist.Playlist> _playlistTargets = new();
    private int _selectedPlaylistTargetIndex = 0;
    private bool _isLoadingPlaylistTargets = false;
    private bool _closeAddToPlaylistPopup = false;

    // Bulk tag selected songs
    private readonly List<Tag> _tagTargets = new();
    private string _selectedTagTargetName = string.Empty;
    private readonly ImGuiComboSearch _bulkTagCombo = new();
    private bool _isLoadingTagTargets = false;
    private bool _closeBulkTagPopup = false;
    private bool _bulkTagAdd = true;

    // Deferred popup opens (set inside menu items, flushed after EndMenuBar)
    private string? _pendingPopup;
    private void OpenPopup(string id) => _pendingPopup = id;

    private bool _isLoading;

    // Import progress
    private readonly SongImportHelper _importHelper;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    // Per-column filters
    private string _filterName = string.Empty;
    private string _filterArtist = string.Empty;
    private string _filterYear = string.Empty;
    private string _filterFilePath = string.Empty;
    private string _filterComments = string.Empty;
    private string _filterTags = string.Empty;
    private readonly ImGuiComboSearch _filterTagsCombo = new();

    // Sort state
    private SongSortColumn? _sortCol = null;
    private bool _sortAsc = true;

    public SongsWindow(Plugin plugin) : base($"{Plugin.Name} {Language.SongsTitle} Collection###SongsWindow")
    {
        Plugin = plugin;
        _importHelper = new SongImportHelper(plugin);
        _importHelper.OnImportCompleted = OnImportCompleted;
        _importHelper.OnSyncCompleted = OnSyncCompleted;

        Flags = ImGuiWindowFlags.MenuBar;
        Size = ImGuiHelpers.ScaledVector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(350, 300),
            // MaximumSize = ImGuiHelpers.ScaledVector2(350, float.MaxValue)
        };
    }

    private async Task OnImportCompleted()
    {
        await LoadSongsAsync();
    }

    private void OnSyncCompleted()
    {
        _ = LoadSongsAsync();
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _ = LoadSongsAsync();
    }

    public override void OnClose()
    {
        ResetState();
        base.OnClose();
    }

    private void ResetState()
    {
        _songs.Clear();
        _searchIndexes.Clear();
        _search = string.Empty;
    }

    public async Task LoadSongsAsync()
    {
        _isLoading = true;
        try
        {
            _songs = await ServiceContainer.SongRepository.GetAllSongsWithTagsAsync();
            _availableTagNames = _songs
                .SelectMany(s => s.Tags)
                .Select(t => t.Name ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            Search();
        }
        finally
        {
            _isLoading = false;
        }
    }
}
