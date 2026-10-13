# N1-A — durable local identity and activation client implementation

Status: implementation submitted for review in source commit `711c925863102340d0e92e4dacae1231444ee03d`; operational activation remains disabled. This implements the bounded scope approved by the reviewed specification `73b4b6e5`.

## Implemented boundary

The Node installation lock retains a validated state-directory handle. Identity, certificate DER, private key, API key and activation checkpoint are accessed relative to that handle. The state directory must be root-owned mode `0700`; state files must be root-owned regular single-link files mode `0600`. Writes use an exclusive temporary file (`O_EXCL = 0x80`), file flush, handle-relative atomic rename and directory flush. Installed certificate/key binding, P-256 profile, identity realm/Node/install binding, canonical manifest/checkpoint shapes and content hashes are revalidated before use. Invalid or missing state leaves startup in `RecoveryRequired`.

The checkpoint keeps the canonical recovery view and its digest separate from the mutable activation-intent ledger. A process-start `ActivationRequestId` and exact request body are persisted as `Prepared`, then advanced durably to `ResponseUnknown` immediately before transport. Transport retries use the same request ID and exact bytes. Receipt persistence is immutable; readback can settle a lost response only when the API's current request ID, request digest and receipt match the retained intent. The real-store integration tests now exercise identity/checkpoint file writes and a three-start rollover that retains potentially-sent requests across reopened lock/store instances.

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
| `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` | Passed, 0 errors, 1,257 repository warnings. Existing NU1900 vulnerability-feed and SSH.NET NU1903 warnings remain. |
| `git diff --check` | Passed. |

The two new real-store acceptance tests—protected identity/checkpoint writes and three-start intent rollover—were added, but not run on Linux. This Windows environment cannot execute the Linux/root lock contract. The automatic approval review rejected running repository tests as root in Docker under the current Docker-disabled boundary; no alternate container route was used. Their Linux acceptance remains open.

## Remaining gates

The Node local state and API readback are implementation building blocks, not an activation release. A reviewed production recovery-authority adapter, security/operations approval, selected authentication deployment contract, Linux protected-file acceptance and deployed-path verification remain outstanding. Startup stays `RecoveryRequired`; no workload admission or privileged path is enabled.