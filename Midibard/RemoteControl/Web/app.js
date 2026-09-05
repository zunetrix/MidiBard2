import { h, render, Component } from "/vendor/preact/index.js";

const TOKEN_KEY = "midibard.remote.token";
const API = "/api/v1";
const PLAY_MODES = [
  ["single", "Single"],
  ["single_repeat", "Single Repeat"],
  ["list_ordered", "List Ordered"],
  ["list_repeat", "List Repeat"],
  ["random", "Random"]
];

const PLAY_MODE_DESCRIPTIONS = {
  single: "Stop after the current song.",
  single_repeat: "Replay the current song.",
  list_ordered: "Advance through the active playlist, then stop at the end.",
  list_repeat: "Advance through the active playlist and wrap at the end.",
  random: "Choose another song from the active playlist, preferring unplayed songs."
};

function consumeUrlToken() {
  const query = new URLSearchParams(window.location.search);
  const hash = new URLSearchParams(
    window.location.hash.startsWith("#")
      ? window.location.hash.slice(1)
      : window.location.hash);
  const token = (hash.get("token") || query.get("token") || "").trim();
  if (!token) return null;

  query.delete("token");
  hash.delete("token");
  const queryString = query.toString();
  const hashString = hash.toString();
  window.history.replaceState(
    {},
    document.title,
    window.location.pathname +
      (queryString ? "?" + queryString : "") +
      (hashString ? "#" + hashString : ""));
  return token;
}

function formatTime(milliseconds) {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = String(totalSeconds % 60).padStart(2, "0");

  return hours > 0
    ? hours + ":" + String(minutes).padStart(2, "0") + ":" + seconds
    : minutes + ":" + seconds;
}

function estimatedPlaybackPosition(playback, statusReceivedAt, clock) {
  const nowPlaying = playback?.nowPlaying;
  if (!nowPlaying) return 0;
  const elapsed = playback?.state === "playing"
    ? Math.max(0, clock - statusReceivedAt)
    : 0;
  return Math.min(
    nowPlaying.durationMs,
    Math.max(0, nowPlaying.positionMs + elapsed));
}

function schemaLabel(schema) {
  if (!schema) return "—";
  if (schema.$ref) return schema.$ref.split("/").pop();
  if (schema.allOf?.[0]?.$ref) return schema.allOf[0].$ref.split("/").pop();
  if (schema.type === "array") return "Array<" + schemaLabel(schema.items) + ">";
  return schema.format ? schema.type + " (" + schema.format + ")" : (schema.type || "object");
}

function Nav({ connectionState }) {
  return h("header", { class: "topbar" },
    h("div", { class: "brand" },
      h("img", {
        class: "brand-mark",
        src: "/plugin-icon.png",
        alt: ""
      }),
      h("div", null,
        h("strong", null, "MidiBard 2"),
        h("span", null, "Remote Control")
      )
    ),
    h("nav", null,
      h("a", { href: "/", class: location.pathname === "/" ? "active" : "" }, "Controller"),
      h("a", { href: "/docs/", class: location.pathname.startsWith("/docs") ? "active" : "" }, "API Docs"),
      connectionState == null ? null : h("span", { class: "connection " + connectionState },
        h("i", null),
        connectionState === "online"
          ? "Connected"
          : connectionState === "reconnecting"
            ? "Reconnecting"
            : "Disconnected")
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
      h(Nav, { connectionState: null }),
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

class NowPlayingCard extends Component {
  state = { clock: Date.now(), seekPreview: null };
  ticker = null;

  componentDidMount() {
    this.ticker = setInterval(() => this.setState({ clock: Date.now() }), 250);
  }

  componentWillUnmount() {
    if (this.ticker) clearInterval(this.ticker);
  }

  render() {
    const {
      playback = {},
      ensemble = {},
      player = {},
      controls = {},
      currentPlaylist,
      busy
    } = this.props;
    const nowPlaying = playback.nowPlaying;
    const position = this.state.seekPreview ?? estimatedPlaybackPosition(
      playback,
      this.props.statusReceivedAt,
      this.state.clock);

    return h("section", { class: "card now-playing" },
      h("div", { class: "section-heading" },
        h("div", null,
          h("p", { class: "eyebrow" }, "NOW PLAYING"),
          h("h1", null, nowPlaying?.fileName || "Nothing loaded")
        ),
        h("span", { class: "state state-" + (playback.state || "idle") }, playback.state || "idle")
      ),
      h("input", {
        class: "seek-slider",
        type: "range",
        min: 0,
        max: Math.max(0, nowPlaying?.durationMs || 0),
        step: 100,
        value: Math.round(position),
        disabled: !nowPlaying || !controls.canSeek || busy,
        "aria-label": "Playback position",
        onInput: event => this.setState({
          seekPreview: Number(event.currentTarget.value)
        }),
        onChange: event => {
          const positionMs = Number(event.currentTarget.value);
          this.setState({ seekPreview: null });
          this.props.onSeek(positionMs);
        }
      }),
      h("div", { class: "time-row" },
        h("span", null, formatTime(position)),
        h("span", null, formatTime(nowPlaying?.durationMs || 0))
      ),
      h("div", { class: "controls" },
        h("button", {
          disabled: !controls.canPrevious || busy,
          onClick: this.props.onPrevious
        }, "⏮ Previous"),
        h("button", {
          class: "primary",
          disabled: !controls.canPlay || busy,
          onClick: this.props.onPlay
        }, "▶ Play Solo"),
        h("button", {
          disabled: !controls.canPause || busy,
          onClick: this.props.onPause
        }, "❚❚ Pause"),
        h("button", {
          disabled: !controls.canStop || busy,
          onClick: this.props.onStop
        }, "■ Stop"),
        h("button", {
          disabled: !controls.canNext || busy,
          onClick: this.props.onNext
        }, "Next ⏭"),
        h("button", {
          class: "ensemble-button",
          disabled: !controls.canStartEnsemble || busy,
          onClick: this.props.onEnsemble
        }, "♪ Ensemble Ready Check")
      ),
      h("div", { class: "sequence-panel" },
        h("div", { class: "sequence-heading" },
          h("span", { class: "eyebrow" }, "SEQUENCE"),
          h("span", { class: "sequence-playlist" },
            "Active playlist: ",
            h("strong", null, currentPlaylist?.name || "—")
          )
        ),
        h("div", { class: "sequence-controls" },
          h("label", { class: "sequence-mode" },
            h("span", null, "After song"),
            h("select", {
              value: playback.playMode || "single",
              disabled: busy,
              onChange: event => this.props.onPlayMode(
                event.currentTarget.value)
            }, PLAY_MODES.map(([value, label]) =>
              h("option", { value, key: value }, label)
            ))
          ),
          h("div", { class: "sequence-description" },
            PLAY_MODE_DESCRIPTIONS[playback.playMode] ||
              PLAY_MODE_DESCRIPTIONS.single
          )
        )
      ),
      h("div", { class: "status-meta" },
        h("span", null, "Job ", h("b", null, player.classJobAbbreviation || "—")),
        h("span", null, ensemble.inParty ? "In party" : "Solo"),
        h("span", null, ensemble.isPartyLeader ? "Party leader" : "Not leader"),
        h("span", null, "Monitoring ", ensemble.monitoringEnabled ? "on" : "off"),
        h("span", null, "Sync ", ensemble.syncClientsEnabled ? "on" : "off")
      )
    );
  }
}

function EnsemblePanel({ playback = {}, visualization }) {
  const nowPlaying = playback.nowPlaying;
  if (!nowPlaying ||
      !visualization ||
      visualization.playbackId !== nowPlaying.playbackId) {
    return null;
  }

  const instruments = visualization.instruments || [];
  return h("section", { class: "card ensemble-card" },
    h("div", { class: "section-heading ensemble-heading" },
      h("div", null,
        h("p", { class: "eyebrow" }, "ENSEMBLE"),
        h("h2", null, "Instruments")
      ),
      h("span", { class: "muted small" },
        instruments.length + (instruments.length === 1 ? " part" : " parts"))
    ),
    instruments.length
      ? h("div", { class: "ensemble-instrument-grid" },
          instruments.map((instrument, index) =>
            h("div", {
              class: "ensemble-instrument",
              key:
                (instrument.performerName || "unassigned") +
                ":" + instrument.instrumentId + ":" + index
            },
              h("span", {
                class: "ensemble-instrument-icon-shell",
                "aria-hidden": "true"
              },
                h("span", {
                  class: "ensemble-instrument-icon-fallback"
                }, "♪"),
                instrument.iconId > 0
                  ? h("img", {
                      class: "ensemble-instrument-icon",
                      src: "/instrument-icons/" + instrument.iconId + ".png",
                      alt: "",
                      loading: "lazy",
                      onError: event => event.currentTarget.remove()
                    })
                  : null
              ),
              h("span", { class: "ensemble-instrument-text" },
                h("strong", {
                  title: instrument.instrumentName || "Unknown instrument"
                }, instrument.instrumentName || "Unknown instrument"),
                h("small", {
                  title: instrument.performerName || "Unassigned"
                }, instrument.performerName || "Unassigned")
              )
            )
          )
        )
      : h("p", { class: "empty ensemble-empty" },
          "No enabled ensemble parts for this playback.")
  );
}

class PlaylistBrowser extends Component {
  state = {
    scrollTop: 0,
    viewSignature: null
  };

  tableWrap = null;
  scrollFrame = null;
  pendingScrollTop = 0;
  songCache = null;

  static getDerivedStateFromProps(props, state) {
    const signature = [
      props.playlist?.id ?? "",
      props.search,
      props.sortColumn ?? "",
      props.sortAscending ? "1" : "0"
    ].join("|");

    if (signature !== state.viewSignature) {
      return { scrollTop: 0, viewSignature: signature };
    }

    return null;
  }

  componentDidUpdate(previousProps) {
    const viewChanged =
      previousProps.playlist?.id !== this.props.playlist?.id ||
      previousProps.search !== this.props.search ||
      previousProps.sortColumn !== this.props.sortColumn ||
      previousProps.sortAscending !== this.props.sortAscending;

    if (viewChanged && this.tableWrap) {
      this.pendingScrollTop = 0;
      this.tableWrap.scrollTop = 0;
    }
  }

  componentWillUnmount() {
    if (this.scrollFrame != null) cancelAnimationFrame(this.scrollFrame);
  }

  shouldComponentUpdate(nextProps, nextState) {
    const loadedIdentity = props => {
      const nowPlaying = props.nowPlaying;
      return [
        nowPlaying?.playlistId ?? null,
        nowPlaying?.songId ?? null,
        nowPlaying?.fileName ?? null
      ].join(":");
    };

    return nextState.scrollTop !== this.state.scrollTop ||
      nextState.viewSignature !== this.state.viewSignature ||
      nextProps.playlist !== this.props.playlist ||
      nextProps.search !== this.props.search ||
      nextProps.sortColumn !== this.props.sortColumn ||
      nextProps.sortAscending !== this.props.sortAscending ||
      nextProps.canLoad !== this.props.canLoad ||
      nextProps.busy !== this.props.busy ||
      loadedIdentity(nextProps) !== loadedIdentity(this.props);
  }

  handleScroll(event) {
    this.pendingScrollTop = event.currentTarget.scrollTop;
    if (this.scrollFrame != null) return;

    this.scrollFrame = requestAnimationFrame(() => {
      this.scrollFrame = null;
      if (this.pendingScrollTop !== this.state.scrollTop) {
        this.setState({ scrollTop: this.pendingScrollTop });
      }
    });
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
    const playlist = this.props.playlist;
    const search = this.props.search;
    const sortColumn = this.props.sortColumn;
    const sortAscending = this.props.sortAscending;

    if (this.songCache &&
        this.songCache.playlist === playlist &&
        this.songCache.search === search &&
        this.songCache.sortColumn === sortColumn &&
        this.songCache.sortAscending === sortAscending) {
      return this.songCache.songs;
    }

    const songs = playlist?.songs || [];
    const query = search.trim().toLowerCase();
    const filtered = songs.filter(song =>
      !query ||
      (song.name || "").toLowerCase().includes(query) ||
      (song.artist || "").toLowerCase().includes(query) ||
      (song.fileName || "").toLowerCase().includes(query));

    let visible = filtered;
    if (sortColumn) {
      const direction = sortAscending ? 1 : -1;
      visible = [...filtered].sort((a, b) => {
        const av = this.sortValue(a, sortColumn);
        const bv = this.sortValue(b, sortColumn);
        if (typeof av === "string" || typeof bv === "string") {
          return String(av).localeCompare(String(bv), undefined, { numeric: true }) * direction;
        }
        return (av - bv) * direction;
      });
    }

    this.songCache = {
      playlist,
      search,
      sortColumn,
      sortAscending,
      songs: visible
    };
    return visible;
  }

  sortHeader(label, column, className = "") {
    const active = this.props.sortColumn === column;
    const marker = active ? (this.props.sortAscending ? " ↑" : " ↓") : " ↕";
    return h("th", { class: className },
      h("button", {
        type: "button",
        class: "sort-header" + (active ? " active" : ""),
        onClick: () => this.props.onSort(column)
      }, label + marker)
    );
  }

  formatDate(value) {
    if (!value) return "—";
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? "—" : date.toLocaleString();
  }

  renderSongRow(song, playlist, nowPlaying, canLoad, busy) {
    const exactLoaded =
      nowPlaying?.playlistId === playlist.id &&
      nowPlaying?.songId === song.songId;
    const legacyLoaded =
      nowPlaying?.songId == null &&
      playlist.isCurrent &&
      nowPlaying?.fileName === song.fileName;
    const loaded = exactLoaded || legacyLoaded;
    const rowCanLoad = !!canLoad && song.isValid && !busy;

    return h("tr", {
      key: song.songId,
      class: "song-row " + (loaded ? "loaded " : "") + (!song.isValid ? "invalid" : "")
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
          disabled: !rowCanLoad,
          title: !song.isValid
            ? "MidiBard reports this song file as invalid."
            : loaded ? "This song is loaded." : "Load this song.",
          onClick: () => this.props.onLoadSong(song)
        }, loaded ? "Loaded" : "Load")
      )
    );
  }

  renderSpacer(height, key) {
    if (height <= 0) return null;
    return h("tr", {
      key,
      class: "virtual-spacer",
      "aria-hidden": "true"
    }, h("td", {
      colspan: 10,
      style: { height: height + "px" }
    }));
  }

  render() {
    const { playlist, search, canLoad, busy, nowPlaying } = this.props;
    const songs = this.visibleSongs();

    const rowHeight = 50;
    const overscan = 6;
    const viewportHeight = this.tableWrap?.clientHeight || 620;
    const startIndex = Math.max(
      0,
      Math.floor(this.state.scrollTop / rowHeight) - overscan);
    const endIndex = Math.min(
      songs.length,
      Math.ceil((this.state.scrollTop + viewportHeight) / rowHeight) + overscan);
    const renderedSongs = songs.slice(startIndex, endIndex);
    const topSpacerHeight = startIndex * rowHeight;
    const bottomSpacerHeight = (songs.length - endIndex) * rowHeight;

    return h("section", { class: "card playlist-browser" },
      h("div", { class: "playlist-browser-heading" },
        h("div", null,
          h("p", { class: "eyebrow" }, playlist?.isCurrent ? "ACTIVE PLAYLIST" : "PLAYLIST"),
          h("h2", null, playlist?.name || "Select a playlist"),
          playlist
            ? h("p", { class: "muted small" },
                playlist.songCount + " songs · " + formatTime(playlist.durationMs))
            : null
        ),
        playlist
          ? h("input", {
              class: "search library-search",
              type: "search",
              placeholder: "Search name, artist, or filename…",
              value: search,
              onInput: event => this.props.onSearch(event.currentTarget.value)
            })
          : null
      ),

      !playlist
        ? h("p", { class: "empty" }, "Choose a persisted playlist from the left.")
        : h("div", {
            class: "song-table-wrap",
            ref: element => { this.tableWrap = element; },
            onScroll: event => this.handleScroll(event)
          },
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
                  ? [
                      this.renderSpacer(topSpacerHeight, "top-spacer"),
                      ...renderedSongs.map(song =>
                        this.renderSongRow(song, playlist, nowPlaying, canLoad, busy)),
                      this.renderSpacer(bottomSpacerHeight, "bottom-spacer")
                    ]
                  : h("tr", null,
                      h("td", { colspan: 10, class: "empty table-empty" },
                        search.trim()
                          ? "No songs match this search."
                          : "This playlist is empty."))
              )
            )
          )
    );
  }
}

class RemoteController extends Component {
  state = {
    connected: false,
    connectionState: "offline",
    tokenInput: "",
    status: null,
    statusReceivedAt: 0,
    playlists: [],
    selectedPlaylistId: null,
    selectedPlaylist: null,
    ensembleVisualization: null,
    search: "",
    sortColumn: null,
    sortAscending: true,
    error: null,
    busy: null
  };

  apiToken = "";
  pollGeneration = 0;
  playlistRequestGeneration = 0;
  statusTimer = null;
  statusRefreshInFlight = false;

  componentDidMount() {
    document.addEventListener("visibilitychange", this.handleVisibilityChange);
    window.addEventListener("focus", this.handleWindowFocus);

    const urlToken = consumeUrlToken();
    if (urlToken) {
      this.connect(urlToken);
      return;
    }

    const savedToken = sessionStorage.getItem(TOKEN_KEY);
    if (savedToken) this.connect(savedToken);
  }

  componentWillUnmount() {
    this.pollGeneration++;
    this.playlistRequestGeneration++;
    if (this.statusTimer) clearInterval(this.statusTimer);
    document.removeEventListener("visibilitychange", this.handleVisibilityChange);
    window.removeEventListener("focus", this.handleWindowFocus);
  }

  handleVisibilityChange = () => {
    if (document.visibilityState === "visible") this.periodicRefreshStatus();
  };

  handleWindowFocus = () => {
    this.periodicRefreshStatus();
  };

  async rawRequest(token, path, options = {}) {
    const headers = new Headers(options.headers || {});
    headers.set("Authorization", "Bearer " + token);
    if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");

    let response;
    try {
      response = await fetch(path, { ...options, headers, cache: "no-store" });
    } catch {
      const error = new Error(
        "Unable to reach the MidiBard remote-control server. Check that MidiBard is running and Remote Control is enabled.");
      error.code = "network_unreachable";
      throw error;
    }

    if (response.status === 204) return null;

    const contentType = response.headers.get("content-type") || "";
    if (!contentType.includes("application/json")) {
      const error = new Error(
        response.ok
          ? "Remote-control server returned an unexpected response."
          : response.status >= 500
            ? "Remote-control server unavailable (HTTP " + response.status + ")."
            : "Remote-control request failed (HTTP " + response.status + ").");
      error.status = response.status;
      throw error;
    }

    let body = null;
    try {
      body = await response.json();
    } catch {
      const error = new Error("Remote-control server returned invalid JSON.");
      error.status = response.status;
      throw error;
    }

    if (!response.ok) {
      const error = new Error(
        body?.message ||
        (response.status >= 500
          ? "Remote-control server unavailable (HTTP " + response.status + ")."
          : "Remote-control request failed (HTTP " + response.status + ")."));
      error.status = response.status;
      error.code = body?.code;
      throw error;
    }
    return body;
  }

  request(path, options) {
    return this.rawRequest(this.apiToken, path, options);
  }

  async fetchEnsembleVisualization(token, status) {
    const playbackId = status?.playback?.nowPlaying?.playbackId;
    if (!playbackId) return null;

    try {
      const visualization = await this.rawRequest(
        token,
        API + "/ensemble/visualization");
      return visualization?.playbackId === playbackId
        ? visualization
        : null;
    } catch (error) {
      if (error.status === 409) return null;
      if (error.status === 401) throw error;
      this.setState({
        error:
          "Ensemble visualization unavailable: " +
          (error.message || String(error))
      });
      return null;
    }
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
      const ensembleVisualization =
        await this.fetchEnsembleVisualization(token, status);

      this.apiToken = token;
      sessionStorage.setItem(TOKEN_KEY, token);
      this.setState({
        connected: true,
        connectionState: "online",
        tokenInput: "",
        status,
        statusReceivedAt: Date.now(),
        playlists,
        selectedPlaylistId,
        selectedPlaylist,
        ensembleVisualization,
        busy: null
      });

      this.startTimers();
      const generation = ++this.pollGeneration;
      this.pollEvents(generation, status.latestEventSequence || 0);
    } catch (error) {
      this.apiToken = "";
      sessionStorage.removeItem(TOKEN_KEY);
      this.setState({
        connected: false,
        connectionState: "offline",
        busy: null,
        error: error.message || String(error)
      });
    }
  }

  disconnect(message = null) {
    this.apiToken = "";
    sessionStorage.removeItem(TOKEN_KEY);
    this.pollGeneration++;
    this.playlistRequestGeneration++;
    if (this.statusTimer) clearInterval(this.statusTimer);
    this.statusTimer = null;
    this.setState({
      connected: false,
      connectionState: "offline",
      status: null,
      playlists: [],
      selectedPlaylistId: null,
      selectedPlaylist: null,
      ensembleVisualization: null,
      busy: null,
      error: message
    });
  }

  startTimers() {
    if (this.statusTimer) clearInterval(this.statusTimer);
    this.statusTimer = setInterval(() => this.periodicRefreshStatus(), 30000);
  }

  handleRequestError(error) {
    if (error.status === 401) {
      this.disconnect("The remote-control token is no longer valid.");
      return true;
    }
    this.setState({
      error: error.message || String(error),
      connectionState:
        error?.code === "network_unreachable" ||
        (typeof error?.status === "number" && error.status >= 500)
          ? "reconnecting"
          : this.state.connectionState
    });
    return false;
  }

  async refreshStatus() {
    const previousCurrentId = this.state.status?.currentPlaylist?.id ?? null;
    const previousPlaybackId =
      this.state.status?.playback?.nowPlaying?.playbackId ?? null;
    const status = await this.request(API + "/status");
    const nextCurrentId = status.currentPlaylist?.id ?? null;
    const nextPlaybackId = status.playback?.nowPlaying?.playbackId ?? null;
    const ensembleVisualization =
      previousPlaybackId === nextPlaybackId
        ? this.state.ensembleVisualization
        : await this.fetchEnsembleVisualization(this.apiToken, status);
    this.setState({
      status,
      statusReceivedAt: Date.now(),
      ensembleVisualization,
      connectionState: "online"
    });

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

    const previousPlaylistId = this.state.selectedPlaylistId;
    const generation = ++this.playlistRequestGeneration;
    this.setState({ selectedPlaylistId: playlistId, error: null });

    try {
      const playlist = await this.request(
        API + "/playlist?playlistId=" + encodeURIComponent(playlistId));
      if (generation !== this.playlistRequestGeneration) return;

      this.setState({ selectedPlaylist: playlist });
    } catch (error) {
      if (generation !== this.playlistRequestGeneration) return;
      this.setState({ selectedPlaylistId: previousPlaylistId });
      this.handleRequestError(error);
    }
  }

  async pollEvents(generation, after) {
    let sequence = after;
    while (generation === this.pollGeneration) {
      try {
        const result = await this.request(API + "/events?after=" + sequence + "&timeoutMs=30000");
        sequence = result.latestSequence;
        if (this.state.connectionState !== "online") {
          this.setState({ connectionState: "online" });
        }
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
        this.setState({
          error: error.message || String(error),
          connectionState:
            error?.code === "network_unreachable" ||
            (typeof error?.status === "number" && error.status >= 500)
              ? "reconnecting"
              : this.state.connectionState
        });
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

  seekPlayback(positionMs) {
    const playbackId = this.state.status?.playback?.nowPlaying?.playbackId;
    if (!playbackId) return Promise.reject(new Error("No playback is loaded."));
    return this.request(API + "/playback/seek", {
      method: "POST",
      body: JSON.stringify({ playbackId, positionMs: Math.round(positionMs) })
    });
  }

  setPlayMode(playMode) {
    return this.request(API + "/playback/play-mode", {
      method: "POST",
      body: JSON.stringify({ playMode })
    });
  }

  loadSong(song) {
    const playlistId = this.state.selectedPlaylist?.id;
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

  setSort(column) {
    if (this.state.sortColumn === column) {
      this.setState({ sortAscending: !this.state.sortAscending });
    } else {
      this.setState({ sortColumn: column, sortAscending: true });
    }
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
    const busy = !!this.state.busy;
    const currentPlaylist = status?.currentPlaylist;

    return h("main", { class: "page controller-page" },
      h(ErrorBanner, { message: this.state.error, onDismiss: () => this.setState({ error: null }) }),
      player.canPerform === false
        ? h("div", { class: "info-banner" },
            h("strong", null, "Performance unavailable."),
            h("span", null, " Switch to Bard to load or play songs."))
        : null,

      h(NowPlayingCard, {
        playback,
        ensemble,
        player,
        controls,
        currentPlaylist,
        busy,
        statusReceivedAt: this.state.statusReceivedAt,
        onPrevious: () => this.perform(
          "previous",
          () => this.playbackRequest(API + "/playback/previous")),
        onPlay: () => this.perform("play", () => this.playbackRequest(API + "/playback/play")),
        onPause: () => this.perform("pause", () => this.playbackRequest(API + "/playback/pause")),
        onStop: () => this.perform(
          "stop",
          () => this.playbackRequest(API + "/playback/stop"),
          { refreshSelected: true }),
        onNext: () => this.perform(
          "next",
          () => this.playbackRequest(API + "/playback/next")),
        onSeek: positionMs => this.perform(
          "seek",
          () => this.seekPlayback(positionMs)),
        onPlayMode: playMode => this.perform(
          "play-mode",
          () => this.setPlayMode(playMode)),
        onEnsemble: () => this.perform(
          "ensemble",
          () => this.playbackRequest(API + "/ensemble/ready-check"))
      }),

      h(EnsemblePanel, {
        playback,
        visualization: this.state.ensembleVisualization
      }),

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
                h("strong", null, "Active: "),
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
                      playlist.isCurrent
                        ? h("em", {
                            title: "Active MidiBard playback playlist"
                          }, "Active")
                        : null
                    ),
                    h("small", null,
                      playlist.songCount + " songs · " + formatTime(playlist.durationMs))
                  )
                )
              : h("p", { class: "empty" }, "No persisted playlists.")
          )
        ),

        h(PlaylistBrowser, {
          key: selectedPlaylist?.id ?? "no-playlist",
          playlist: selectedPlaylist,
          search: this.state.search,
          sortColumn: this.state.sortColumn,
          sortAscending: this.state.sortAscending,
          canLoad: controls.canLoad,
          busy,
          nowPlaying,
          onSearch: value => this.setState({ search: value }),
          onSort: column => this.setSort(column),
          onLoadSong: song => this.loadSong(song)
        })
      ),

      h("footer", null,
        h("span", null, "Loopback-only remote control"),
        h("button", { class: "link-button", onClick: () => this.disconnect() }, "Disconnect")
      )
    );
  }

  render() {
    return h("div", null,
      h(Nav, {
        connectionState: this.state.connected
          ? this.state.connectionState
          : "offline"
      }),
      this.state.connected ? this.renderController() : this.renderLogin()
    );
  }
}

const root = document.getElementById("app");
render(location.pathname.startsWith("/docs") ? h(ApiDocs) : h(RemoteController), root);
