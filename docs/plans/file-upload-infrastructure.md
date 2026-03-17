# File Upload / Download Infrastructure

## Problem

Automaton apps currently have no way to accept user-uploaded files. Non-technical users cannot upload a large audio/video/document file for processing, and there is no way to stream large result files back to the browser without buffering them on the server.

Technical users can provide URLs (S3, Google Drive direct link, etc.) but there is no supported pattern documented in the prompts for this either.

Both scenarios must work.

## User Segments

**Professionals** (developers, technical teams) already have a complete solution today: pass an S3, GDrive, or any CDN URL as a plain string input to `callController`. The runner downloads the file directly. Automaton never touches it. No new infrastructure needed.

**Hobbyists and non-technical users** don't want to configure S3. They want to upload a file from their browser and get something back. The chunked streaming architecture built here is completely invisible to them — they just see an app that works.

## Privacy Constraint

**The Automaton server is a pure proxy. Files must only ever reside on the runner's storage.**

The server never writes bytes to disk. Bytes pass through server memory only while being forwarded chunk-by-chunk, never accumulated. This is consistent with Automaton's on-premises design principle: runners execute on user infrastructure, user data never leaves that infrastructure.

The runner's docker volume is the user's storage layer — it just isn't directly reachable from the browser. The SignalR connection (runner → server) makes the server a transparent bidirectional pipe for file I/O, the same way it already proxies JSON execution today.

## Why This Architecture Is Worth the Complexity

The chunked SignalR streaming design is more work than a simple server-side cache. The reason it's worth it is that the same mechanism works regardless of where the runner lives:

- A Docker container on a laptop
- A VPS in a data centre
- A Raspberry Pi on a local network
- A mobile phone

**The compute and storage follow the user, not the platform.** This is a fundamentally different and more defensible position than any cloud-first app builder.

### Mobile Runner Scenarios

The architecture described in this plan is the prerequisite for a mobile runner. Once a runner can run on Android or iOS (connecting outbound to the server the same way the Docker runner does today), the following become possible:

**1. Personal photo processing**
A user builds an app that resizes, watermarks, or converts photos. They run the runner on their phone. The photos never leave their device — processed locally, result streamed back to the browser via the proxy. No cloud storage, no privacy policy to read.

**2. Medical / health data**
A hobbyist builds an app that analyses a blood glucose CSV export or processes a scanned lab result. Sensitive health data stays on their phone. The Automaton server sees only the bytes in transit for milliseconds, encrypted in TLS.

**3. Offline-first home automation**
Someone builds a local smart home dashboard. The runner on their home server (or phone) executes automations. The browser app works even if the Automaton cloud is unreachable — the runner only needs the server to relay, not to store anything.

**4. Journalist / lawyer secure document processing**
A journalist uploads a leaked document for OCR and redaction. The runner on their air-gapped laptop processes it. The document never touches any third-party infrastructure. Automaton is just the relay.

**5. On-device AI inference**
As on-device LLMs become practical (Mistral 7B already runs on modern phones), a mobile runner could execute AI steps locally. A user builds a "summarise my notes" app where the model runs on their phone. Zero API costs, zero data sent to OpenAI.

These scenarios are not hypothetical edge cases — they describe a category of app that no cloud-first platform can credibly offer. The file transfer infrastructure in this plan is the foundation that makes all of them possible.

## Legal & Privacy Considerations

### Automaton as a data processor

Under GDPR and equivalent frameworks, Automaton is a **data processor** — it handles data on behalf of the user (the data controller), not for its own purposes. This is the same legal position as AWS, Cloudflare, or any CDN. It is not inherently problematic, but it requires:

1. A **Data Processing Agreement (DPA)** in the terms of service
2. Processing data only for the purpose of delivering the service (relaying bytes, nothing else)
3. Not retaining, analysing, or sharing data

### Does "no logging" fix it?

Partially. Committing to not logging payload data is a good practice and reduces risk, but it is a **policy commitment** — hard to verify externally and possible to break accidentally. It does not eliminate the concern because:

- TLS termination happens on the Automaton server — the server *can* read plaintext even if it doesn't
- In-transit data still counts as "processing" under GDPR; the question is whether it is *lawful* processing
- Infrastructure jurisdiction matters — US-hosted servers serving EU users require additional legal basis (SCCs, etc.)

**Immediate actions:**
- Add a DPA to the terms of service
- Document clearly: *"data passes through Automaton servers in transit only — nothing is stored, logged, or retained"*
- Ensure request payloads are never written to application logs

### Future: end-to-end encryption (E2E)

Not logging is a promise. E2E encryption is a technical guarantee.

If the payload is encrypted with a key that only the runner holds, the Automaton server becomes a **blind relay** — it physically cannot read the data even under legal compulsion. TLS protects browser↔server; E2E encryption extends that protection all the way through the server to the runner.

This is a significant investment but the right long-term direction, especially as Automaton gains traction in regulated industries (healthcare, legal, finance). A server that is architecturally incapable of reading user data is a stronger selling point than one that promises not to.

E2E encryption between browser and runner should be tracked as a future milestone, not deferred indefinitely.



### URL input (technical users)

The user provides a URL (S3, Google Drive direct link, etc.) as a plain string input to `callController`. The Python controller downloads it using `requests.get(url)`. No new infrastructure needed — this pattern already works.

### Direct upload — chunked SignalR streaming (browser → runner)

The runner connects to the server via outbound SignalR. The browser cannot reach the runner directly. The server reads the upload stream chunk by chunk and immediately forwards each chunk to the runner over SignalR. The server holds at most one chunk in memory at any moment.

```
Browser  →  POST /api/web-apps/{appId|slug}/files/upload  (multipart streaming)
            Server reads 256 KB chunk
            → SignalR "ReceiveFileChunk" → Runner appends chunk to /tmp/uploads/{sessionId}/{filename}
            ... repeat until last chunk ...
            Runner sends "FileUploadComplete" → Server hub method resolves TCS
         ←  { session_id: "abc123" }     ← file lives ONLY on runner docker volume

Browser  →  callController('converter', 'convert', { upload_session: 'abc123' })
            (normal tunnel — tiny JSON string, same as today)

Runner   →  reads /tmp/uploads/abc123/{filename} directly from its local filesystem
         →  processes with ffmpeg / pandas / etc.
         →  writes result to /tmp/results/{sessionId}/{outputFilename}
         →  returns { download_session: "xyz789" } via tunnel response
```

### Direct download — chunked SignalR streaming (runner → browser)

The current mechanism has the runner POST the result file to `/api/runner/files` where it is **buffered in server memory for up to 15 minutes**. This violates the proxy principle for any non-trivial file.

The clean approach: the browser requests a download by `session_id`, the server sends a `FetchFileChunk` request to the runner via SignalR, the runner reads the file from its volume and streams chunks back, the server pipes each chunk directly into the HTTP response — never accumulating the file.

```
Browser  →  GET /api/web-apps/{appId|slug}/files/{session_id}
            Server registers a FetchFileSession TCS
            → SignalR "FetchFileChunk" (offset, chunkSize) → Runner reads from /tmp/results/{sessionId}/
            Runner → SignalR "FileChunkReady" (data, isLast) → Server
            Server pipes chunk bytes directly to Response.Body (chunked transfer encoding)
            ... repeat until isLast ...
         ←  streamed file download (no server-side accumulation)
```

### Summary: symmetric proxy

```
UPLOAD:   Browser → Server (HTTP stream) → SignalR chunks → Runner volume
DOWNLOAD: Browser → Server (HTTP GET)    → SignalR request → Runner streams back → Server pipes to HTTP response
```

The server is a stateless pipe in both directions. No caches, no temp files, no TTLs.

## Implementation Plan

### Phase 1 — Core Models

- [ ] Add `FileUploadChunk` to `Automaton.Core/Models/`
  - `SessionId` (string), `ChunkIndex` (int), `Data` (byte[]), `IsLast` (bool), `FileName` (string), `ContentType` (string)
- [ ] Add `FileUploadCompleteMessage` to `Automaton.Core/Models/`
  - `SessionId` (string), `LocalPath` (string) — absolute path where runner stored the file on its volume
- [ ] Add `FetchFileChunkRequest` to `Automaton.Core/Models/`
  - `SessionId` (string), `Offset` (long), `ChunkSize` (int)
- [ ] Add `FileChunkReadyMessage` to `Automaton.Core/Models/`
  - `SessionId` (string), `Data` (byte[]), `IsLast` (bool), `FileName` (string), `ContentType` (string)
- [ ] Add hub method constants to `AutomatonHubMethods.cs`
  - `ReceiveFileChunk` (server → runner, upload)
  - `FileUploadComplete` (runner → server, upload ACK)
  - `FetchFileChunk` (server → runner, download request)
  - `FileChunkReady` (runner → server, download data)

### Phase 2 — Server: Registries + Hub Handlers

- [ ] Add `FileUploadRegistry.cs` to server `Services/` — mirrors `TunnelRequestRegistry`
  - Maps `sessionId` → `TaskCompletionSource<FileUploadCompleteMessage>`
  - 5-minute timeout
- [ ] Add `FileDownloadRegistry.cs` to server `Services/`
  - Maps `sessionId` → `Channel<FileChunkReadyMessage>` (async producer/consumer)
  - Server writes received chunks into channel; download endpoint reads and pipes to response
- [ ] Add hub methods to `AutomatonHub.cs`:
  - `FileUploadComplete(FileUploadCompleteMessage msg)` → resolves upload TCS
  - `FileChunkReady(FileChunkReadyMessage msg)` → writes chunk into download channel
- [ ] Register both registries as singletons in `Program.cs`

### Phase 3 — Server: Upload Endpoint

- [ ] Add upload routes to `AppFilesController.cs`:
  - `POST /api/web-apps/preview/{appId}/files/upload` — requires JWT
  - `POST /api/web-apps/{slug}/files/upload` — same auth as published controller execution
- [ ] Implementation:
  - Resolve runner connection (same logic as `AppsControllerExecutionService`)
  - Generate `sessionId`, register in `FileUploadRegistry`
  - Read `IFormFile` stream in 256 KB chunks; send each as `ReceiveFileChunk` via `IHubContext<AutomatonHub>`
  - Set `IsLast = true` on final chunk
  - Await `FileUploadRegistry` task (runner ACK)
  - Return `{ session_id }` — no bytes stored on server

### Phase 4 — Server: Download Endpoint

- [ ] Add download route to `AppFilesController.cs`:
  - `GET /api/web-apps/preview/{appId}/files/{sessionId}` — requires JWT
  - `GET /api/web-apps/{slug}/files/{sessionId}` — same auth as published controller execution
- [ ] Implementation:
  - Register `sessionId` in `FileDownloadRegistry` (creates a channel)
  - Send `FetchFileChunk` to runner (offset=0, chunkSize=256KB) via SignalR
  - Read chunks from the channel; pipe each `Data` directly to `Response.Body`
  - After each chunk: if `!IsLast`, send next `FetchFileChunk` with incremented offset
  - Set `Content-Type` and `Content-Disposition` from first chunk metadata
  - Complete response when `IsLast = true`
- [ ] Remove the old `POST /api/runner/files` + memory cache mechanism (or keep as opt-in fallback for small files)

### Phase 5 — Runner: Upload Handler

- [ ] Add `ReceiveFileChunk` handler to `HubService.cs`:
  - On first chunk: create `/tmp/uploads/{sessionId}/` dir, open file for writing
  - Append `Data` bytes
  - When `IsLast = true`: close file, invoke `FileUploadComplete` hub method with `LocalPath`
- [ ] Runner startup: purge `/tmp/uploads/` dirs older than 1 hour

### Phase 6 — Runner: Download Handler

- [ ] Add `FetchFileChunk` handler to `HubService.cs`:
  - Open file at `LocalPath` derived from `sessionId` (e.g. `/tmp/results/{sessionId}/`)
  - Seek to `Offset`, read `ChunkSize` bytes
  - Invoke `FileChunkReady` hub method with data, `IsLast = (bytesRead < ChunkSize)`
- [ ] Runner: controller writes result to `/tmp/results/{sessionId}/{outputFilename}` and returns `{ download_session: sessionId }` instead of POSTing to server

### Phase 7 — Frontend API

- [ ] Add `uploadFile(file, { runnerNames } = {})` to `automaton-api-preview.js`
  - `POST /api/web-apps/preview/${APP_ID}/files/upload` as `FormData`
  - Returns `{ session_id }` or throws on error
- [ ] Add `uploadFile(file, { runnerNames } = {})` to `automaton-api-published.js`
  - `POST /api/web-apps/${APP_SLUG}/files/upload` as `FormData`
  - Returns `{ session_id }` or throws on error
- [ ] Add `getDownloadUrl(sessionId)` to both — returns the URL to use in an `<a href>` or `window.location`
- [ ] Expose both on `window.automatonApi`

### Phase 8 — Prompts

- [ ] Update `app-base.md`:
  - `UPLOADING FILES`: `automatonApi.uploadFile(file)` → `{ session_id }`, pass to `callController`
  - `DOWNLOADING RESULTS`: controller returns `{ download_session: id }`, browser uses `automatonApi.getDownloadUrl(id)`
  - HTML file input pattern
  - Python controller reads from local path in inputs, writes result to `/tmp/results/{session_id}/`
- [ ] Update `PlanMode.cs`: server is pure proxy, use upload/download session pattern, never base64

### Phase 9 — Validation

- [ ] Build server + runner
- [ ] End-to-end: upload WAV → runner converts → download MP3
- [ ] Confirm server never writes to disk and memory footprint stays at ~256 KB during transfer
- [ ] Confirm runner `/tmp/uploads/` and `/tmp/results/` are populated and cleaned up

## Technical Notes

| Concern | Decision |
|---------|----------|
| **Chunk size** | 256 KB — well under SignalR's 1 MB default message limit |
| **Server memory (upload)** | One 256 KB chunk at a time — O(256 KB) regardless of file size |
| **Server memory (download)** | Chunk channel holds at most a few chunks in-flight; response streams directly |
| **Runner storage** | `/tmp/uploads/{sessionId}/{filename}` (input), `/tmp/results/{sessionId}/{filename}` (output) on docker volume |
| **Runner cleanup** | Delete session dirs after use; purge stale dirs on startup (> 1 hour old) |
| **Runner selection** | First connected runner for the app — same logic as `AppsControllerExecutionService` |
| **Auth (anonymous apps)** | Same approach as existing anonymous controller execution endpoint |
| **URL pattern** | Still works unchanged — pass URL string in inputs, runner uses `requests.get(url)` |
| **Old result POST mechanism** | `POST /api/runner/files` + memory cache can be removed or kept as fallback for very small files (< 1 MB) |
| **Backward compatibility** | Existing apps using `download_url` continue to work until migrated |

## Files to Change

| File | Change |
|------|--------|
| `Automaton.Core/Models/` | +`FileUploadChunk`, `FileUploadCompleteMessage`, `FetchFileChunkRequest`, `FileChunkReadyMessage` |
| `AutomatonHubMethods.cs` | +4 new method constants |
| `AutomatonHub.cs` | +`FileUploadComplete`, `FileChunkReady` hub methods |
| `Services/FileUploadRegistry.cs` | New — TCS registry for upload sessions |
| `Services/FileDownloadRegistry.cs` | New — Channel registry for download sessions |
| `Program.cs` | Register both registries as singletons |
| `AppFilesController.cs` | +upload endpoints (preview + published), +download endpoints (preview + published) |
| `Automaton.Runner/HubService.cs` | +`ReceiveFileChunk` handler, +`FetchFileChunk` handler |
| `automaton-api-preview.js` | +`uploadFile`, +`getDownloadUrl` |
| `automaton-api-published.js` | +`uploadFile`, +`getDownloadUrl` |
| `app-base.md` | +`UPLOADING FILES`, +`DOWNLOADING RESULTS` sections |
| `PlanMode.cs` | +file proxy design principle |
