# N1-A activation and recovery implementation specification

Status: design specification prepared for review against current main `8f301941` and the reviewed N0 contracts. This is not an implementation, security approval, deployment plan, or live acceptance record. No runtime, database, credential, host, broker, Docker, workload, or production state was changed.

Scope is the activation and recovery portion of N1-A only. The already merged limited preparation remains the baseline: `src/Automaton.Node` is non-enrolled and nonprivileged, and existing Runner behavior is preserved. This document does not authorize activation, broker consumption, Docker access, workload execution, or production changes. The existing [N0-B contracts](N0-B-contracts.md), [N0-C migration design](N0-C-migration-design.md), [N0-D architecture and acceptance contract](N0-D-architecture-and-acceptance.md), and [authentication/security review](N0-authentication-and-security-review.md) remain normative where they are more specific.

## Current-main boundary

Current main contains enrollment authorization and single-use recovery-challenge records, enrollment handlers, certificate/API-key binding, and the limited Node preparation service. It does not contain Node management activation state, an incarnation CAS endpoint, an external recovery-register client, assignment recovery grants, or the startup admission state machine specified here. Enrollment completion is not activation. The proposed activation state is separate from the existing enrollment authorization status and must not overload the bootstrap recovery endpoint.

Relevant current source seams checked on `8f301941`:

- `src/Automaton.Infrastructure/Entities/NodeEnrollmentAuthorization.cs` and `NodeEnrollmentRecoveryChallenge.cs` hold enrollment, realm, credential binding, and one-time recovery challenge state; neither is a Node activation/session record.
- `src/Automaton.Api/Services/NodeEnrollmentHandlers.cs` commits enrollment conditionally and consumes recovery challenges once. Those flows remain unchanged by this specification.
- `src/Automaton.Api/Extensions/NodeEnrollmentApiExtensions.cs` exposes bootstrap completion and credential recovery routes. Activation and recovery-session operations need their own management-listener routes and listener allow-list entries; bootstrap must remain limited to its reviewed endpoints.
- `src/Automaton.Node/Program.cs` and `Preparation/NodePreparationService.cs` register only the preparation lifecycle. The future activation coordinator belongs in the Node feature boundary and must not pull in Runner execution registrations or tenant execution dependencies.
- `src/Automaton.Infrastructure/Data/ApplicationDbContext.EntitySets.cs` and `NodeEnrollmentModelConfiguration.cs` show the current persistence integration point. Proposed activation entities and constraints require a new migration and model-snapshot update; no such schema exists today.

## Required identities and durable records

Use the N0-B identity tuple without substituting a transport or machine name:

| Value | Authority and rule |
| --- | --- |
| `ControlPlaneRealmId` | Provisioned control-plane realm, bound at enrollment and independently checked against the recovery authority. A request body cannot select or change it. |
| `NodeId` | Existing RunnerHost identity bound to the committed enrollment. It is immutable for this Node identity. |
| `InstallationId` | Root-protected local installation identity bound at enrollment. It is read from the protected local identity record, never caller-selected. |
| `TransportRunnerId` | Existing routing identity associated with the Node; it is not an ownership or activation authority. |
| `RecoveryEpoch` | Positive, monotonic realm-wide value read from the independently held restore register. A restored DB value is only a claim to compare, never the authority. |
| `Incarnation` | Positive, monotonic per-Node activation/session value allocated by the control plane CAS. It advances for each newly admitted Node process session, not for a retry of the same request. |
| `ActivationRequestId` | Client-generated random GUID created once for one Node process start and durably recorded locally before its first activation request. Reuse it on transport retry within that process start; a process restart creates a new request ID. It is the idempotency identity for the canonical request body. |

Add proposed durable records to the existing application persistence boundary, with naming finalized during implementation review:

1. `NodeManagement`: one row per Node with immutable Node/Installation/realm binding, current enrollment and credential revisions, current `RecoveryEpoch`, nullable `CurrentIncarnation` (null only before the first activation; every allocated value is positive), lifecycle status, concurrency token, active activation request, and recovery checkpoint digest. Preserve fenced/revoked identities and history; do not cascade into workload or operation history.
2. `NodeActivationAttempt`: immutable binding of `(NodeId, InstallationId, ActivationRequestId)` to canonical request digest, expected prior incarnation, assigned incarnation, realm, epoch, status, and immutable receipt. Unique `(NodeId, ActivationRequestId)`. Reuse with different canonical bytes is a conflict.
3. `NodeRecoveryCheckpoint` or equivalent existing-operation record: stable recovery operation ID, old/new incarnation, previous/current epoch, journal inventory digest, unresolved operation set digest, per-assignment readiness, external fence proof references, and phase. Prefer extending the established operation store if it can retain this audit and retry contract; do not create a competing operation ledger.

All SQL transitions use database constraints plus conditional updates/transactions; process-local locks and ORM concurrency tokens alone are insufficient across API replicas. Use MySQL integration tests with independent connections and the actual migration. Enrollment authorization and activation state are related by explicit foreign keys/bindings, but bootstrap credential recovery must not mint an activation receipt.

## Startup, lock, and admission state machine

Startup is fail-closed and ordered. No step may be reordered to start a consumer or effect path early.

| State / step | Required action and durable condition | Failure result |
| --- | --- | --- |
| `Stopped` | No management session is admitted. | — |
| `LockAcquired` | Open the stable host-scoped installation lock inode under a root-owned directory and acquire an OS-held exclusive interprocess lock. The handle remains open for the manager lifetime. Verify the file and parent ownership/mode and protected `NodeId`/`InstallationId` record. Never unlink or replace the lock file while processes may hold it. | Another owner, unsupported lock filesystem, symlink/identity mismatch, or permission failure leaves Node stopped. |
| `LocalStateValidated` | Read and validate the versioned local identity, journal/checkpoint, integrity digests, fence watermark, and activation request record. Missing/corrupt/unknown-version state is not evidence that no effect occurred. | Keep the local startup gate closed and report a `RecoveryRequired` diagnostic; do not add a new persisted NodeManagement status for this local condition or activate normal service. |
| `AuthorityValidated` | Authenticate to the intended realm using the reviewed enrollment binding. Read the independent restore register and verify its authenticated receipt/chain, realm, committed epoch, restore operation/checkpoint and host-scope fence evidence. Compare it with the local durable epoch and API DB epoch. | Any missing, stale, lower, foreign-realm, uncommitted, or inconsistent authority result quarantines startup. Never adopt a DB-supplied epoch or continue with cached authority. |
| `AttemptReserved` | Send the activation request described below. API atomically claims the attempt and CAS-allocates one next incarnation in `Recovering`. Node durably writes and fsyncs the returned receipt/checkpoint before acknowledging or progressing. | A lost response retries the exact attempt. Local write failure leaves server state `Recovering`; a later process must use a new attempt and recovery flow. |
| `RecoveryOnly` | Reconcile old permits, exact original operations, journal evidence, owned resource observations, reservations, storage/cgroup identity and per-assignment fences under the N0-B/N0-C contracts. Only explicitly authorized read-only recovery and evidence/receipt operations are eligible. | Keep affected assignments paused and retain occupancy/uncertainty debt. No timeout converts uncertainty to success or free capacity. |
| `Active` | Enter only after the API verifies current realm/epoch/incarnation, all required fence and settlement proofs, the local recovery checkpoint is durable, and every activated assignment has a ready receipt. Node records that receipt durably before opening management admission. | Any crash, epoch change, revocation, proof mismatch, or unavailable authority returns to closed/recovery-only startup; no local cached Active state suffices. |

`RecoveryOnly` is the local runtime mode corresponding to durable `NodeManagement.Status = Recovering`; it is not a new persisted lifecycle status. `Active` in this N1-A slice means authenticated, enrolled management session with reviewed capabilities. It does not grant tenant execution, host administration, broker subscription, Docker access, or workload dispatch. Those remain later slices and separate capability gates. The recovery-only process in this task specification has no broker consumer, Docker client/socket, executor, or workload code path.

### Installation-lock ownership contract

The lock is keyed by the configured host installation identity, not account, assignment, data directory, or per-Node child lock. N0-C requires one host-wide manager fence; multiple installation-local locks cannot substitute for it. Initial supported implementation must name and validate a local filesystem with OS interprocess-lock semantics suitable for the service. Network filesystems or unsupported lock semantics are ineligible until separately proven.

The OS lock proves only that one cooperating manager currently holds that lock inode. It does not prove old child processes, engine requests, host writers, or legacy services have stopped. Reacquisition after process death therefore leads to `RecoveryOnly`, never straight to `Active`. Before a new manager can cause any future host effect, the N0-C host-wide resurrection-prevention and outstanding-effect settlement proof must independently pass. Existing inherited workloads may remain serving under their original identity while inspected; they are never rerun or relabeled by activation.

Graceful shutdown first closes new admissions and records quiescing state, then settles/reports only work authorized by its current contract, fsyncs its checkpoint, and releases the lock last. Abrupt process death releases the kernel lock, but leaves the previous activation and all unresolved effects durable and recoverable. Do not use a lease timeout, lock-file age, PID file, or missing callback as stop proof.

## Activation request and incarnation CAS

The management activation request is a strict versioned contract. This example is the initial activation; it has no host-fence receipt because no prior incarnation exists. Restart requests include the signed HFA receipt. Example values are illustrative and non-authoritative:

```json
{
  "schemaVersion": 1,
  "controlPlaneRealmId": "40000000-0000-0000-0000-000000000001",
  "recoveryEpoch": "3",
  "nodeId": "30000000-0000-0000-0000-000000000001",
  "installationId": "60000000-0000-0000-0000-000000000001",
  "activationRequestId": "70000000-0000-0000-0000-000000000001",
  "expectedIncarnation": "0",
  "credentialRevision": "2",
  "journalInventorySha256": "<64 lowercase hex characters>",
  "recoveryCheckpointSha256": "<64 lowercase hex characters>",
  "supportedManagementSchemaVersions": [1]
}
```

Use N0-B canonical JSON rules: reject duplicate/unknown properties, invalid version, noncanonical GUIDs, nonpositive or overflowed Int64 decimal strings, duplicate schema versions, malformed digests and unbounded collections. `expectedIncarnation` has one explicit exception: the literal canonical decimal string `"0"` is allowed only for the first activation of a committed `Enrolled` identity whose `NodeManagement.CurrentIncarnation` is null and which has no prior activation attempt. The zero is a compare-and-swap sentinel, never a persisted incarnation; the first allocated value is `1`. For all later activations, `expectedIncarnation` must be positive and equal the current persisted incarnation. No other integer field accepts zero. The API derives Node, Installation and realm from the authenticated enrollment binding, then requires exact equality with the request. The client fields are consistency assertions, not authority. The external register is read through its reviewed verifier; API DB fields cannot authorize a higher epoch.

For a restart, include a `hostFenceReceipt` envelope containing the signed payload, `signatureAlgorithm = "ECDSA_P256_SHA256_P1363"`, and Base64 signature. Initial activation rejects that property; a later activation requires it. Its exact bytes are covered by the canonical activation request digest.

For a new process attempt, the API performs one transaction:

1. Parse bounded request shape and authenticate the bound Node certificate/API-key proof. Derive Node/Installation/realm from the committed enrollment binding. Reject revoked or foreign identities. Management-listener route isolation applies before the handler.
2. Look up `(NodeId, ActivationRequestId)` before state-dependent expected-incarnation checks. If found with equal canonical request digest, return the immutable activation receipt/status without minting a new grant; if the digest differs, return 409 `activation_request_conflict`. This preserves retries after the initial `null → 1` CAS. Current revocation or authority state can deny further admission, but cannot rewrite the stored receipt.
3. For a new request, validate the current external epoch authority and credentials, then conditional-update the single `NodeManagement` row. Initial activation predicates on `Status = Enrolled AND CurrentIncarnation IS NULL` and writes incarnation `1`. Later activation predicates on the exact positive expected incarnation and allowed prior status, then writes `expected + 1`. Both set status `Recovering`, current epoch, and current activation request. Every predicate includes NodeId, InstallationId, realm, credential revision and nonrevoked state. Require exactly one row changed.
4. Insert the activation request and immutable receipt in that transaction with unique constraints. A restart request also atomically consumes its host-fence receipt as described below. Commit before returning. A lost transaction race returns the winner's outcome only when it is the same request and digest; a different request receives 409 `activation_in_progress` / `incarnation_conflict` with no session grant and must re-read status. Do not advance again automatically.

Allowed prior state is explicit: initial `Enrolled` with no allocated incarnation; or `Quiescing`/`Fenced` with a current, durable host-fence receipt and recovery checkpoint that inventories unresolved effects and preserves their occupancy debt. A previous `Recovering` incarnation can be replaced only after that same fence/checkpoint transition. `Active` is never displaced directly. Heartbeat age, API-key possession, certificate possession, a self-reported lock boolean, or a lock-file timestamp cannot produce a fence receipt.

### Host-fence receipt contract

Only the **Infrastructure Operations Host-Fence Authority (HFA)** may issue a restart receipt. HFA is a separately controlled authority outside the Node host and ordinary API database; its issuer identity is distinct from both the Node certificate and the N0-D restore-register signer. HFA signs only after its authorized host-fencing process has verified that every old Node/legacy manager and restart source in the host scope is fenced, and has recorded a durable disposition for each outstanding effect. A Node, API handler, tenant/user, or ordinary database administrator cannot mint or self-assert the receipt. The HFA signer key and its operational custody remain a rollout prerequisite; this specification does not establish that a production HFA is deployed.

Version 1 receipt is a signed envelope over an exact, deterministic UTF-8 payload. Payload encoding is strict camelCase JSON with properties emitted in the table order below, no insignificant whitespace, GUIDs in `D` form, Int64 values as canonical decimal strings, UTC timestamps in the N0-B format, and lowercase hex digests. The verifier rejects duplicate/unknown fields and any alternate encoding. Sign with ECDSA P-256/SHA-256 using fixed-width IEEE-P1363 `r || s` signature bytes (exactly 64 bytes, Base64 encoded) over `UTF8("automaton.host-fence-receipt.v1\n") || canonicalPayloadBytes`. The verifier accepts only configured HFA public keys identified by `signerKeyId`; keys are provisioned outside NodeManagement rows and the restored application database. There is no unsigned mode, test-key fallback, caller-provided trust key, or boolean bypass.

Required signed payload fields:

| Field | Validation |
| --- | --- |
| `schemaVersion`, `receiptId`, `issuerId`, `signerKeyId` | Version exactly 1; canonical unique IDs; issuer/key pair is in the configured HFA trust set. |
| `controlPlaneRealmId`, `nodeId`, `installationId`, `scope` | Exact current binding; `scope` is exactly `host-wide`. |
| `priorRecoveryEpoch`, `priorIncarnation`, `priorActivationRequestId` | Exact current server epoch, positive prior incarnation, and the activation request that allocated that incarnation. |
| `nextActivationRequestId`, `expectedCurrentIncarnation` | Exact new request ID and exact current incarnation expected by its CAS; `expectedCurrentIncarnation` equals `priorIncarnation`. This prevents moving the same receipt to another retry or session. |
| `hostFenceGeneration`, `fencedAtUtc`, `issuedAtUtc`, `expiresAtUtc` | Positive strictly increasing host-scoped generation; ordered UTC times; receipt lifetime at most 10 minutes. Expired or not-yet-valid receipts fail closed. |
| `fenceEvidenceSha256`, `effectDispositionSha256`, `recoveryCheckpointSha256` | SHA-256 digests of the HFA evidence bundle proving all manager/restart paths fenced and unresolved effects inventoried; checkpoint digest must equal the activation request and current recovery checkpoint. |

Before any state-dependent CAS, the API verifies the signature, trust key, time bounds and every binding above. In the same MySQL transaction as incarnation CAS and activation receipt insertion, it inserts the HFA receipt ID into a consumed-receipt table with a unique key on `receiptId`, plus unique binding of the receipt to `nextActivationRequestId`. The API also requires `hostFenceGeneration` to exceed the last accepted generation for that host. Any duplicate receipt under a different activation request, realm, epoch, installation, prior incarnation, or checkpoint is rejected as `host_fence_receipt_replay` or `host_fence_receipt_conflict`, with no state change. Transaction rollback consumes neither receipt nor incarnation.

An exact retry using the already committed `ActivationRequestId` is resolved from `NodeActivationAttempt` first and returns its immutable receipt; it does not reconsume the HFA receipt or issue another incarnation. This retry path returns no new activation grant if credentials have since been revoked or the current realm/epoch is no longer valid. A synthetic HFA key/verifier may be used only by isolated tests and must pass through the same signature and binding validator. Production composition must use configured HFA trust keys and fail startup/activation closed if they are absent; test keys and test verifier implementations must be excluded from production dependency registration and packaging.

**Initial activation example:** committed enrollment, no previous activation row, and `NodeManagement.CurrentIncarnation = null`, `Status = Enrolled`; request `expectedIncarnation = "0"`; CAS writes positive incarnation `1` and `Status = Recovering`. No host-fence receipt is required because no prior session exists. A new request with zero after this commit is rejected. A same-process timeout retries identical bytes and the same `ActivationRequestId`; it returns the exact same assigned incarnation and receipt and cannot allocate again. A different body with the same request ID conflicts. The local installation lock blocks cooperating managers on one host, while the API CAS blocks concurrent activation claims. The separately verified host-fence receipt prevents an active/uncertain identity from being displaced by a request from another host.

After process restart, generate and fsync a new `ActivationRequestId`. If the prior request committed but its response/checkpoint was lost, the old request remains immutable. The new request first requires an independently verifiable fence transition for the prior current incarnation; then it CASes from the current server incarnation to the next incarnation in `Recovering`. It must not reset to the local stale expected value. The server checks that the old manager cannot resume and that the required outstanding effects are settled or preserved as explicit occupancy debt under N0-B/C. If these proofs are unavailable, reject and remain quarantined. Thus retrying one request is idempotent while restarting a process creates a new session/incarnation only after recovery gates are met.

No code may set `Active` in the initial reservation transaction. A separate completion CAS requires the current activation attempt, incarnation, realm, epoch, credential revision, recovery checkpoint digest, per-assignment settlement/fence proof digests, and expected `Recovering` concurrency token. It changes only that exact current attempt to `Active`. Repeating the same completion returns its receipt. Stale or competing completion, old-incarnation requests, revoked credentials, mismatched evidence, or changed epoch are denied without touching assignment state.

## Activation retry and failure matrix

| Failure point | Required retry/recovery behavior |
| --- | --- |
| Before API transaction | Retry same request/attempt; no incarnation allocated. |
| Transaction rolled back | Retry same bytes; one future CAS may allocate. No attempt row/receipt is visible. |
| API committed, response lost | Same process retries same attempt and receives the same incarnation and receipt. No second increment. |
| API committed, local receipt fsync failed | Do not open management admission. Process restart creates a new attempt; API recognizes prior current Recovering incarnation, verifies local lock/recovery, and advances only through the recovery path. No old attempt is rewritten. |
| Two replicas process the same attempt | Unique key/CAS leaves one durable attempt. Loser reads and returns the matching pinned result after transaction conflict. |
| Two distinct attempts for same expected incarnation | One CAS wins. The other gets conflict/status only; it cannot receive an activation grant, increment again, or displace the winner. |
| Active API receipt arrives, then epoch or revocation changes | Close admissions immediately on the next required authority check; discard cached Active. Startup/recovery protocol required; no work starts offline. |
| Crash during recovery or before Active CAS | New process starts RecoveryOnly and resumes from durable checkpoint. It does not clear evidence or replay old effects. |
| Crash after Active CAS but before local Active receipt | New process starts RecoveryOnly under a new incarnation; it fetches current status and proves recovery state. It does not infer Active from a server status alone. |

## Realm and restore-epoch validation

`ControlPlaneRealmId` binds the enrollment, Node row, local protected identity, activation request, activation receipt, journal record, recovery grant, API authority response, and external register. Exact realm mismatch is a hard authorization failure; do not attempt realm migration in place. A replacement realm requires reviewed disaster recovery and explicit reenrollment after old writers are fenced.

`RecoveryEpoch` is separate from `Incarnation`, assignment generation, policy revision, credential revision, and software version. Compare fence dimensions as a tuple, with realm and epoch validated before incarnation/generation. Never accept a larger generation or incarnation as compensation for a stale epoch. Ordinary activation cannot advance the epoch, and ordinary API/Node traffic cannot rewrite the external register.

At every startup and every activation/Active completion boundary, compare:

- authenticated local identity realm and epoch watermark;
- authenticated external register realm, committed epoch and signed restore operation/host-scope proof;
- API database realm/epoch projection and current Node row;
- activation attempt and incarnation CAS;
- journal/local fence epoch and operation bindings.

Normal case: all current values equal and register says committed; proceed. Local/API epoch lower than register: enter R4 rebind only after R0–R3 restoration and host fencing, persist the new epoch, then activate a fresh incarnation in RecoveryOnly. Local/API epoch greater than register, register unavailable, ambiguous register copy, realm mismatch, stale DB projection, or lost register: quarantine. Do not select the maximum number observed. R0–R5, including restore-history/dedup completeness, remain the only epoch-advance protocol. Unknown request history returns `restore_history_unknown` before allocation as N0-D specifies.

## Recovery-only operation and transition to Active

Recovery-only is a restricted management state, not a degraded execution state. It may authenticate and fetch the current recovery view; read local journal and exact owned-resource metadata; submit N0-B-authorized original evidence under a current recovery grant; read operation receipts; and persist its checkpoint. This specification does not authorize any live Docker/engine inventory, engine mutation, broker action, workload execution, or production operation. In a later separately reviewed privileged slice, host/resource observation and exact recovery stop actions must satisfy N0-C's host fence and effect-settlement proof before they can be wired.

Recovery reconciles each old command using its original Node/incarnation/assignment/generation/workload/revision/request hash. Never rewrite its identity to the new incarnation. Apply N0-B recovery dispositions: only provably never-issued commands may receive one fenced successor; accepted/issued uncertain commands are observed or marked uncertain/Lost, never automatically replayed; saved terminal bytes are imported exactly with original executor identity and new reporting incarnation; terminal conflicts remain historical evidence. No new reservation or workload allocation is part of recovery import.

For each account/assignment, the recovery checkpoint records the source of evidence, original operation tuple, settlement status, remaining occupancy/debt, policy/storage/cgroup verification receipt (when applicable in a separately authorized fixture), current fence and readiness result. API and local records must agree on checkpoint digest. `Active` admission is per-assignment and cannot be inferred from one Node heartbeat. Unknown/corrupt journal, mismatched ownership labels, unknown external effect, old manager that can resurrect, unresolved permit, unverified policy or unavailable authority keeps the affected scope paused. Do not free reservations to make recovery converge.

After restart in RecoveryOnly, activation and recovery endpoints are available only on the authenticated Node-management listener. Bootstrap `/complete` and `/recovery/*` remain enrollment credential recovery only. Ordinary API controllers, hubs, fallback paths, and bootstrap listeners reject Node management operations before handler invocation per N0-B listener isolation. The Node sends no requests for unsupported workflows/custom steps and no manager route can silently fall back to legacy Runner execution.

## Bounded next implementation scope

After this specification and the reviewed N0 contracts are approved, the next code slice is limited to **activation persistence, conditional incarnation CAS, and immutable activation retry receipts**:

1. Add the Node management/activation persistence records, constraints, migration and model snapshot under the existing Infrastructure/ApplicationDbContext boundary.
2. Add the strict activation request/receipt contract and API transaction implementing the initial-null-to-one case, later positive-incarnation CAS, HFA receipt validation/one-time consumption, and exact same-request replay behavior.
3. Add isolated tests for the persistence/CAS/retry contract, using a synthetic signed HFA fixture only in test composition. Include an HFA verifier boundary; production activation must fail closed without configured real HFA trust keys. This slice does not add a production HFA issuer/integration adapter.
4. Preserve existing enrollment/bootstrap behavior, existing Runner behavior and the limited Node dependency boundary. Do not add Node startup/lock adapters, operational recovery adapters, broker consumption, Docker/host effects, workload execution, install/restart behavior or broad restore tooling.

Later owning slices handle Node local lock/startup enforcement, operational HFA and restore-register adapters, old-effect inventory/settlement, evidence import, service restart prevention, and broad restore drills. Relevant source seams on current main include `NodeEnrollmentHandlers.cs`, `NodeEnrollmentApiExtensions.cs`, enrollment entity configuration, `ApplicationDbContext`, and `src/Automaton.Node`; revalidate concrete owners against the implementation branch before editing. This specification defines behavior without claiming these new symbols already exist.

## Required acceptance tests

These are future requirements. The first table is the acceptance set for the bounded persistence/CAS/retry-receipt slice. Use a disposable, migrated MySQL database with independent connections for database guarantees; do not use production credentials or hosts.

| Test | Fixture and required assertions |
| --- | --- |
| `InitialActivationZeroSentinel` | With committed enrollment, `Status = Enrolled`, null current incarnation and no prior request, canonical `expectedIncarnation = "0"` commits exactly once as positive incarnation `1` and status `Recovering`. Persisted incarnation is never zero. Zero with Active/Fenced/Recovering status, nonnull incarnation, a prior activation row or a second request is rejected. |
| `ActivationRequestIdempotency` | Two independent DbContexts race identical request ID and canonical bytes: one incarnation and byte-identical immutable receipt. Same ID/different digest is 409 with no row change. Exact replay after initial null-to-one CAS returns the pinned result. |
| `ActivationIncarnationCasRace` | Two distinct requests race the same positive expected incarnation: exactly one CAS commits; loser receives conflict; only one current Recovering incarnation exists and no Active grant is issued. Include Int64 overflow; it fails closed. |
| `ActivationCommitResponseLoss` | Drop the response after transaction commit. Retry the same request ID/body and return the same incarnation/receipt. Assert the attempt and receipt are immutable and no second allocation occurs. |
| `HostFenceReceiptSignatureAndBinding` | Generate an ephemeral synthetic HFA P-256 key in test-only composition and run its signed receipt through the same signature/binding validator. Reject unknown/test-untrusted key, bad signature, altered/duplicate fields, wrong issuer, wrong realm/Node/Installation, non-host-wide scope, wrong prior epoch/incarnation/request, wrong next request ID, checkpoint/effect digest mismatch, invalid lifetime, expired receipt and non-increasing fence generation. |
| `HostFenceReceiptSingleUseRace` | Race two new activation requests against one receipt using independent MySQL contexts. One transaction consumes it and advances the incarnation; the other fails as replay/conflict. Rollback leaves both the receipt and incarnation unconsumed. Same already-committed request ID retry returns its immutable receipt without consuming the HFA proof a second time; another request ID cannot reuse it. |
| `ActivationRealmEpochAndCredentialBinding` | Wrong realm, Node, InstallationId, certificate pin, API key, credential revision, revoked enrollment, retired credential or stale/foreign epoch is rejected before mutation. Include valid controls. |
| `ActivationSchemaConstraints` | Apply the real migration and verify uniqueness, foreign-key, null-before-initial / positive-after-allocation invariants and durable activation/receipt rows. Competing bootstrap/recovery operations cannot create duplicate Node bindings or activation identities. |
| `NoSyntheticProductionBypass` | Production dependency composition has no test signer/verifier registration, unsigned mode or allow-fence bypass; missing real HFA trust configuration disables activation. Test fixtures remain in test-only assemblies/configuration. |

The following requirements belong to later owning slices and are not prerequisites for coding or isolated acceptance of the bounded CAS slice:

| Deferred test / owner | Acceptance scope |
| --- | --- |
| `InstallationLockProcessOwnership` — N1-A local startup / N1-D service installation | Disposable supported Linux filesystem and real processes; exclusivity, crash release, stable inode, ownership/mode/symlink checks; reacquisition still starts RecoveryOnly. |
| `RecoveryOnlyNoEffectDependencies` — N1-A startup integration | Resolved Node DI/architecture graph excludes broker, Docker, executor, workflow/custom-step and host-effect adapters; application listener route matrix rejects before handler invocation. |
| `RecoveryOriginalIdentityAndEvidence` — N1-B/N1-C | N0-B never-issued successor, uncertain accepted operation, exact saved-result import, lost receipt replay and conflicting terminal bytes; preserve original tuple/hash/reservation and invoke no body. |
| `RecoverySettlementBlocksActive` / `RecoveryPerAssignmentActivation` — N1-C/N0-C | Unsettled effects, resource ownership, journal integrity, storage/cgroup policy and occupancy debt block affected admission; no timeout releases capacity. |
| `RestoreEpochAuthorityMatrix` / `RestoreR0R5Rebind` — N1-D/N4-A | Isolated external register, restored DB/data and old/new writers; broad R0–R5 fault injection, missing dedup history, register loss/clone and no old writer/effect reactivation. |
| `ActivationShutdownAndRestart` — N1-A local startup / N1-D | Graceful shutdown and crash across local checkpoint boundaries; restart resumes recovery-only and cannot treat stale local Active as current. |

## Verification gates

**Before coding:** independent review must approve this activation contract and the affected N0 security/identity/restore/fencing decisions. This review gate is about the contract and bounded code scope; deployed Traefik, production HFA/register infrastructure, custody exercises and broad restore drills are not prerequisites to begin the persistence/CAS/retry implementation. Unreviewed contract choices remain a stop condition.

**Before accepting the isolated implementation slice:** run the actual migration against disposable MySQL; pass all first-table concurrency, retry, initial-zero, HFA receipt signature/binding/single-use and realm/epoch tests; run affected API/Infrastructure/architecture tests and nonincremental affected-project builds; run the required nonincremental `Automaton.All.sln` Debug build. Synthetic HFA signer material is test-only and exercises the real validator; it proves contract behavior only, not operational fencing. Report unavailable NuGet audit data separately.

**Before rollout or enabling activation against an operational Node:** provision and review HFA signer custody/trust distribution, the independent restore-register authority and recovery procedures; verify the selected Traefik/API mutual-auth path, certificate/pin rotation and direct-origin denial on the deployed topology; prove host-wide restart-source fencing and outstanding-effect settlement; complete the applicable isolated recovery/restore rehearsal and record live evidence. These deployment and operational acceptance gates remain mandatory, but do not block the bounded coding or isolated acceptance gates above.

## Unresolved rollout prerequisites

The coding and isolated-acceptance gates are specified above; neither requires deployed operations. The following rollout evidence remains outstanding:

- Provision Infrastructure Operations HFA, protect and recover its signing key under reviewed custody, distribute its trust keys outside NodeManagement/restored DB state, and establish auditable issuance/revocation procedures. The synthetic test issuer cannot sign operational receipts.
- Deploy and exercise the independent restore-register authority, signer recovery, authenticated verification path and R0–R5 process. A missing, cloned or unavailable register keeps startup quarantined; DB epoch is never a substitute.
- Establish the approved host-wide lock/service-restart controls and real receipt-production evidence for every old manager/restart source; settle or preserve outstanding effects and occupancy. The local installation lock alone is insufficient.
- Complete supported-platform lock/journal/atomic-fsync crash evidence, deployed Traefik/API mTLS and pin-rotation checks, listener route isolation and direct-origin denial. mTLS selection and operational custody remain review-gated proposals.
- Complete broad restore, storage/cgroup, quota, rollback and privileged recovery evidence in their owning N1-C/N1-D/N4-A slices before those consumers or rollout.

If coding review is incomplete, do not start the persistence/CAS/retry slice. If isolated acceptance is incomplete, do not accept that slice. If rollout evidence is incomplete, keep operational activation disabled and fail closed. These are separate gates; later deployment work does not block the reviewed bounded implementation or its isolated tests.

## Review disposition

This specification is ready for design review. It does not implement runtime behavior or claim test, security, host, restore, deployment, or production acceptance. After approval of the coding gate, the next implementation remains limited to activation persistence/CAS and immutable retry receipts. Operational recovery adapters and broad restore drills stay deferred to their owning slices; rollout remains separately gated.
