using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Resources;
using MidiBard.Playlist;

namespace MidiBard;

public partial class PlaylistWindow : Window
{
    private Plugin Plugin { get; }

    // UI State
    private List<Playlist.Playlist> _playlists = new();
    private Playlist.Playlist? _selectedPlaylist;
    private Song? _selectedSong;
    private int _selectedSongIndex = -1;

    private static readonly List<PlaylistSong> _emptyPlaylistSongs = new();
    private List<PlaylistSong> PlaylistSongs => _selectedPlaylist?.Songs ?? _emptyPlaylistSongs;

    // Deferred popup opens (set inside menu items, flushed after EndMenuBar)
    private string? _pendingPopup;
    private void OpenPopup(string id) => _pendingPopup = id;

    // Cached total duration - recalculated in LoadPlaylistSongsAsync
    private TimeSpan _playlistTotalDuration;

    // Form state
    private string _newPlaylistName = string.Empty;
    private string _editPlaylistName = string.Empty;

    // Search
    private readonly List<int> _songSearchIndexes = new();
    private readonly List<int> _playlistSearchIndexes = new();
    private string _songSearch = string.Empty;
    private string _playlistSearch = string.Empty;

    // Panel resizing
    private float _leftPanelWidth = 200f;
    private bool _showPlaylistEditorLeftPanel = true;

    private bool _isLoading;

    // Import helper (progress tracking, dialog, cancellation)
    private readonly SongImportHelper _importHelper;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    // Per-column filters
    private string _filterName = string.Empty;
    private string _filterArtist = string.Empty;
    private string _filterYear = string.Empty;
    private string _filterTags = string.Empty;
    private readonly ImGuiComboSearch _filterTagsCombo = new();
    private string _filterComments = string.Empty;
    private string _filterFilePath = string.Empty;
    // 0 = all, 1 = played only, 2 = not played
    private int _filterPlayed = 0;

    // Tag names present in the current playlist - used to populate the tag filter combo
    private List<string> _availableTagNames = new();

    // Sort state
    private SongSortColumn? _sortCol = null;
    private bool _sortAsc = true;

    public PlaylistWindow(Plugin plugin) : base($"{Plugin.Name} {Language.window_playlist_tab} Editor###PlaylistWindow")
    {
        Plugin = plugin;
        _importHelper = new SongImportHelper(plugin);

        Flags = ImGuiWindowFlags.MenuBar;
        Size = ImGuiHelpers.ScaledVector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(350, 300),
            // MaximumSize = ImGuiHelpers.ScaledVector2(350, float.MaxValue)
        };
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _ = LoadPlaylistsAsync();
    }

    public override void OnClose()
    {
        ResetState();
        base.OnClose();
    }

    private void ResetState()
    {
        _playlists.Clear();
        _selectedPlaylist = null;
        _selectedSong = null;
        _selectedSongIndex = -1;
        _songSearchIndexes.Clear();
        _playlistSearchIndexes.Clear();
        _playlistTotalDuration = TimeSpan.Zero;
    }

    public async Task LoadPlaylistsAsync()
    {
        _isLoading = true;
        try
        {
            _playlists = await Plugin.PlaylistManager.GetAllPlaylistsAsync();
            _playlistSearchIndexes.Clear();
            _playlistSearchIndexes.AddRange(Enumerable.Range(0, _playlists.Count));

            // Auto-select: keep existing selection if still valid, otherwise pick first
            var stillExists = _selectedPlaylist != null && _playlists.Any(p => p.Id == _selectedPlaylist.Id);
            if (!stillExists)
                _selectedPlaylist = _playlists.Count > 0 ? _playlists[0] : null;

            if (_selectedPlaylist != null)
                await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadPlaylistSongsAsync(int playlistId)
    {
        _isLoading = true;
        try
        {
            var playlist = await Plugin.PlaylistManager.GetPlaylistByIdAsync(playlistId);
            _selectedPlaylist = playlist ?? new Playlist.Playlist { Id = playlistId };
            _selectedSongIndex = -1;
            _selectedSong = null;
            _playlistTotalDuration = PlaylistSongs.Aggregate(TimeSpan.Zero, (acc, ps) => acc + (ps.Song?.Duration ?? TimeSpan.Zero));
            _availableTagNames = PlaylistSongs
                .SelectMany(ps => ps.Song?.Tags ?? Enumerable.Empty<Tag>())
                .Select(t => t.Name ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            SearchSongs();
        }
        finally
        {
            _isLoading = false;
        }
    }
}
