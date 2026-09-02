import { h, render, Component } from "/vendor/preact/index.js";

const TOKEN_KEY = "midibard.remote.token";
const API = "/api/v1";

function formatTime(milliseconds) {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = String(totalSeconds % 60).padStart(2, "0");
  return minutes + ":" + seconds;
}

function schemaLabel(schema) {
  if (!schema) return "—";
  if (schema.$ref) return schema.$ref.split("/").pop();
  if (schema.allOf?.[0]?.$ref) return schema.allOf[0].$ref.split("/").pop();
  if (schema.type === "array") return "Array<" + schemaLabel(schema.items) + ">";
  return schema.format ? schema.type + " (" + schema.format + ")" : (schema.type || "object");
}

function Nav({ connected }) {
  return h("header", { class: "topbar" },
    h("div", { class: "brand" },
      h("div", { class: "brand-mark" }, "M2"),
      h("div", null,
        h("strong", null, "MidiBard 2"),
        h("span", null, "Remote Control")
      )
    ),
    h("nav", null,
      h("a", { href: "/", class: location.pathname === "/" ? "active" : "" }, "Controller"),
      h("a", { href: "/docs/", class: location.pathname.startsWith("/docs") ? "active" : "" }, "API Docs"),
      connected == null ? null : h("span", { class: "connection " + (connected ? "online" : "offline") },
        h("i", null), connected ? "Connected" : "Disconnected")
    )
  );
}

function ErrorBanner({ message, onDismiss }) {
  if (!message) return null;
  return h("div", { class: "error-banner" },
    h("span", null, message),
    h("button", { type: "button", onClick: onDismiss, "aria-label": "Dismiss error" }, "×")
  );
}

class ApiDocs extends Component {
  state = { spec: null, error: null };

  async componentDidMount() {
    try {
      const response = await fetch("/openapi.json", { cache: "no-store" });
      if (!response.ok) throw new Error("OpenAPI request failed (" + response.status + ")");
      this.setState({ spec: await response.json() });
    } catch (error) {
      this.setState({ error: error.message || String(error) });
    }
  }

  render() {
    const { spec, error } = this.state;
    return h("div", null,
      h(Nav, { connected: null }),
      h("main", { class: "page docs-page" },
        h("section", { class: "hero compact" },
          h("div", null,
            h("p", { class: "eyebrow" }, "OPENAPI 3.0"),
            h("h1", null, spec?.info?.title || "Remote Control API"),
            h("p", null, spec?.info?.description || "Loading API contract…")
          ),
          h("a", { class: "secondary button-link", href: "/openapi.json" }, "Raw OpenAPI")
        ),
        h(ErrorBanner, { message: error, onDismiss: () => this.setState({ error: null }) }),
        !spec ? h("div", { class: "card loading" }, "Loading API documentation…") :
          h("div", { class: "docs-layout" },
            h("aside", { class: "card docs-summary" },
              h("h2", null, "Authentication"),
              h("p", null, "All /api/v1 operations require the MidiBard remote-control token as a Bearer token."),
              h("code", null, "Authorization: Bearer <token>"),
              h("h2", null, "Server"),
              h("p", null, "The API is bound to loopback only. The relative server URL is ", h("code", null, "/"), "."),
              h("h2", null, "Contract"),
              h("p", null, "This page reads the OpenAPI document generated from the same endpoint registry the server executes.")
            ),
            h("section", { class: "endpoint-list" },
              Object.entries(spec.paths || {}).flatMap(([path, methods]) =>
                Object.entries(methods).map(([method, operation]) =>
                  h("article", { class: "card endpoint", key: method + ":" + path },
                    h("div", { class: "endpoint-heading" },
                      h("span", { class: "method method-" + method }, method.toUpperCase()),
                      h("code", { class: "endpoint-path" }, path)
                    ),
                    h("h3", null, operation.operationId),
                    h("p", null, operation.description),
                    operation.parameters?.length ? h("div", { class: "doc-section" },
                      h("strong", null, "Query parameters"),
                      h("ul", null, operation.parameters.map(parameter =>
                        h("li", { key: parameter.name },
                          h("code", null, parameter.name), " — ", parameter.description,
                          " (", schemaLabel(parameter.schema), ")"
                        )
                      ))
                    ) : null,
                    operation.requestBody ? h("div", { class: "doc-section" },
                      h("strong", null, "Request body"),
                      h("code", null, schemaLabel(operation.requestBody.content?.["application/json"]?.schema))
                    ) : null,
                    h("div", { class: "doc-section" },
                      h("strong", null, "Responses"),
                      h("div", { class: "response-chips" }, Object.entries(operation.responses || {}).map(([code, response]) =>
                        h("span", { key: code }, h("b", null, code), " ", response.description)
                      ))
                    )
                  )
                )
              )
            )
          )
      )
    );
  }
}

class RemoteController extends Component {
  state = {
    connected: false,
    tokenInput: "",
    status: null,
    statusReceivedAt: 0,
    playlists: [],
    selectedPlaylistId: null,
    selectedPlaylist: null,
    search: "",
    sortColumn: null,
    sortAscending: true,
    error: null,
    busy: null,
    clock: Date.now()
  };

  apiToken = "";
  pollGeneration = 0;
  ticker = null;
  statusTimer = null;
  statusRefreshInFlight = false;

  componentDidMount() {
    const savedToken = sessionStorage.getItem(TOKEN_KEY);
    if (savedToken) this.connect(savedToken);
  }

  componentWillUnmount() {
    this.pollGeneration++;
    if (this.ticker) clearInterval(this.ticker);
    if (this.statusTimer) clearInterval(this.statusTimer);
  }

  async rawRequest(token, path, options = {}) {
    const headers = new Headers(options.headers || {});
    headers.set("Authorization", "Bearer " + token);
    if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");

    const response = await fetch(path, { ...options, headers, cache: "no-store" });
    const contentType = response.headers.get("content-type") || "";
    const body = response.status === 204 ? null :
      contentType.includes("application/json") ? await response.json() : await response.text();

    if (!response.ok) {
      const error = new Error(body?.message || body || "Request failed (" + response.status + ")");
      error.status = response.status;
      error.code = body?.code;
      throw error;
    }
    return body;
  }

  request(path, options) {
    return this.rawRequest(this.apiToken, path, options);
  }

  async connect(tokenOverride) {
    const token = (tokenOverride || this.state.tokenInput).trim();
    if (!token) {
      this.setState({ error: "Enter the remote-control token shown in MidiBard settings." });
      return;
    }

    this.setState({ busy: "connect", error: null });
    try {
      const [status, playlistsResponse] = await Promise.all([
        this.rawRequest(token, API + "/status"),
        this.rawRequest(token, API + "/playlists")
      ]);
      const playlists = playlistsResponse.playlists || [];
      const currentId = status.currentPlaylist?.isTemporary ? null : status.currentPlaylist?.id;
      const selectedPlaylistId =
        playlists.some(playlist => playlist.id === currentId)
          ? currentId
          : (playlists[0]?.id ?? null);
      const selectedPlaylist = selectedPlaylistId == null
        ? null
        : await this.rawRequest(
            token,
            API + "/playlist?playlistId=" + encodeURIComponent(selectedPlaylistId));

      this.apiToken = token;
      sessionStorage.setItem(TOKEN_KEY, token);
      this.setState({
        connected: true,
        tokenInput: "",
        status,
        statusReceivedAt: Date.now(),
        playlists,
        selectedPlaylistId,
        selectedPlaylist,
        busy: null
      });

      this.startTimers();
      const generation = ++this.pollGeneration;
      this.pollEvents(generation, status.latestEventSequence || 0);
    } catch (error) {
      this.apiToken = "";
      sessionStorage.removeItem(TOKEN_KEY);
      this.setState({ connected: false, busy: null, error: error.message || String(error) });
    }
  }

  disconnect(message = null) {
    this.apiToken = "";
    sessionStorage.removeItem(TOKEN_KEY);
    this.pollGeneration++;
    if (this.ticker) clearInterval(this.ticker);
    if (this.statusTimer) clearInterval(this.statusTimer);
    this.ticker = null;
    this.statusTimer = null;
    this.setState({
      connected: false,
      status: null,
      playlists: [],
      selectedPlaylistId: null,
      selectedPlaylist: null,
      busy: null,
      error: message
    });
  }

  startTimers() {
    if (this.ticker) clearInterval(this.ticker);
    if (this.statusTimer) clearInterval(this.statusTimer);
    this.ticker = setInterval(() => this.setState({ clock: Date.now() }), 250);
    this.statusTimer = setInterval(() => this.periodicRefreshStatus(), 1500);
  }

  handleRequestError(error) {
    if (error.status === 401) {
      this.disconnect("The remote-control token is no longer valid.");
      return true;
    }
    this.setState({ error: error.message || String(error) });
    return false;
  }

  async refreshStatus() {
    const previousCurrentId = this.state.status?.currentPlaylist?.id ?? null;
    const status = await this.request(API + "/status");
    const nextCurrentId = status.currentPlaylist?.id ?? null;
    this.setState({ status, statusReceivedAt: Date.now() });

    if (previousCurrentId !== nextCurrentId) {
      await this.refreshPlaylists();
      const selectedId = this.state.selectedPlaylistId;
      if (selectedId != null &&
          (selectedId === previousCurrentId || selectedId === nextCurrentId)) {
        await this.refreshSelectedPlaylist(selectedId);
      }
    }

    return status;
  }

  async periodicRefreshStatus() {
    if (!this.state.connected || this.statusRefreshInFlight) return;
    this.statusRefreshInFlight = true;
    try {
      await this.refreshStatus();
    } catch (error) {
      this.handleRequestError(error);
    } finally {
      this.statusRefreshInFlight = false;
    }
  }

  async refreshPlaylists() {
    const response = await this.request(API + "/playlists");
    const playlists = response.playlists || [];
    this.setState({ playlists });

    const selectedId = this.state.selectedPlaylistId;
    if (selectedId != null && !playlists.some(playlist => playlist.id === selectedId)) {
      const currentId = this.state.status?.currentPlaylist?.isTemporary
        ? null
        : this.state.status?.currentPlaylist?.id;
      const replacementId = playlists.some(playlist => playlist.id === currentId)
        ? currentId
        : (playlists[0]?.id ?? null);

      if (replacementId == null) {
        this.setState({ selectedPlaylistId: null, selectedPlaylist: null });
      } else {
        await this.selectPlaylist(replacementId);
      }
    }
  }

  async refreshSelectedPlaylist(playlistId = this.state.selectedPlaylistId) {
    if (playlistId == null) {
      this.setState({ selectedPlaylist: null });
      return null;
    }
    const playlist = await this.request(
      API + "/playlist?playlistId=" + encodeURIComponent(playlistId));
    if (playlistId === this.state.selectedPlaylistId) {
      this.setState({ selectedPlaylist: playlist });
    }
    return playlist;
  }

  async selectPlaylist(playlistId) {
    if (playlistId === this.state.selectedPlaylistId && this.state.selectedPlaylist) return;
    this.setState({ busy: "playlist", error: null });
    try {
      const playlist = await this.request(
        API + "/playlist?playlistId=" + encodeURIComponent(playlistId));
      this.setState({
        selectedPlaylistId: playlistId,
        selectedPlaylist: playlist,
        busy: null
      });
    } catch (error) {
      if (!this.handleRequestError(error)) this.setState({ busy: null });
    }
  }

  async pollEvents(generation, after) {
    let sequence = after;
    while (generation === this.pollGeneration) {
      try {
        const result = await this.request(API + "/events?after=" + sequence + "&timeoutMs=30000");
        sequence = result.latestSequence;
        if (result.events?.length) {
          const types = new Set(result.events.map(event => event.type));
          const status = await this.refreshStatus();
          if (types.has("playback_completed") || types.has("playback_stopped")) {
            await this.refreshPlaylists();
            if (this.state.selectedPlaylistId != null &&
                this.state.selectedPlaylistId === status.currentPlaylist?.id) {
              await this.refreshSelectedPlaylist();
            }
          }
        }
      } catch (error) {
        if (generation !== this.pollGeneration) return;
        if (error.status === 401) {
          this.disconnect("The remote-control token is no longer valid.");
          return;
        }
        if (error.status === 410) {
          const status = await this.refreshStatus();
          sequence = status.latestEventSequence || 0;
          continue;
        }
        this.setState({ error: error.message || String(error) });
        await new Promise(resolve => setTimeout(resolve, 1000));
      }
    }
  }

  async perform(name, action, options = {}) {
    this.setState({ busy: name, error: null });
    try {
      await action();
      await this.refreshStatus();
      if (options.refreshPlaylists) await this.refreshPlaylists();
      if (options.refreshSelected) await this.refreshSelectedPlaylist();
      this.setState({ busy: null });
    } catch (error) {
      if (!this.handleRequestError(error)) this.setState({ busy: null });
    }
  }

  playbackRequest(path) {
    const playbackId = this.state.status?.playback?.nowPlaying?.playbackId;
    if (!playbackId) return Promise.reject(new Error("No playback is loaded."));
    return this.request(path, { method: "POST", body: JSON.stringify({ playbackId }) });
  }

  loadSong(song) {
    const playlistId = this.state.selectedPlaylistId;
    if (playlistId == null) return;
    return this.perform(
      "load",
      () => this.request(API + "/playback/load-song", {
        method: "POST",
        body: JSON.stringify({ playlistId, songId: song.songId })
      }),
      { refreshPlaylists: true, refreshSelected: true });
  }

  refreshLibrary() {
    return this.perform(
      "refresh",
      () => Promise.resolve(),
      { refreshPlaylists: true, refreshSelected: true });
  }

  estimatedPosition() {
    const nowPlaying = this.state.status?.playback?.nowPlaying;
    if (!nowPlaying) return 0;
    const elapsed = this.state.status?.playback?.state === "playing"
      ? Math.max(0, this.state.clock - this.state.statusReceivedAt)
      : 0;
    return Math.min(nowPlaying.durationMs, Math.max(0, nowPlaying.positionMs + elapsed));
  }

  sortValue(song, column) {
    switch (column) {
      case "position": return song.position || 0;
      case "name": return (song.name || song.fileName || "").toLowerCase();
      case "artist": return (song.artist || "").toLowerCase();
      case "durationMs": return song.durationMs || 0;
      case "playCount": return song.playCount || 0;
      case "lastPlayedAt": return song.lastPlayedAt ? Date.parse(song.lastPlayedAt) || 0 : 0;
      case "isPlayed": return song.isPlayed ? 1 : 0;
      case "rating": return song.rating || 0;
      case "fileModifiedAt": return song.fileModifiedAt ? Date.parse(song.fileModifiedAt) || 0 : 0;
      default: return song.position || 0;
    }
  }

  visibleSongs() {
    const songs = this.state.selectedPlaylist?.songs || [];
    const query = this.state.search.trim().toLowerCase();
    const filtered = songs.filter(song =>
      !query ||
      (song.name || "").toLowerCase().includes(query) ||
      (song.artist || "").toLowerCase().includes(query) ||
      (song.fileName || "").toLowerCase().includes(query));

    const column = this.state.sortColumn;
    if (!column) return filtered;

    const direction = this.state.sortAscending ? 1 : -1;
    return [...filtered].sort((a, b) => {
      const av = this.sortValue(a, column);
      const bv = this.sortValue(b, column);
      if (typeof av === "string" || typeof bv === "string") {
        return String(av).localeCompare(String(bv), undefined, { numeric: true }) * direction;
      }
      return (av - bv) * direction;
    });
  }

  setSort(column) {
    if (this.state.sortColumn === column) {
      this.setState({ sortAscending: !this.state.sortAscending });
    } else {
      this.setState({ sortColumn: column, sortAscending: true });
    }
  }

  sortHeader(label, column, className = "") {
    const active = this.state.sortColumn === column;
    const marker = active ? (this.state.sortAscending ? " ↑" : " ↓") : " ↕";
    return h("th", { class: className },
      h("button", {
        type: "button",
        class: "sort-header" + (active ? " active" : ""),
        onClick: () => this.setSort(column)
      }, label + marker)
    );
  }

  formatDate(value) {
    if (!value) return "—";
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? "—" : date.toLocaleString();
  }

  renderLogin() {
    return h("main", { class: "page login-page" },
      h("section", { class: "card login-card" },
        h("p", { class: "eyebrow" }, "LOOPBACK REMOTE"),
        h("h1", null, "Connect to MidiBard"),
        h("p", null, "Enter the token shown under MidiBard Settings → Stream Support → Remote Control."),
        h("form", { onSubmit: event => { event.preventDefault(); this.connect(); } },
          h("label", null, "Remote-control token"),
          h("input", {
            type: "password",
            value: this.state.tokenInput,
            autocomplete: "off",
            spellcheck: false,
            autofocus: true,
            onInput: event => this.setState({ tokenInput: event.currentTarget.value })
          }),
          h("button", { class: "primary", type: "submit", disabled: this.state.busy === "connect" },
            this.state.busy === "connect" ? "Connecting…" : "Connect")
        ),
        h(ErrorBanner, { message: this.state.error, onDismiss: () => this.setState({ error: null }) }),
        h("p", { class: "muted small" }, "The token is kept only in this browser session. MidiBard accepts connections from localhost only.")
      )
    );
  }

  renderController() {
    const { status, selectedPlaylist } = this.state;
    const playback = status?.playback || {};
    const nowPlaying = playback.nowPlaying;
    const ensemble = status?.ensemble || {};
    const player = status?.player || {};
    const controls = status?.controls || {};
    const position = this.estimatedPosition();
    const progress = nowPlaying?.durationMs ? Math.min(100, position / nowPlaying.durationMs * 100) : 0;
    const songs = this.visibleSongs();
    const busy = !!this.state.busy;
    const currentPlaylist = status?.currentPlaylist;

    return h("main", { class: "page controller-page" },
      h(ErrorBanner, { message: this.state.error, onDismiss: () => this.setState({ error: null }) }),
      player.canPerform === false
        ? h("div", { class: "info-banner" },
            h("strong", null, "Performance unavailable."),
            h("span", null, " Switch to Bard to load or play songs."))
        : null,

      h("section", { class: "card now-playing" },
        h("div", { class: "section-heading" },
          h("div", null,
            h("p", { class: "eyebrow" }, "NOW PLAYING"),
            h("h1", null, nowPlaying?.fileName || "Nothing loaded")
          ),
          h("span", { class: "state state-" + (playback.state || "idle") }, playback.state || "idle")
        ),
        h("div", {
          class: "progress-track",
          role: "progressbar",
          "aria-valuenow": Math.round(progress),
          "aria-valuemin": 0,
          "aria-valuemax": 100
        }, h("div", { class: "progress-fill", style: { width: progress + "%" } })),
        h("div", { class: "time-row" },
          h("span", null, formatTime(position)),
          h("span", null, formatTime(nowPlaying?.durationMs || 0))
        ),
        h("div", { class: "controls" },
          h("button", {
            class: "primary",
            disabled: !controls.canPlay || busy,
            onClick: () => this.perform("play", () => this.playbackRequest(API + "/playback/play"))
          }, "▶ Play Solo"),
          h("button", {
            disabled: !controls.canPause || busy,
            onClick: () => this.perform("pause", () => this.playbackRequest(API + "/playback/pause"))
          }, "❚❚ Pause"),
          h("button", {
            disabled: !controls.canStop || busy,
            onClick: () => this.perform(
              "stop",
              () => this.playbackRequest(API + "/playback/stop"),
              { refreshSelected: true })
          }, "■ Stop"),
          h("button", {
            class: "ensemble-button",
            disabled: !controls.canStartEnsemble || busy,
            onClick: () => this.perform(
              "ensemble",
              () => this.playbackRequest(API + "/ensemble/ready-check"))
          }, "♪ Ensemble Ready Check")
        ),
        h("div", { class: "status-meta" },
          h("span", null, "Job ", h("b", null, player.classJobAbbreviation || "—")),
          h("span", null, ensemble.inParty ? "In party" : "Solo"),
          h("span", null, ensemble.isPartyLeader ? "Party leader" : "Not leader"),
          h("span", null, "Monitoring ", ensemble.monitoringEnabled ? "on" : "off"),
          h("span", null, "Sync ", ensemble.syncClientsEnabled ? "on" : "off")
        )
      ),

      h("div", { class: "library-grid" },
        h("aside", { class: "card playlist-sidebar" },
          h("div", { class: "section-heading" },
            h("div", null,
              h("p", { class: "eyebrow" }, "LIBRARY"),
              h("h2", null, "Playlists")
            ),
            h("button", {
              class: "icon-button",
              title: "Refresh playlists",
              disabled: busy,
              onClick: () => this.refreshLibrary()
            }, "↻")
          ),
          currentPlaylist?.isTemporary
            ? h("div", { class: "temporary-playlist-note" },
                h("strong", null, "Current: "),
                currentPlaylist.name)
            : null,
          h("div", { class: "playlist-nav" },
            this.state.playlists.length
              ? this.state.playlists.map(playlist =>
                  h("button", {
                    type: "button",
                    key: playlist.id,
                    class:
                      "playlist-nav-item" +
                      (playlist.id === this.state.selectedPlaylistId ? " selected" : "") +
                      (playlist.isCurrent ? " current" : ""),
                    disabled: busy,
                    onClick: () => this.selectPlaylist(playlist.id)
                  },
                    h("span", null,
                      h("strong", null, playlist.name),
                      playlist.isCurrent ? h("em", null, "Current") : null
                    ),
                    h("small", null,
                      playlist.songCount + " songs · " + formatTime(playlist.durationMs))
                  )
                )
              : h("p", { class: "empty" }, "No persisted playlists.")
          )
        ),

        h("section", { class: "card playlist-browser" },
          h("div", { class: "playlist-browser-heading" },
            h("div", null,
              h("p", { class: "eyebrow" }, selectedPlaylist?.isCurrent ? "CURRENT PLAYLIST" : "PLAYLIST"),
              h("h2", null, selectedPlaylist?.name || "Select a playlist"),
              selectedPlaylist
                ? h("p", { class: "muted small" },
                    selectedPlaylist.songCount + " songs · " + formatTime(selectedPlaylist.durationMs))
                : null
            ),
            selectedPlaylist
              ? h("input", {
                  class: "search library-search",
                  type: "search",
                  placeholder: "Search name, artist, or filename…",
                  value: this.state.search,
                  onInput: event => this.setState({ search: event.currentTarget.value })
                })
              : null
          ),

          !selectedPlaylist
            ? h("p", { class: "empty" }, "Choose a persisted playlist from the left.")
            : h("div", { class: "song-table-wrap" },
                h("table", { class: "song-table" },
                  h("thead", null,
                    h("tr", null,
                      this.sortHeader("#", "position", "number-column"),
                      this.sortHeader("Name", "name", "name-column"),
                      this.sortHeader("Artist", "artist", "artist-column"),
                      this.sortHeader("Duration", "durationMs"),
                      this.sortHeader("Plays", "playCount"),
                      this.sortHeader("Last Played", "lastPlayedAt"),
                      this.sortHeader("Played", "isPlayed"),
                      this.sortHeader("Rating", "rating"),
                      this.sortHeader("File Modified", "fileModifiedAt"),
                      h("th", { class: "action-column" }, "Action")
                    )
                  ),
                  h("tbody", null,
                    songs.length
                      ? songs.map(song => {
                          const exactLoaded =
                            nowPlaying?.playlistId === selectedPlaylist.id &&
                            nowPlaying?.songId === song.songId;
                          const legacyLoaded =
                            nowPlaying?.songId == null &&
                            selectedPlaylist.isCurrent &&
                            nowPlaying?.fileName === song.fileName;
                          const loaded = exactLoaded || legacyLoaded;
                          const canLoad = !!controls.canLoad && song.isValid && !busy;

                          return h("tr", {
                            key: song.songId,
                            class: (loaded ? "loaded " : "") + (!song.isValid ? "invalid" : "")
                          },
                            h("td", { class: "number-column" }, song.position),
                            h("td", { class: "name-column" },
                              h("strong", null, song.name || song.fileName),
                              song.name && song.fileName !== song.name + ".mid"
                                ? h("small", null, song.fileName)
                                : null
                            ),
                            h("td", { class: "artist-column" }, song.artist || "—"),
                            h("td", null, formatTime(song.durationMs)),
                            h("td", null, song.playCount ?? 0),
                            h("td", { class: "date-cell" }, this.formatDate(song.lastPlayedAt)),
                            h("td", { class: "center-cell" }, song.isPlayed ? "✓" : "—"),
                            h("td", { class: "rating-cell" },
                              song.rating > 0 ? "★".repeat(Math.min(5, song.rating)) : "—"),
                            h("td", { class: "date-cell" }, this.formatDate(song.fileModifiedAt)),
                            h("td", { class: "action-column" },
                              h("button", {
                                type: "button",
                                class: loaded ? "loaded-button" : "",
                                disabled: !canLoad,
                                title: !song.isValid
                                  ? "MidiBard reports this song file as invalid."
                                  : loaded ? "This song is loaded." : "Load this song.",
                                onClick: () => this.loadSong(song)
                              }, loaded ? "Loaded" : "Load")
                            )
                          );
                        })
                      : h("tr", null,
                          h("td", { colspan: 10, class: "empty table-empty" },
                            this.state.search.trim()
                              ? "No songs match this search."
                              : "This playlist is empty."))
                  )
                )
              )
        )
      ),

      h("footer", null,
        h("span", null,
          "Play mode: " + (playback.playMode || "—") +
          " · Current playlist: " + (currentPlaylist?.name || "—")),
        h("button", { class: "link-button", onClick: () => this.disconnect() }, "Disconnect")
      )
    );
  }

  render() {
    return h("div", null,
      h(Nav, { connected: this.state.connected }),
      this.state.connected ? this.renderController() : this.renderLogin()
    );
  }
}

const root = document.getElementById("app");
render(location.pathname.startsWith("/docs") ? h(ApiDocs) : h(RemoteController), root);
