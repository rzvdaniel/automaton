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

1. `NodeManagement`: one row per Node with immutable Node/Installation/realm binding, current enrollment and credential revisions, current `RecoveryEpoch`, current `Incarnation`, lifecycle status, concurrency token, active activation attempt, and recovery checkpoint digest. Preserve fenced/revoked identities and history; do not cascade into workload or operation history.
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

The management activation request is a strict versioned contract. Example values are illustrative and non-authoritative:

```json
{
  "schemaVersion": 1,
  "controlPlaneRealmId": "40000000-0000-0000-0000-000000000001",
  "recoveryEpoch": "3",
  "nodeId": "30000000-0000-0000-0000-000000000001",
  "installationId": "60000000-0000-0000-0000-000000000001",
  "activationRequestId": "70000000-0000-0000-0000-000000000001",
  "expectedIncarnation": "4",
  "credentialRevision": "2",
  "journalInventorySha256": "<64 lowercase hex characters>",
  "supportedManagementSchemaVersions": [1]
}
```

Use N0-B canonical JSON rules: reject duplicate/unknown properties, invalid version, noncanonical GUIDs, nonpositive or overflowed Int64 decimal strings, duplicate schema versions, malformed digests and unbounded collections. The API derives Node, Installation and realm from the authenticated enrollment binding, then requires exact equality with the request. The client fields are consistency assertions, not authority. The external register is read directly through its reviewed verifier; API DB fields cannot authorize a higher epoch.

For a new process attempt, the API performs one transaction:

1. Verify the bound Node certificate and API-key proof, committed nonrevoked enrollment, exact Node/Installation/realm binding, current certificate/key revisions, expected `RecoveryEpoch`, and current external restore-attestation receipt. Management listener route isolation applies before the handler.
2. Look up `(NodeId, ActivationRequestId)`. If found with equal canonical request digest, return the existing immutable outcome; if the digest differs, return 409 `activation_request_conflict`.
3. For a new attempt, conditional-update the single `NodeManagement` row from the expected incarnation and allowed prior status to `Incarnation = expected + 1`, status `Recovering`, current epoch and current attempt. The predicate includes NodeId, InstallationId, realm, epoch, expected incarnation, credential revision, nonrevoked state, and an allowed prior lifecycle state. Require exactly one row changed.
4. Insert the activation attempt and immutable receipt in that transaction with a unique constraint. Commit before returning. A lost transaction race returns the winner's outcome only when it is the same attempt and digest; a different attempt receives 409 `activation_in_progress` / `incarnation_conflict` with no session grant and must re-read status. Do not advance again automatically.

Allowed prior state is explicit: initial `Enrolled` with incarnation `0`; or `Quiescing`/`Fenced` with a current, durable host-fence receipt and recovery checkpoint that inventories unresolved effects and preserves their occupancy debt. A previous `Recovering` incarnation can be replaced only after that same fence/checkpoint transition. `Active` is never displaced directly. Heartbeat age, API-key possession, certificate possession, a self-reported lock boolean, or a lock-file timestamp cannot produce a fence receipt. The evidence authority and verification mechanism must be approved before the endpoint is implemented; until then, a crashed or uncertain prior manager remains blocked.

The initial enrolled identity uses expected incarnation `0` and gets incarnation `1` in `Recovering`. A same-process timeout retries identical bytes and the same `ActivationRequestId`; it returns the exact same assigned incarnation and receipt and cannot allocate again. A different body with the same request ID conflicts. The local installation lock blocks cooperating managers on one host, while the API CAS blocks concurrent activation claims. The separately verified host-fence receipt prevents an active/uncertain identity from being displaced by a request from another host.

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

## Proposed implementation slices and ownership

Keep the future implementation cohesive around the existing Node/API/Infrastructure feature boundary:

1. **Persistence/API contract:** propose `NodeManagement` plus activation/recovery attempt state in `src/Automaton.Infrastructure/Entities/` and the `ApplicationDbContext` configuration/migration/snapshot; add typed activation request, receipt, status, and recovery grant under Node enrollment/management API feature ownership; implement conditional CAS handlers and exact listener allow-list registration. Do not modify the existing enrollment bootstrap semantics.
2. **Node local activation:** add a Node-owned activation coordinator, protected identity/checkpoint store, lock abstraction, strict response validator, and startup state machine under `src/Automaton.Node/`. Keep pure decisions separate from OS lock and filesystem adapters. No Runner execution service registration, broker client/consumer, Docker package/socket, workflow Steps, or host-effect type enters the Node dependency graph.
3. **Recovery integration:** add current-session evidence/receipt API contracts using the N0-B existing operation store and current reporter binding. Keep recovery evidence separate from original command bytes and preserve exact terminal bytes/digest. No action executor, stop adapter, or host inventory implementation in this N1-A activation/authentication implementation slice.
4. **Architecture and lifecycle validation:** extend Node architecture tests to assert absent broker/Docker/executor/tenant-code dependencies and no consumer registration; test listener route allow-lists at application level. Preserve all existing Runner registrations, behavior, and tests.

Current source owner map in N0-D identifies `NodeEnrollmentHandlers.cs`, `NodeEnrollmentApiExtensions.cs`, enrollment entity configuration, `ApplicationDbContext`, and `src/Automaton.Node`. Final implementation records must revalidate exact paths and classes against their branch before code edits; this specification intentionally defines behavior rather than claiming concrete new symbols already exist.

## Required acceptance tests

These are required future tests, not performed by this documentation task. Fast deterministic tests cover pure state decisions; real database, process-lock, restore-authority, and host tests establish their respective guarantees. No test may use production credentials or mutate a production host.

| Test | Fixture and required assertions |
| --- | --- |
| `ActivationRequestIdempotency` | MySQL, two independent DbContexts/connections. Same request ID and canonical bytes racing across API instances produces one incarnation and byte-identical receipt. Same ID/different digest is 409 and row unchanged. |
| `ActivationIncarnationCasRace` | MySQL independent contexts, two distinct attempts with the same expected incarnation. Exactly one conditional update commits; loser gets conflict; one current Recovering incarnation; no Active grant. Repeat at `Int64.MaxValue` boundary and ensure overflow fails closed. |
| `ActivationCommitResponseLoss` | Inject response loss after commit. Same live process retries same attempt and receives same receipt; assert incarnation increments once and attempt/receipt row is immutable. |
| `ActivationLocalFsyncFailureRecovery` | Fault local receipt/checkpoint write after API commit, restart with a new attempt. Assert no Active startup, old attempt unchanged, next incarnation is RecoveryOnly only after valid lock/recovery proofs, and no effect/broker/Docker path starts. |
| `ConcurrentEnrollmentThenActivation` | Real migrated MySQL; competing bootstrap/activation contexts. One enrollment binding and one initial incarnation; enrollment credential recovery does not activate or increment it. |
| `ActivationRealmAndCredentialBinding` | Wrong realm, Node, InstallationId, certificate pin, API key, credential revision, revoked enrollment, retired credential and wrong listener are rejected before state mutation. Include valid current bindings as controls. |
| `RestoreEpochAuthorityMatrix` | Fake verifier unit tests plus isolated authority integration fixture: committed epoch accepted; stale DB/local rejected or recovery-only; DB-ahead, register unavailable, foreign realm, uncommitted reservation, altered signature, lost/cloned register quarantine; no epoch inferred from DB. |
| `RestoreR0R5Rebind` | Disposable restored MySQL/data and isolated external register: fault before/after every R0–R5 boundary. No old writer, CAP message, result, or intake retry can become a new effect; missing request history returns `restore_history_unknown` before allocation. |
| `InstallationLockProcessOwnership` | Supported target Linux filesystem in disposable VM. Two real processes: one holds stable lock, second cannot acquire; graceful release and process crash release OS lock; inode is never unlinked/replaced; wrong owner/mode/symlink/unsupported filesystem is denied. Demonstrate that reacquisition still enters RecoveryOnly. |
| `RecoveryOnlyNoEffectDependencies` | Resolved DI graph and architecture scan prove no broker consumer, Docker client/socket, workload executor, workflow/custom-step runtime, or host-effect adapter registration. Requests on ordinary/bootstrap listeners reject before any handler invocation. |
| `RecoveryOriginalIdentityAndEvidence` | N0-B cases for never-issued successor, accepted uncertain operation, exact saved terminal import, lost receipt replay and conflicting terminal bytes. Original tuple/hash and reservation are unchanged; current reporter is explicit; effect/body invocation count remains zero. |
| `RecoverySettlementBlocksActive` | N0-B/N0-C fault fixtures for unresolved asynchronous engine effect, surviving owned workload, missing/corrupt journal, stale local fence, unsettled old manager, policy/storage/cgroup mismatch, occupancy debt. RecoveryOnly persists; Active CAS has zero successful transitions. No timeout frees occupancy. |
| `RecoveryPerAssignmentActivation` | Multiple assignments with one unresolved scope. Verified unaffected scope may become ready only if N0-C permits; affected assignment remains paused. Node-wide heartbeat cannot activate all assignments. |
| `ActivationRevocationAndEpochChange` | Revoke credentials or advance authoritative epoch between reservation, recovery and Active CAS. Every stale completion is rejected; local gate closes; next start follows recovery flow. |
| `ActivationShutdownAndRestart` | Cancellation and graceful stop keep lock until durable quiescing/checkpoint. Crash at each checkpoint resumes RecoveryOnly; no stale process can satisfy current incarnation after restart. |

Required verification when implementation is separately authorized: affected API/Infrastructure/Node builds before and after edits; complete `Automaton.All.sln` nonincremental Debug build; affected Node/API/Architecture tests; actual migrated disposable MySQL concurrency suite; supported Linux lock/restore fixture before claiming those guarantees. NuGet advisory service availability must be reported separately. Builds or fakes do not substitute for external security review or live operational evidence.

## Unresolved prerequisites and stop conditions

The following items block enrollment/activation implementation or its privileged consumers until their named evidence exists:

- **Independent approval:** independent security/operations approval of S-B1 and the affected N0 identity, listener, realm/restore, local fence, operation settlement, storage/cgroup, and rollback contracts. Prior design review closed the listed enrollment/authentication findings; it did not approve live operations or privileged effects.
- **Issuer and proxy operations:** named certificate issuer/key custodians, protected backup/recovery drill, Traefik-to-API client certificate identity and bounded pin rotation on both API colors, direct-origin denial, and deployed-path tests. mTLS and certificate handling remain a proposed design pending this review/evidence.
- **Restore authority:** deployed independent register location, signer custody/recovery, authenticated client/verification path, R0–R5 operator process, and a successful disposable DB rewind/restore rehearsal. If authority is unavailable, cloned with DB, or cannot prove old writers fenced, startup remains quarantined.
- **Host manager fence:** concrete owner for host-wide lock location and service/restart controls; an independently verifiable receipt/authority for fencing the prior incarnation; proof that all overlapping legacy managers and resurrection sources are fenced. A Node-supplied lock claim or the installation lock alone cannot satisfy this API prerequisite.
- **Effect settlement and occupancy:** N0-C evidence for outstanding asynchronous effects, exact resource ownership, storage/cgroup adoption, quota accounting and rollback. Until separately authorized, Node recovery code stays free of Docker/host-effect adapters.
- **Supported platform:** decide the initial OS/filesystem profile and demonstrate stable OS lock, directory ownership, atomic replacement/fsync, journal durability and crash behavior on that actual profile. Unsupported/network filesystems remain ineligible.
- **Schema and transactional guarantees:** reviewed NodeManagement/attempt/checkpoint schema, migration/backfill behavior for existing enrollment rows, retention/tombstones, MySQL CAS/index semantics, and independent-context race tests.
- **Current identity deployment:** selected Traefik image/version fixture and deployed route isolation/proxy peer verification remain deployment evidence, not inferred from source tests.
- **Scope readiness:** N0-D still records N0 exit and full N1-A as blocked pending review/evidence. N1-B command consumption, N1-C resource inventory/repair, N1-D install/restart, and all N2+ effects remain separate dependent scopes.

If any prerequisite is absent, the implementation must fail closed or remain preparation-only. No timeouts, cached credentials, DB projection, successful enrollment, local lock reacquisition, or passing unit test may substitute for the missing authority/evidence.

## Review disposition

This specification is ready for design review. It does not implement runtime behavior or claim test, security, host, restore, deployment, or production acceptance. Do not begin activation/authentication implementation until this specification and the outstanding prerequisites have been reviewed and the user separately authorizes the implementation slice.
