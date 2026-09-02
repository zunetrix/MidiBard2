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
    playlist: [],
    search: "",
    error: null,
    busy: null,
    clock: Date.now()
  };

  apiToken = "";
  pollGeneration = 0;
  ticker = null;

  componentDidMount() {
    const savedToken = sessionStorage.getItem(TOKEN_KEY);
    if (savedToken) this.connect(savedToken);
  }

  componentWillUnmount() {
    this.pollGeneration++;
    if (this.ticker) clearInterval(this.ticker);
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
      const [status, playlist] = await Promise.all([
        this.rawRequest(token, API + "/status"),
        this.rawRequest(token, API + "/playlist")
      ]);
      this.apiToken = token;
      sessionStorage.setItem(TOKEN_KEY, token);
      this.setState({
        connected: true,
        tokenInput: "",
        status,
        statusReceivedAt: Date.now(),
        playlist: playlist.songs || [],
        busy: null
      });
      this.startTicker();
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
    if (this.ticker) {
      clearInterval(this.ticker);
      this.ticker = null;
    }
    this.setState({ connected: false, status: null, playlist: [], busy: null, error: message });
  }

  startTicker() {
    if (this.ticker) clearInterval(this.ticker);
    this.ticker = setInterval(() => this.setState({ clock: Date.now() }), 250);
  }

  async refreshStatus() {
    const status = await this.request(API + "/status");
    this.setState({ status, statusReceivedAt: Date.now() });
    return status;
  }

  async refreshPlaylist() {
    const playlist = await this.request(API + "/playlist");
    this.setState({ playlist: playlist.songs || [] });
  }

  async pollEvents(generation, after) {
    let sequence = after;
    while (generation === this.pollGeneration) {
      try {
        const result = await this.request(API + "/events?after=" + sequence + "&timeoutMs=30000");
        sequence = result.latestSequence;
        if (result.events?.length) await this.refreshStatus();
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

  async perform(name, action, refreshPlaylist = false) {
    this.setState({ busy: name, error: null });
    try {
      await action();
      await this.refreshStatus();
      if (refreshPlaylist) await this.refreshPlaylist();
      this.setState({ busy: null });
    } catch (error) {
      if (error.status === 401) {
        this.disconnect("The remote-control token is no longer valid.");
        return;
      }
      this.setState({ busy: null, error: error.message || String(error) });
    }
  }

  playbackRequest(path) {
    const playbackId = this.state.status?.playback?.nowPlaying?.playbackId;
    if (!playbackId) return Promise.reject(new Error("No playback is loaded."));
    return this.request(path, { method: "POST", body: JSON.stringify({ playbackId }) });
  }

  loadSong(fileName) {
    return this.perform("load", () => this.request(API + "/playback/load", {
      method: "POST",
      body: JSON.stringify({ fileName })
    }));
  }

  estimatedPosition() {
    const nowPlaying = this.state.status?.playback?.nowPlaying;
    if (!nowPlaying) return 0;
    const elapsed = this.state.status?.playback?.state === "playing"
      ? Math.max(0, this.state.clock - this.state.statusReceivedAt)
      : 0;
    return Math.min(nowPlaying.durationMs, Math.max(0, nowPlaying.positionMs + elapsed));
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
    const { status } = this.state;
    const playback = status?.playback;
    const nowPlaying = playback?.nowPlaying;
    const ensemble = status?.ensemble || {};
    const position = this.estimatedPosition();
    const progress = nowPlaying?.durationMs ? Math.min(100, position / nowPlaying.durationMs * 100) : 0;
    const canPlay = !!nowPlaying && ["ready", "paused", "completed"].includes(playback.state);
    const canPause = !!nowPlaying && playback.state === "playing";
    const canStop = !!nowPlaying && playback.state !== "idle";
    const canEnsemble = !!nowPlaying && playback.state === "ready" && ensemble.inParty &&
      ensemble.isPartyLeader && ensemble.monitoringEnabled && ensemble.syncClientsEnabled && !ensemble.running;
    const query = this.state.search.trim().toLowerCase();
    const songs = this.state.playlist.filter(song => !query || song.fileName.toLowerCase().includes(query));

    return h("main", { class: "page controller-page" },
      h(ErrorBanner, { message: this.state.error, onDismiss: () => this.setState({ error: null }) }),
      h("section", { class: "card now-playing" },
        h("div", { class: "section-heading" },
          h("div", null, h("p", { class: "eyebrow" }, "NOW PLAYING"), h("h1", null, nowPlaying?.fileName || "Nothing loaded")),
          h("span", { class: "state state-" + (playback?.state || "idle") }, playback?.state || "idle")
        ),
        h("div", { class: "progress-track", role: "progressbar", "aria-valuenow": Math.round(progress), "aria-valuemin": 0, "aria-valuemax": 100 },
          h("div", { class: "progress-fill", style: { width: progress + "%" } })
        ),
        h("div", { class: "time-row" },
          h("span", null, formatTime(position)),
          h("span", null, formatTime(nowPlaying?.durationMs || 0))
        ),
        h("div", { class: "controls" },
          h("button", { class: "primary", disabled: !canPlay || !!this.state.busy, onClick: () => this.perform("play", () => this.playbackRequest(API + "/playback/play")) }, "▶ Play Solo"),
          h("button", { disabled: !canPause || !!this.state.busy, onClick: () => this.perform("pause", () => this.playbackRequest(API + "/playback/pause")) }, "❚❚ Pause"),
          h("button", { disabled: !canStop || !!this.state.busy, onClick: () => this.perform("stop", () => this.playbackRequest(API + "/playback/stop")) }, "■ Stop"),
          h("button", { class: "ensemble-button", disabled: !canEnsemble || !!this.state.busy, onClick: () => this.perform("ensemble", () => this.playbackRequest(API + "/ensemble/ready-check")) }, "♪ Ensemble Ready Check")
        )
      ),
      h("div", { class: "dashboard-grid" },
        h("section", { class: "card ensemble-card" },
          h("div", { class: "section-heading" }, h("h2", null, "Ensemble"), h("span", { class: ensemble.running ? "state state-playing" : "state" }, ensemble.running ? "running" : "idle")),
          h("dl", null,
            h("div", null, h("dt", null, "In party"), h("dd", null, ensemble.inParty ? "Yes" : "No")),
            h("div", null, h("dt", null, "Party leader"), h("dd", null, ensemble.isPartyLeader ? "Yes" : "No")),
            h("div", null, h("dt", null, "Monitoring"), h("dd", null, ensemble.monitoringEnabled ? "Enabled" : "Disabled")),
            h("div", null, h("dt", null, "Client sync"), h("dd", null, ensemble.syncClientsEnabled ? "Enabled" : "Disabled"))
          )
        ),
        h("section", { class: "card playlist-card" },
          h("div", { class: "section-heading" },
            h("div", null, h("h2", null, "Playlist"), h("p", { class: "muted" }, this.state.playlist.length + " songs")),
            h("button", { class: "icon-button", title: "Refresh playlist", disabled: !!this.state.busy, onClick: () => this.perform("refresh", () => Promise.resolve(), true) }, "↻")
          ),
          h("input", {
            class: "search",
            type: "search",
            placeholder: "Search current playlist…",
            value: this.state.search,
            onInput: event => this.setState({ search: event.currentTarget.value })
          }),
          h("div", { class: "song-list" },
            songs.length ? songs.map((song, index) =>
              h("button", {
                class: "song " + (nowPlaying?.fileName === song.fileName ? "selected" : ""),
                key: song.fileName + ":" + index,
                disabled: !!this.state.busy || ensemble.running,
                onClick: () => this.loadSong(song.fileName)
              },
                h("span", null, song.fileName),
                h("small", null, nowPlaying?.fileName === song.fileName ? "Loaded" : "Load")
              )
            ) : h("p", { class: "empty" }, query ? "No songs match this search." : "The current playlist is empty.")
          )
        )
      ),
      h("footer", null,
        h("span", null, "Play mode: " + (playback?.playMode || "—")),
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
