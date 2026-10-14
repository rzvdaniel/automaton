# N1-A — durable local identity and activation client implementation

Status: the bounded implementation approved by the reviewed specification `73b4b6e5` merged in PR #379 at `f524c2bd`. Positive-incarnation checkpoint synchronization follow-up is under review at commit `325c97b1`; operational activation remains disabled.

## Implemented boundary

The Node installation lock retains a validated state-directory handle. Identity, certificate DER, private key, API key and activation checkpoint are accessed relative to that handle. The state directory must be root-owned mode `0700`; state files must be root-owned regular single-link files mode `0600`. Writes use an exclusive temporary file (`O_EXCL = 0x80`), file flush, handle-relative atomic rename and directory flush. Installed certificate/key binding, P-256 profile, identity realm/Node/install binding, canonical manifest/checkpoint shapes and content hashes are revalidated before use. Invalid or missing state leaves startup in `RecoveryRequired`.

The checkpoint keeps the canonical recovery view and its digest separate from the mutable activation-intent ledger. A process-start `ActivationRequestId` and exact request body are persisted as `Prepared`, then advanced durably to `ResponseUnknown` immediately before transport. Transport retries use the same request ID and exact bytes. Receipt persistence is immutable; readback can settle a lost response only when the API's current request ID, request digest and receipt match the retained intent. Real-store tests cover protected identity/checkpoint writes, a three-start rollover that preserves an earlier `ResponseUnknown` while abandoning an unsent `Prepared` intent, readback receipt reconciliation across lock/store reopen, and injected failures before checkpoint file fsync, before rename and after rename before directory fsync.

The Node assembly contains a strict HTTPS activation client that validates fresh readback, realm, identity, credential revision, recovery epoch, incarnation and immutable receipt contracts. Its transport disables automatic redirects before requests are sent. The host does not compose or invoke it. The API adds an authenticated, read-only management-listener endpoint. It brackets a repeatable-read database snapshot with current external recovery-authority reads and returns `NoActivation`, current lifecycle state plus immutable receipt, or fail-closed unavailability. Receipt canonicalization matches the activation endpoint's per-property number serialization.

No mTLS implementation choice was made here; the proposed selection remains pending review. No API client is wired into Node startup. Enrollment, activation, Active transition, broker consumption, Docker access, workload execution and host effects remain disabled. No Runner behavior or production configuration changed.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build src/UnitTests/Automaton.Node.Tests/Automaton.Node.Tests.csproj -c Debug --no-incremental -v minimal` | Passed, 0 errors. Three NU1900 warnings because NuGet vulnerability data was unavailable. |
| `dotnet build src/Automaton.Api/Automaton.Api.csproj -c Debug --no-incremental -v minimal` | Passed, 0 errors; 889 repository warnings, including existing SSH.NET NU1903 advisories and analyzer/compiler warnings. |
| `dotnet test src/UnitTests/Automaton.Node.Tests/Automaton.Node.Tests.csproj -c Debug --no-build --no-restore -v minimal` | Passed, 41 passed, 0 failed. Windows test output printed conditional skip notices for Linux/root-only lock and real-store cases; those filesystem cases were not executed here. |
| Node activation-client/redirect tests | Passed, 11 passed, 0 failed. The loopback TLS redirect test confirmed the source receives the synthetic API key and the redirect target receives no connection. |
| Node dependency-boundary test | Passed, 1 passed, 0 failed. |
| API listener-boundary tests | Passed, 42 passed, 0 failed, 0 skipped. |
| Four `NodeActivationReadbackMySqlTests` | Passed against disposable MySQL 8.0.36, 4 passed, 0 failed, 0 skipped. The test connection was process-scoped; randomized test databases and the loopback-only container were removed afterward. |
| `dotnet test src/UnitTests/Automaton.Node.Tests/Automaton.Node.Tests.csproj -c Debug --no-build --no-restore -v minimal --filter "FullyQualifiedName~NodeLocalStateStoreIntegrationTests|FullyQualifiedName~NodeLocalStateStoreRecoveryFailureTests|FullyQualifiedName~NodeInstallationLockPathValidationTests|FullyQualifiedName~NodeInstallationLockProcessTests|FullyQualifiedName~NodePreparationDependencyTests"` | Passed in Linux x64 as root, 11 passed, 0 failed, 0 skipped. Includes actual lock-process termination/restart, protected-file writes, unsent-intent rollover, receipt reconciliation and checkpoint write-failure stages. |
| `dotnet test src/UnitTests/Automaton.Architecture.Tests/Automaton.Architecture.Tests.csproj -c Debug --no-build --no-restore -v minimal --filter "FullyQualifiedName~NodePreparationArchitectureTests"` | Passed in Linux x64 as root, 3 passed, 0 failed, 0 skipped. |
| Linux test container | Disposable `mcr.microsoft.com/dotnet/sdk@sha256:1330d3c4144aec2695036a125d9a3b49a1fc555c9024c981248fa83d40fa8b23`; workspace bind-mounted read-only, `--network none`, private `/tmp` and `/run` tmpfs for test state, no privileged mode or Docker socket, and only container-local `CHOWN`/`DAC_OVERRIDE` capabilities for ownership-validation cases. `--rm` removed the container; tmpfs test state disappeared with it. |
| `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` | Passed, 0 errors, 1,256 repository warnings. Existing NU1900 vulnerability-feed and SSH.NET NU1903 warnings remain. |
| `git diff --check` | Passed. |

The Linux acceptance was run against source commit `c236e920b9f1c615b6cf2258a6472ed7dce3a7fc`. The focused selection included actual abrupt process termination for installation-lock recovery and passed with no skips. The write-stage fault injection is test-only and does not change production I/O behavior.

## Positive-incarnation checkpoint follow-up

The merged implementation advanced the checkpoint's top-level incarnation after persisting an activation receipt but left the embedded recovery view at its genesis incarnation. After a restart, that mismatch prevented preparation of the next activation request. Commit `325c97b1a932392a657b36664bd78c83ada28e2c` updates the embedded recovery view's epoch/incarnation with the checkpoint tuple, recomputes both nested and outer digests, and rejects tuple divergence during envelope validation. Its regression persists an incarnation-1 receipt, reopens the protected store, validates both tuples and the recovery-view digest, then prepares a subsequent request with `expectedIncarnation=1`.

Independent review of exact commit `325c97b1a932392a657b36664bd78c83ada28e2c` found no blocker. Independent QA used that exact source revision and reported:

| Check | Result |
| --- | --- |
| `dotnet build src/Automaton.Node/Automaton.Node.csproj -c Debug --no-incremental -v minimal` | Passed, 0 warnings/errors. |
| `dotnet build src/UnitTests/Automaton.Node.Tests/Automaton.Node.Tests.csproj -c Debug --no-incremental -v minimal` | Passed, 0 warnings/errors. |
| Linux `dotnet test src/UnitTests/Automaton.Node.Tests/Automaton.Node.Tests.csproj -c Debug --no-build --no-restore -v minimal` | Passed, 99/99, 0 failed/skipped; includes positive-incarnation receipt reconciliation and local filesystem cases. |
| Linux `dotnet test src/UnitTests/Automaton.Architecture.Tests/Automaton.Architecture.Tests.csproj -c Debug --no-build --no-restore -v minimal` | Passed, 10/10, 0 failed/skipped. |
| `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` | Passed, 0 errors, 1,254 existing warnings including SSH.NET NU1903 advisories. |
| `git diff --check` | Passed. |

The disposable Linux test container used `mcr.microsoft.com/dotnet/sdk:9.0`, read-only source and package mounts, no network, privileged mode, Docker socket or production mounts, bounded 2 CPU/2 GiB/256 PIDs, and private `/run` and `/tmp` tmpfs. It was launched with automatic removal and verified absent afterward. No MySQL, broker, workload, enrollment authority or production path was exercised. This follow-up test does not simulate power loss or forced termination during every individual filesystem syscall. The implementation remains uncomposed by the Node host and does not enable activation or host effects.

## Remaining gates

The Node local state and API readback are implementation building blocks, not an activation release. A reviewed production recovery-authority adapter, security/operations approval, selected authentication deployment contract, Linux protected-file acceptance and deployed-path verification remain outstanding. Startup stays `RecoveryRequired`; no workload admission or privileged path is enabled.
