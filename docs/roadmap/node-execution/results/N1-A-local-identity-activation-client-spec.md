# N1-A durable local identity and activation client specification

**Status:** prepared for review against merged `main` at `059827477d4cd79f76bd5c482347e9607b14a4f3` on 2026-10-13. Design only; no runtime, schema, credential, host, broker, Docker, workload, or production state changed. This specification refines the reviewed [activation/recovery contract](N1-A-activation-recovery-spec.md) for the next bounded local-state/client slice. It does not authorize enrollment, operational activation, recovery effects, broker consumption, workload admission, or rollout.

## Current boundary and source owners

Current `src/Automaton.Node` has the preparation host and a Linux x64 host-wide `flock` implementation. Successful lock acquisition sets process-local `RecoveryRequired`; it does not read identity, journal, restore authority, or activation state. Node has no HTTP client, credentials, activation coordinator, or recovery implementation. Its capabilities remain unknown and `CanAdmitWorkloads` remains false.

The API already implements `POST /api/node/v1/activation` in `src/Automaton.Api/Extensions/NodeActivationApiExtensions.cs`. `NodeActivationService` binds the authenticated enrollment identity, checks the configured epoch authority/HFA trust, applies incarnation and epoch CAS, and returns the immutable stored `NodeActivationReceipt`. The existing receipt schema is in `NodeActivationContracts.cs`; durable management/attempt/HFA-use rows and migration live in Infrastructure. There is no read-only endpoint to recover the current activation receipt after a Node process dies between API commit and local receipt persistence.

The normative contracts remain [N0-B](N0-B-contracts.md), [N0-D](N0-D-architecture-and-acceptance.md), and the reviewed [activation/recovery specification](N1-A-activation-recovery-spec.md). The [local startup-lock result](N1-A-node-local-startup-lock.md) owns lock validation and host-wide manager exclusivity. mTLS/API-key remains a proposed authentication selection pending independent security approval; this document does not approve it or enable its production wiring.

## Bounded outcome and exclusions

Implement a protected local identity/checkpoint store and a fail-closed client for the existing activation contract, plus a read-only API readback contract for activation recovery. The Node consumes an already verified enrollment credential bundle through a narrow identity-install boundary; it does not implement enrollment, challenge issuance, credential rotation, or service installation. Test composition may seed synthetic identities and authority evidence.

At this slice's end, every successful startup still reports local `RecoveryRequired`. A validated `Recovering` API receipt is durable evidence, not authenticated `RecoveryOnly` completion and never an `Active` grant. Recovery reconciliation, external-effect settlement, privileged adapters, workload admission, broker subscriptions, Docker access, executor/workflow code, and host effects remain absent. Production activation remains disabled without the separately reviewed authority and authentication configuration.

## Protected local files and identity contract

All files are beneath the configured host-state directory already held open and validated by the N1-A installation lock. The directory is root-owned mode `0700`; every component from `/` is traversed through held directory handles. Files are root-owned regular files with exact mode `0600`, link count one, no symlink following, and opened relative to the validated directory handle. Unknown versions, duplicate/unknown JSON properties, malformed fields, owner/mode/link mismatch, symlink, truncation, digest mismatch, or missing required state fail closed. An unkeyed digest detects accidental corruption; it is not treated as authenticity against host root. The OS root account and kernel remain in the trusted computing base.

| File | Contents and mutation rule |
| --- | --- |
| `node-identity.json` | Versioned immutable binding: `ControlPlaneRealmId`, `NodeId`, `InstallationId`, `CredentialRevision`, exact Node leaf-certificate DER SHA-256, credential-set identifier, enrollment-receipt digest, and relative names of credential files. It contains no private key or API key. Initial creation is from an authenticated, verified enrollment bundle. Existing identity is never silently replaced; an exact same-binding replay is idempotent, while a different binding requires the separately reviewed fenced reenrollment/rotation process. |
| `node-client-cert.der` | Exact per-installation self-signed P-256 leaf certificate, whose DER fingerprint matches the pinned identity manifest. Public material, but kept in the protected credential set so key/certificate rotation cannot be observed half-applied. |
| `node-client-key.pem` | Per-installation P-256 private key corresponding to the pinned certificate. Root-only; never journaled or included in diagnostic output. The authentication review says Node keys are not copied to the control plane or ordinary backups. |
| `node-api-key` | Node-bound API key from the verified enrollment bundle. Root-only; never copied to logs, activation requests, checkpoints, or exception text. |
| `activation-checkpoint.json` | Versioned, replace-atomically snapshot of the pinned realm/epoch watermark, latest known positive incarnation (or null before initial allocation), latest exact activation receipt bytes/digest, activation request intent for the current process start, and any unresolved prior request needed for reconciliation. It contains no credential secret. |

Identity manifest v1 has a strict schema and canonical GUIDs/digests. Before commit, the installer boundary validates that the enrollment receipt is authentic under the reviewed API trust path, that Node/Installation/realm/credential revision match the certificate and API-key binding, and that certificate public key matches the private key. The verified bundle and manifest are written as one logical install: write client key, certificate and API-key files first; fsync each; atomically publish the identity manifest last; fsync the directory. A crash before manifest publication leaves no usable identity. A preexisting different identity is quarantined; no file overwrite turns it into a new Node. Credential rotation is a separate slice and may not use an in-place partial update.

An illustrative identity manifest is:

```json
{
  "schemaVersion": 1,
  "controlPlaneRealmId": "40000000-0000-0000-0000-000000000001",
  "nodeId": "30000000-0000-0000-0000-000000000001",
  "installationId": "60000000-0000-0000-0000-000000000001",
  "credentialRevision": "2",
  "nodeCertificateDerSha256": "<64 lowercase hex characters>",
  "credentialSetId": "80000000-0000-0000-0000-000000000001",
  "enrollmentReceiptSha256": "<64 lowercase hex characters>",
  "clientKeyFile": "node-client-key.pem",
  "apiKeyFile": "node-api-key"
}
```

The checkpoint is one canonical v1 snapshot with a monotonically increasing local `checkpointSequence`, a `checkpointPayloadSha256` over the canonical snapshot with that digest field omitted, exact receipt/request bytes encoded as Base64, and two intent slots: `unresolvedPriorIntent` (at most one request sent by the previous process whose outcome is unknown) and `currentProcessIntent` (the one request ID allocated to this process start, whether or not it has been sent). Each intent binds request bytes/digest, ID, process-start ID, expected incarnation, epoch, and `Prepared`/`ResponseUnknown`/`ReceiptPersisted` state. It also records `lastKnownRecoveryEpoch`, `currentIncarnation`, `lastActivationRequestId`, and `lastActivationReceiptSha256`. Do not send the current process intent until the prior intent is reconciled or the CAS/readback protocol below safely resolves a race. No bounded-history pruning may delete unresolved intent.

The API request's `journalInventorySha256` is the SHA-256 of exact canonical v1 journal-inventory bytes. Its `recoveryCheckpointSha256` is the SHA-256 of a separate canonical `RecoveryView` document containing the Node/Installation/realm/epoch, current known incarnation, journal-inventory digest, unresolved-operation-set digest and current recovery/fence evidence digest. `RecoveryView` excludes process-start IDs, activation request/receipt bytes, retry state and its own digest, so a request can bind its digest without a self-reference. The local checkpoint stores the exact `RecoveryView` bytes and recomputes both hashes on every read. `checkpointPayloadSha256` detects accidental changes to the mutable local snapshot; it is distinct from the API-bound recovery-view digest and is not treated as an authenticity proof. No journal, recovery-view or checkpoint digest is synthesized as empty after missing/corrupt state, except the explicit fresh-enrollment genesis below.

`RecoveryView` v1 uses strict camelCase UTF-8 JSON, fields emitted exactly in this order, no insignificant whitespace, canonical `D` GUIDs, Int64 values as decimal strings, and explicit null only for the initial `currentIncarnation`:

```json
{"schemaVersion":1,"controlPlaneRealmId":"40000000-0000-0000-0000-000000000001","nodeId":"30000000-0000-0000-0000-000000000001","installationId":"60000000-0000-0000-0000-000000000001","recoveryEpoch":"3","currentIncarnation":null,"journalInventorySha256":"<64 lowercase hex characters>","unresolvedOperationSetSha256":"<64 lowercase hex characters>","recoveryEvidenceSha256":"<64 lowercase hex characters>"}
```

For genesis only, `journalInventorySha256` and `unresolvedOperationSetSha256` are each the SHA-256 of the exact canonical empty-set bytes `{"schemaVersion":1,"entries":[]}`. `recoveryEvidenceSha256` is the SHA-256 of a canonical ordered v1 `FreshEnrollmentGenesis` evidence object containing the verified enrollment-receipt digest, current external-register-attestation digest and authenticated `NoActivation` response digest. This binds the initial empty view to affirmative fresh-enrollment evidence rather than an assumption. Later nonempty recovery views use the exact journal, unresolved-operation and effect/fence evidence digests defined by the owning recovery slice; this implementation does not invent them.

The only permitted genesis is a fresh enrollment before any activation: validate the exact enrollment receipt and identity binding, obtain a fresh committed external-register attestation, and authenticate the current-activation readback as `NoActivation` for the same Node/Installation/realm. Then write a v1 genesis checkpoint with null incarnation, the attested epoch, no prior request/receipt, and the digest of the canonical empty journal inventory `{"schemaVersion":1,"entries":[]}`. This is an explicit initialization event, not a fallback for missing/corrupt files. Once any activation intent exists or the API reports any allocated incarnation, a missing checkpoint cannot be recreated as genesis. A later loss or rollback of state remains `RecoveryRequired` until reviewed recovery restores/reconciles it.

## Atomic write and crash-recovery protocol

Every checkpoint update runs under the host installation lock and uses the same validated directory handle as the lock implementation:

1. Serialize the complete strict v1 document to a uniquely named same-directory temporary file created with exclusive/no-follow flags and mode `0600`.
2. Write all bytes, flush the file with `fsync`, and validate its open-handle owner, type, mode and single-link metadata.
3. Atomically rename the temporary entry over the checkpoint with same-filesystem rename semantics; never truncate the live file in place.
4. `fsync` the containing directory before reporting the write durable or sending a request that depends on it.
5. On any error, retain the old checkpoint if the rename did not occur, keep startup `RecoveryRequired`, and do not send/advance. If rename may have occurred but directory fsync failed, the outcome is uncertain: reread and strictly validate under the lock, reconcile with the API, and remain `RecoveryRequired` until resolved. Never restore a previous file solely because its sequence is lower.

Crash behavior is intentionally conservative:

| Crash point | Required restart observation |
| --- | --- |
| Before temporary file complete/fsynced | Old checkpoint remains authoritative; temporary file is ignored/quarantined and no dependent request was sent. |
| After file fsync, before rename | Old checkpoint remains authoritative; no dependent request was sent. |
| After rename, before directory fsync | Either complete old or complete new snapshot may be visible. Revalidate and reconcile against the authenticated API; no torn JSON is accepted. |
| After durable request intent, before/during/after HTTP commit | Checkpoint retains exact request bytes and ID. New process obtains authenticated current activation readback before forming a new request; conflict/unknown state stays `RecoveryRequired`. |
| After valid receipt, before checkpoint fsync | Previous durable intent remains. Readback must return the immutable receipt and bind it to the stored intent before checkpointing it. |
| After receipt checkpoint fsync | Revalidate its digest and identity/epoch binding on restart. It still does not imply `RecoveryOnly` or `Active`. |

No state file, lock file, credential, or recovery evidence is automatically deleted on failure or rollback. Orphan temporary credentials are not automatically swept until a separately tested secret-cleanup policy exists.

## Process-start request identity and API client

After lock acquisition, a usable protected checkpoint, and identity parsing, each process start creates exactly one random `ProcessStartId` and one random `ActivationRequestId` and durably records them before any activation HTTP request. If required identity, authority or checkpoint validation then fails, the ID remains unused and the host stays `RecoveryRequired`; no request is sent. The request body is canonical v1 JSON matching the existing `NodeActivationRequest`; its exact bytes and SHA-256 are stored in `currentProcessIntent` before transport. Every transport retry for that request uses the exact same ID and bytes. A retry never regenerates a GUID, changes the checkpoint digest, refreshes the HFA envelope, or changes the expected incarnation. Each later process start gets a distinct ID, while any prior unresolved request remains separately recorded and must be read back/reconciled before a new attempt can advance state. It does not claim that a lost response was a failed transaction.

The client POSTs only to the configured dedicated Node-management API origin and existing `POST /api/node/v1/activation` route. Request fields are those already defined by the API: schema 1; locally pinned realm/Node/Installation IDs; current committed positive recovery epoch; per-start request GUID; expected incarnation (`"0"` only for the initial enrolled/null-incarnation case, otherwise the positive expected current incarnation); credential revision; lowercase SHA-256 journal-inventory and recovery-checkpoint digests; supported schema versions; and HFA receipt only when a separately reviewed authority supplies one. Canonical body limits and the HFA envelope rules remain the existing API contract. The API key is a single `X-API-Key` header; TLS peer verification and certificate proof follow the reviewed authentication decision. There is no bearer-only, ordinary-listener, bootstrap-listener, alternate-host, or insecure TLS fallback.

Before POST, the client obtains the current committed realm/epoch from the N0-D external restore-register authority verifier and checks it against identity and checkpoint watermarks. An API DB row, local file, cached register copy, Node-supplied number, wall clock, or signed-but-stale register snapshot is not the authority. Because the production register reader and its trust distribution are not implemented/reviewed, the production client composition is unavailable and cannot send activation requests. A synthetic authority is allowed only in isolated tests.

### Lost-response readback contract required for restart

Add a read-only, authenticated, management-listener-only `GET /api/node/v1/activation/current` operation. It returns a bounded strict response containing the current Node/Installation/realm/credential revision, committed recovery epoch when a management row exists, lifecycle status, current incarnation, current activation request ID, canonical request digest, and the exact immutable activation receipt bytes. It performs no CAS, HFA consumption, allocation, or mutation. The route must be rejected before handler invocation on ordinary API/bootstrap listeners. API authorization rechecks the committed, nonrevoked Node binding. Unknown or inconsistent rows are conflict/unavailable, never an empty-initial-state response. `NoActivation` is returned only for committed, nonrevoked enrollment with no `NodeManagement` row or activation attempt and only when API startup has verified its database realm/epoch against the current external register; otherwise readback is unavailable.

The response is one canonical JSON object (maximum 64 KiB), with all listed fields present; nullable values are explicit `null` only for the initial `Enrolled`/null-incarnation state. The raw receipt is Base64 so the client can validate and persist the exact bytes returned by the existing immutable attempt:

```json
{
  "schemaVersion": 1,
  "controlPlaneRealmId": "40000000-0000-0000-0000-000000000001",
  "nodeId": "30000000-0000-0000-0000-000000000001",
  "installationId": "60000000-0000-0000-0000-000000000001",
  "credentialRevision": "2",
  "recoveryEpoch": "3",
  "status": "Recovering",
  "currentIncarnation": "1",
  "currentActivationRequestId": "70000000-0000-0000-0000-000000000001",
  "requestSha256": "<64 lowercase hex characters>",
  "activationReceiptJsonBase64": "<base64 of exact immutable receipt JSON bytes>"
}
```

If a committed enrollment has no `NodeManagement` row or activation-attempt history yet, return the explicit `NoActivation` state with `recoveryEpoch`, incarnation, request ID, digest and receipt all null. This is read-only and does not create the management row. The Node must still validate the external current epoch before POST. If a persisted `Enrolled` row is returned, recovery epoch is positive and incarnation, request ID, digest and receipt are all null. `Recovering` requires every field and receipt. Any other null combination is invalid. `requestSha256` must match the durable canonical request bytes for the matching request ID; do not infer equivalence from parsed objects.

At restart, the client validates the readback against the protected identity, current external authority, checkpoint's known request intent/receipt digest, and receipt rules below. If the server is ahead of local state but its receipt cannot be tied to a locally durable exact request, or readback is unavailable/ambiguous, remain `RecoveryRequired`; do not adopt server state by ID alone. If readback reports the exact unresolved request, persist its immutable receipt. If readback reports `NoActivation`, or an authenticated `Enrolled`/null-incarnation row, and local genesis evidence is valid, the current process may attempt initial activation using its newly persisted ID. If the prior request is still in flight, the API CAS permits only one initial winner; on conflict the client reads back the winner and reconciles. A restart from a positive incarnation requires a fresh HFA receipt bound to the current process's new ID; absent HFA host-fence evidence, it must not POST a restart activation.

This read-only operation is a required contract gap in current main, not an existing endpoint. It is part of this specification's bounded implementation slice because the durable client cannot safely distinguish a lost response from a failed transaction across process death without server readback. It does not provide host fence evidence or operational recovery authority.

## Receipt validation and persistence

The activation response is limited to 64 KiB and parsed strictly: one JSON object; no duplicate or unknown fields; schema version exactly 1; canonical `D`-format GUIDs; canonical positive decimal strings; exact UTC `createdAtUtc`; no trailing bytes. Validate every field against the durable outbound intent and current authority:

- `nodeId`, `installationId`, `controlPlaneRealmId`, `activationRequestId`, `recoveryEpoch`, and `recoveryCheckpointSha256` must exactly match the identity, request and attestation.
- `incarnation` must be positive and equal `1` for initial expected `0`, or `expectedIncarnation + 1` for a successful restart (with overflow rejected).
- `status` must be exactly `Recovering`. `Active`, unknown status, or any future status is rejected; the receipt cannot grant workload or host capabilities.
- `createdAtUtc` must parse as a canonical UTC value and be non-default. Do not use the local wall clock as a security authority or infer fencing from receipt age.

After validation, retain the response's exact bytes, compute and store their SHA-256 and bind them to the exact request digest. Atomically fsync/rename/fsync-directory the checkpoint before acknowledging the result to any future local state machine. A mismatch, invalid receipt, local write failure, or API/network uncertainty leaves the previous durable state intact and phase `RecoveryRequired`. A valid stored receipt advances only the local known server incarnation/epoch evidence; it does not set `RecoveryOnly` or `Active`.

Retry policy: repeat the exact durable request for connection loss, timeout, 408, 429, and 5xx with bounded exponential backoff and cancellation. A 200 response is accepted only after strict validation and durable persistence. 400, 401, 403, and 409 are not blindly retried; produce fixed diagnostic codes and perform readback where permitted. 503/authority unavailable stays `RecoveryRequired`; retry does not relax validation. Never log API keys, private-key bytes, certificate PEM, raw request/receipt bodies, or HFA signatures.

## Startup gate and durable state machine

The existing lock is acquired first and held through stop. After it, the new coordinator follows this order:

1. Validate protected identity files and exact credential binding. Missing identity means `RecoveryRequired`; there is no implicit enrollment or new Node identity.
2. Read the checkpoint. If present, validate its strict schema and digests. If absent, create the genesis checkpoint only after validating fresh enrollment, fresh external authority and authenticated API `NoActivation` with no activation history; every other missing/corrupt/old-version/mismatched checkpoint means `RecoveryRequired`. Never infer an empty journal from a damaged or previously used installation.
3. Create and durably store this eligible start's `ProcessStartId`/`ActivationRequestId`. A process without usable identity/checkpoint cannot durably participate in activation and remains `RecoveryRequired` without issuing a request.
4. Read and validate the current independent realm/epoch authority. Missing, unavailable, stale, lower, foreign, uncommitted or unverifiable authority means `RecoveryRequired`; never adopt an epoch from MySQL or a backup. Reconcile prior intent/readback; construct and persist exact canonical request bytes before POST.
5. Strictly validate and persist the immutable receipt. End this slice in local `RecoveryRequired`, non-admitting. RecoveryOnly/reconciliation and Active transitions remain owned by later reviewed work.

| Condition | Local result | Activation POST / effect |
| --- | --- | --- |
| Missing/corrupt identity or credential mismatch | `RecoveryRequired` with fixed reason | No request; no effect |
| Checkpoint/journal missing, corrupt, unsupported, or unresolved | `RecoveryRequired` | No new request until exact prior attempt is reconciled; no effect |
| Authority unavailable, stale, mismatched realm/epoch, or uncommitted restore | `RecoveryRequired` | No request; no effect |
| Auth/API unavailable, timeout, conflict, or uncertain response | `RecoveryRequired`; preserve exact intent | Same-byte retry/readback only; no effect |
| Valid `Recovering` receipt durably stored | `RecoveryRequired` | Receipt evidence only; no effect |
| Required HFA proof, process/effect fencing or settlement evidence unavailable | `RecoveryRequired` | No restart activation; no effect |

No cache, receipt, heartbeat, lock reacquisition, certificate/API key possession, or successful liveness check changes this gate. This slice does not implement `RecoveryOnly` or any path to `Active`.

## Required acceptance cases

All cases below are future required tests; none ran for this documentation task.

| Test | Required assertion |
| --- | --- |
| `NodeIdentityStrictReadAndBinding` | Accept only exact v1 identity; reject missing/unknown version, duplicate/unknown fields, wrong realm/Node/Installation, noncanonical IDs/digests, certificate/private-key mismatch, secret path escape, symlink, wrong owner/mode, hard link and oversized files. Diagnostics contain no secret/path contents. |
| `NodeIdentityInstallAtomicCommit` | Fault each credential/manifest write, fsync, rename and directory-fsync boundary. Identity becomes usable only after manifest commit; restart sees complete old/no identity or complete new identity, never a partially bound credential set. Exact same enrollment receipt is idempotent; different identity cannot overwrite. |
| `GenesisCheckpointRequiresNoPriorActivation` | Create genesis only from fresh verified enrollment plus current external epoch and authenticated `NoActivation` readback. Remove/corrupt the checkpoint after any activation attempt/current incarnation and assert it cannot be recreated as empty; startup remains `RecoveryRequired`. |
| `NodeCheckpointAtomicCrashMatrix` | Crash before/after temp fsync, rename and directory fsync. Restart accepts only complete strict checkpoint and preserves request ID/digest/receipt bytes; torn, lower, unknown-version or digest-mismatched state remains `RecoveryRequired`. |
| `ActivationIntentDurableBeforeSend` | Inject local write/fsync failure: zero HTTP calls. Captured outbound request bytes exactly equal the durable checkpoint bytes. |
| `ActivationOneRequestIdPerStart` | One start creates one ID and persists it before POST; simulated transport retries have byte-identical ID/body. A later process start gets a distinct ID while the prior unknown intent stays separately durable; it sends no new request until readback/CAS reconciliation allows it. No retry mutates checkpoint digest or HFA bytes. |
| `ActivationLostResponseReadback` | API commits but transport drops response and Node stops before receipt fsync. A new process reads the current API attempt, verifies its exact request digest/receipt against durable prior intent, persists identical receipt bytes, and does not treat timeout as no-commit. Include old/new initial CAS race and prove one positive incarnation only. |
| `ActivationCurrentReadbackAuthAndIsolation` | Independent API contexts return only this authenticated Node's state; foreign/revoked identity is denied. Management listener accepts GET; ordinary API/bootstrap listeners reject it before handler invocation. Readback has no mutation/HFA consumption. |
| `ActivationReceiptStrictBinding` | Accept exact valid Recovering receipt. Reject duplicate/unknown fields, oversize/trailing bytes, wrong schema, identity, request ID, epoch, checkpoint hash, noncanonical timestamp, zero/overflow/wrong incarnation, Active/unknown status and altered receipt bytes. Persist original bytes and SHA-256 only after all checks. |
| `ActivationCredentialAndAuthorityFailClosed` | Missing/rotated/revoked credential, missing API trust, missing HFA trust, missing external register, stale/foreign/lower/uncommitted epoch, unavailable listener or invalid proxy-auth result leaves `RecoveryRequired` and sends no unsafe request. Synthetic keys exist only in test composition. |
| `ActivationReceiptPersistenceFailure` | Valid API receipt followed by file/fsync/rename failure leaves old checkpoint and `RecoveryRequired`; restart resolves by exact authenticated readback and cannot report Active. |
| `RecoveryEvidenceUnavailableBlocksStartup` | Missing/corrupt journal, unknown operation, unresolved effect, missing HFA receipt or stale register blocks request/admission as specified; no resource release, broker subscription, Docker call, executor, or host effect occurs. |
| `NodeActivationClientArchitectureBoundary` | Node DI has no enrollment/bootstrap client, broker, Docker, executor, workflow/custom-step or host-effect dependency. Activation/API client is explicitly disabled without reviewed auth and authority adapters; Runner registrations/behavior are unchanged. |

Use temporary root-owned directories on a supported Linux filesystem for real file/permission/symlink/fsync tests; use fault-injection file operations for crash points and disposable MySQL/independent contexts for readback races. Unit fakes do not prove Linux atomic rename/fsync or API transaction behavior. Full Linux and database acceptance remains before implementation acceptance.

## Implementation boundary, prerequisites and gates

One bounded implementation PR after approval may add the Node local identity/checkpoint store, verified-bundle import seam, strict activation HTTP client/receipt parser, required API current-activation readback using the existing activation tables, lifecycle wiring that remains `RecoveryRequired`, and the named tests. The readback contract should require no migration; if source review finds a schema gap, stop and return that gap for review rather than expanding scope. The PR must preserve Runner behavior and the Node dependency boundary. It must not implement enrollment/network bootstrap, production authority/HFA adapters, service installation, operational activation enablement, broker consumption, Docker access, workload execution, recovery effects, or production configuration.

Before coding, review this specification and the affected N0 identity, auth, restore-epoch, host-fence and recovery contracts. The activation origin is an explicit service-owned `AUTOMATON_NODE_MANAGEMENT_API_ORIGIN` setting; it must be an absolute HTTPS URI without user info, query or fragment. No hardcoded host/port, general ambient configuration provider, tenant-provided URI, HTTP downgrade, redirect following, or alternate ordinary/bootstrap listener is allowed. Missing/invalid origin keeps startup `RecoveryRequired`. The configured client validates the API server TLS identity normally and presents only the locally bound Node credential set under the selected auth proposal; proxy/mTLS wiring remains subject to independent security approval. Independent security/operations approval of the mTLS/proxy/authentication and external-authority design remains separate. Before isolated acceptance, pass Linux filesystem crash tests, disposable MySQL/API readback tests, affected builds/tests and `Automaton.All.sln` nonincremental Debug build. Operational activation and rollout remain disabled pending external register/HFA custody and verifier, real enrollment provisioning and key protection, independent review, deployed listener/Traefik/direct-origin evidence, host-fence/recovery proof, and the N0-D live gates.

Concrete blockers/gaps on current main:

- No Node enrollment client or verified credential-bundle writer exists; this slice can define and test the import seam, but a separate reviewed enrollment flow must supply real identity material.
- The external restore-register reader/verifier and protected trust distribution are absent and operational custody remains unresolved. Without them, no production activation request is sent.
- No API current-activation readback endpoint exists; add it with strict management-listener isolation and auth before enabling restart reconciliation.
- No operational HFA receipt producer/verifier provisioning, recovery/effect settlement, RecoveryOnly transition, or Active transition exists. A restart from positive incarnation stays `RecoveryRequired` without fresh HFA proof.
- mTLS/API-key selection remains a proposed design pending independent security review. No production trust fallback is allowed.

This document is specification-ready for review only. Approval authorizes one bounded implementation PR; it does not enable operational activation or privileged behavior. The current architecture guide remains unchanged because this is a proposal and implemented behavior has not changed.
