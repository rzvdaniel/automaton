# N1-A — durable local identity and activation client implementation

Status: implementation submitted for review in source commit `58a986315bd8bb3d4e42aa42ef54966f078e195a`; operational activation remains disabled. This implements the bounded scope approved by the reviewed specification `73b4b6e5`.

## Implemented boundary

The Node installation lock now retains a validated state-directory handle. Identity, certificate DER, private key, API key and activation checkpoint are accessed relative to that handle. The state directory must be root-owned mode `0700`; state files must be root-owned regular single-link files mode `0600`. Writes use an exclusive temporary file, file flush, handle-relative atomic rename and directory flush. Installed certificate/key binding, P-256 profile, identity realm/Node/install binding, canonical manifest/checkpoint shapes and content hashes are revalidated before use. Invalid or missing state leaves startup in `RecoveryRequired`.

The checkpoint keeps the canonical recovery view and its digest separate from the mutable activation-intent ledger. A process-start `ActivationRequestId` and exact request body are persisted as `Prepared`, then advanced durably to `ResponseUnknown` immediately before transport. Transport retries use the same request ID and exact bytes. Receipt persistence is immutable; readback can settle a lost response only when the API's current request ID, request digest and receipt match the retained intent. Unsent intents may be abandoned only under the specification's pre-send rules; unresolved potentially-sent intents survive repeated process starts and cannot be silently replaced.

The Node assembly contains a strict HTTPS activation client that validates fresh readback, realm, identity, credential revision, recovery epoch, incarnation and immutable receipt contracts. The host does not compose or invoke it. The API adds an authenticated, read-only management-listener endpoint. It brackets a repeatable-read database snapshot with current external recovery-authority reads and returns `NoActivation`, current lifecycle state plus immutable receipt, or fail-closed unavailability. Production dependency injection still uses the unavailable authority implementation, so the endpoint cannot establish a production recovery view yet.

No mTLS implementation choice was made here; the proposed selection remains pending review. No API client is wired into Node startup. Enrollment, activation, Active transition, broker consumption, Docker access, workload execution and host effects remain disabled. No Runner behavior or production configuration changed.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build src/Automaton.Node/Automaton.Node.csproj -c Debug --no-incremental -v minimal` | Passed, 0 errors. NuGet vulnerability-feed retrieval reported NU1900 because `api.nuget.org` was unavailable. |
| `dotnet build src/Automaton.Api/Automaton.Api.csproj -c Debug --no-incremental -v minimal` | Passed, 0 errors; 889 repository warnings, including existing SSH.NET NU1903 advisories and analyzer/compiler warnings. |
| `dotnet test src/UnitTests/Automaton.Node.Tests/Automaton.Node.Tests.csproj -c Debug --no-build --no-restore -v minimal` | Passed, 40 passed, 0 failed. Linux-only lock scenarios reported conditional skip messages in this Windows environment; test summary reports 0 skipped. |
| `dotnet test src/UnitTests/Automaton.Architecture.Tests/Automaton.Architecture.Tests.csproj -c Debug --no-build --no-restore --filter "FullyQualifiedName~NodePreparationArchitectureTests" -v minimal` | Passed, 3 passed, 0 failed, 0 skipped. |
| `dotnet test src/UnitTests/Automaton.Api.Tests/Automaton.Api.Tests.csproj -c Debug --no-build --no-restore --filter "FullyQualifiedName~NodeActivationReadbackMySqlTests|FullyQualifiedName~NodeAuthenticationBoundaryTests" -v minimal` | Passed, 42 passed, 0 failed; 4 readback integration tests skipped because `AUTOMATON_TEST_MYSQL_CONNECTION_STRING` is not configured. These four tests still require disposable-MySQL execution. |
| `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` | Passed, 0 errors, 1,256 repository warnings. Existing NU1900 vulnerability-feed and SSH.NET NU1903 warnings remain. |
| `git diff --check` | Passed. |

The four skipped MySQL tests cover fresh `NoActivation` readback, exact immutable receipt readback, stale-authority `Fenced` status with the original `Recovering` receipt, and an authority change across the consistent snapshot. Their behavior is not claimed as database-verified by this change.

## Remaining gates

The Node local state and API readback are implementation building blocks, not an activation release. A reviewed production recovery-authority adapter, security/operations approval, selected authentication deployment contract, isolated MySQL acceptance, and deployed-path verification remain outstanding. Startup stays `RecoveryRequired`; no workload admission or privileged path is enabled.