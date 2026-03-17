# Web App Platform Roadmap

Challenges to address to make Automaton a reliable platform for web application creation — from hobbyist to professional.

## Design Principles

- **Server is a pure proxy.** User data never resides on Automaton infrastructure. Compute and storage follow the user.
- **Runner is the backend.** Everything that touches user data executes on user-controlled infrastructure (Docker container, on-premises server, or — in the future — mobile device).
- **AI generates the app.** Infrastructure complexity must be invisible to the app builder. Solutions belong in Automaton's platform layer, documented in prompts so the AI knows how to use them.

---

## High Priority

### 1. File Upload / Download
**Status:** In progress — see `docs/plans/file-upload-infrastructure.md`

Apps need to accept user-uploaded files (audio, images, documents) and return processed results. Cannot use base64 (SignalR message size limit). Cannot buffer files on the server (privacy constraint).

**Approach:** Chunked SignalR streaming. Browser → server (HTTP stream) → SignalR chunks → runner docker volume. Download is the symmetric reverse. Server holds at most one 256 KB chunk in memory at any moment.

---

### 2. State & Session Management
**Status:** Not started

HTTP is stateless. Between two `callController` calls there is no shared memory. Almost every real app needs to remember something — a logged-in user, a multi-step form, a processing job in progress.

**Approach (simplest first):**
- **SQLite on runner volume** — Python controller opens `sqlite3` to a path on `/data/`. Already works, just needs to be documented in prompts as the standard pattern.
- **In-memory dict in FastAPI** — fast but lost on restart. Suitable for ephemeral state (e.g. rate limiting).
- **Redis** — user provides connection string via project shared variables. For apps that need distributed or persistent session state.

Document the SQLite pattern in `app-base.md` so the AI uses it by default for any app that needs persistence.

---

### 3. Secret & Credential Management
**Status:** Not started

Generated apps frequently need API keys (OpenAI, Stripe, SendGrid, database passwords). Hardcoding secrets in Python controller code is wrong. There is currently no documented secure pattern.

**Approach:**
- **Project shared variables** already exist in Automaton and are available to flows. Extend them to be injected as environment variables into the runner's FastAPI process on deployment.
- **Runner `.env` file** — user manages it directly on their runner host; Python controller reads `os.environ['OPENAI_API_KEY']`.
- Document both patterns in `app-base.md`: AI always reads secrets from `os.environ`, never hardcodes them.

---

### 4. App-Level Authentication
**Status:** Not started

Automaton authenticates the *app owner* (Automaton login). It does not authenticate the *app's end users*. A multi-user app (CRM, personal tracker, booking system) needs its own user identity layer.

**Options by complexity:**
- **Protected app (Automaton login required)** — already exists. Suitable for internal tools used by people who have Automaton accounts.
- **Simple username/password in SQLite** — controller manages auth, returns a session token stored in `localStorage`. Good for hobbyist apps.
- **OAuth via third-party** — generate code that uses Google/GitHub login. Runner just verifies the token; no credentials stored in Automaton.
- **JWT issued by controller** — runner signs JWTs with a secret from `os.environ`. App sends token on every request.

Document a recommended pattern (SQLite + JWT) in `app-base.md` for apps that need multi-user access.

---

### 5. Observability — Log Viewer for App Builders
**Status:** Partial (execution logs exist, but no per-controller stdout streaming)

When a generated app fails, the builder sees a generic error. They cannot see `print()` output from their Python controller without runner access. This makes debugging AI-generated apps painful.

**Approach:**
- Stream controller stdout/stderr back through the tunnel response (add optional `logs` field to `TunnelControllerResponse`)
- Display logs inline in the Studio execution console
- Add a searchable per-app log history in Studio

---

## Medium Priority

### 6. Background & Scheduled Jobs
**Status:** Not started

Apps often need to act without a user trigger: send a reminder email at 9am, poll an external API hourly, clean up old files.

**Approach:**
- **APScheduler inside the FastAPI process** — Python controller registers scheduled tasks on startup. Already installable, just needs a documented pattern.
- **Flow scheduler** — Automaton flows already support scheduling. A flow can call a controller action. This is the cleaner solution as it uses existing infrastructure.

---

### 7. Concurrent Request Handling
**Status:** Not started

FastAPI handles async I/O concurrency well. CPU-bound work (ffmpeg, image processing, ML inference) blocks the event loop and serialises concurrent requests.

**Approach:**
- **ProcessPoolExecutor in Python** — offload CPU-bound work to a separate process. Document as the standard pattern for heavy processing controllers.
- **Multiple runner instances** — runner selection logic already supports multiple runners per user. Document horizontal scaling for high-traffic apps.
- Cap concurrent tunnel requests per runner to prevent overload; return 503 with retry hint when exceeded.

---

### 8. Runner Resilience & Offline Handling
**Status:** Partial (timeout exists, no graceful degradation)

The runner is on user premises. It will go offline. Today the tunnel times out and the browser gets an error with no context.

**Approach:**
- Expose runner status to `automatonApi`: `automatonApi.getRunnerStatus()` — app can show "service temporarily unavailable" gracefully.
- Retry logic with exponential backoff for transient failures (already partially exists).
- Read-only fallback mode: app detects offline runner and switches to a cached/static view.

---

### 9. App Versioning & Rollback
**Status:** Not started

When the AI regenerates an app, the previous version is lost. There is no undo for a bad generation.

**Approach:**
- Store previous generated file snapshots with timestamps (soft-delete, not hard overwrite).
- One-click rollback in Studio to any previous version.
- Show a diff between current and previous version before applying a regeneration.

---

### 10. Custom Domains
**Status:** Not started

Apps are currently served at `/web/{slug}`. No business wants their app at `automaton.io/web/my-crm`.

**Approach:**
- Custom domain support: user adds a CNAME pointing to the Automaton server; server routes by `Host` header to the correct app.
- Or: static export + self-hosting — publish the app's frontend as a static bundle the user deploys to Vercel, Netlify, or their own server. The runner URL is baked into the bundle at export time.

---

## Low Priority

### 11. Real-Time Events Inside Apps (Push / WebSocket)
**Status:** Not started

Generated apps can call controllers but cannot receive server-initiated events. Long-running jobs require polling.

**Approach:**
- Expose a simple event subscription to `automatonApi`: `automatonApi.onEvent(eventName, handler)`. Runner publishes events via a new hub method; server fans them out to the subscribed browser.
- Server-Sent Events (SSE) as a simpler alternative for one-way push from server to browser.

---

## Future Vision: Mobile Runner

The chunked streaming infrastructure (file upload/download plan) is the prerequisite for a mobile runner. A runner on Android or iOS would connect to the server via the same outbound SignalR connection the Docker runner uses today. This enables:

- **Personal photo/video processing** — files never leave the phone
- **On-device health data analysis** — medical CSVs processed locally, zero cloud exposure
- **On-device AI inference** — local LLMs (Mistral 7B, Phi-3) run on the device; zero API costs
- **Offline-first apps** — runner works without internet; server is only needed for the relay
- **Journalist / legal secure processing** — sensitive documents handled on an air-gapped device

The compute and storage follow the user — not the platform. This is the differentiating position no cloud-first app builder can credibly match.

---

## Legal & Privacy

Automaton is a **data processor** under GDPR. The server relays data; it does not own or control it. This requires:

- A Data Processing Agreement (DPA) in the terms of service
- No logging of request payloads
- Clear documentation: *"data passes through Automaton servers in transit only — nothing is stored, logged, or retained"*

**Long-term:** End-to-end encryption between browser and runner. If payloads are encrypted with a key only the runner holds, the server is architecturally incapable of reading user data — a technical guarantee, not a policy promise. Critical for regulated industries (healthcare, legal, finance).
