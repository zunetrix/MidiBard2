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

class NowPlayingCard extends Component {
  state = { clock: Date.now() };
  ticker = null;

  componentDidMount() {
    this.ticker = setInterval(() => this.setState({ clock: Date.now() }), 250);
  }

  componentWillUnmount() {
    if (this.ticker) clearInterval(this.ticker);
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
        busy,
        statusReceivedAt: this.state.statusReceivedAt,
        onPlay: () => this.perform("play", () => this.playbackRequest(API + "/playback/play")),
        onPause: () => this.perform("pause", () => this.playbackRequest(API + "/playback/pause")),
        onStop: () => this.perform(
          "stop",
          () => this.playbackRequest(API + "/playback/stop"),
          { refreshSelected: true }),
        onEnsemble: () => this.perform(
          "ensemble",
          () => this.playbackRequest(API + "/ensemble/ready-check"))
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
        h("span", null,
          "Play mode: " + (playback.playMode || "—") +
          " · Active playlist: " + (currentPlaylist?.name || "—")),
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
