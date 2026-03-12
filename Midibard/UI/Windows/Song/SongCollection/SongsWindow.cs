using System.Collections.Generic;
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

    // Add selected songs to playlist
    private readonly List<Playlist.Playlist> _playlistTargets = new();
    private int _selectedPlaylistTargetIndex = 0;
    private bool _isLoadingPlaylistTargets = false;
    private bool _closeAddToPlaylistPopup = false;

    // Bulk tag selected songs
    private readonly List<Tag> _tagTargets = new();
    private int _selectedTagTargetIndex = 0;
    private bool _isLoadingTagTargets = false;
    private bool _closeBulkTagPopup = false;
    private bool _bulkTagAdd = true;

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
    }

    private async Task OnImportCompleted()
    {
        await LoadSongsAsync();
    }

    private void OnSyncCompleted()
    {
        _ = LoadSongsAsync();
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
            Search();
        }
        finally
        {
            _isLoading = false;
        }
    }
}
