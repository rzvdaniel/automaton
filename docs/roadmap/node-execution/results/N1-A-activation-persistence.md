# N1-A activation persistence, CAS and retry receipts

**Status:** implementation submitted for review. This record covers the bounded persistence/API slice approved by specification commit `9dd14624`. It does not enable operational activation.

## Implemented boundary

The API persistence model adds `NodeManagement`, `NodeActivationAttempt` and `NodeHostFenceReceiptUse`, with migration `20261013095351_AddNodeActivationPersistence`. Activation requests use strict, bounded canonical contracts. The initial request uses expected incarnation `0` only for an enrolled identity with no current incarnation and allocates `1`. Subsequent requests require the positive current incarnation and use a conditional update over Node identity, realm, credential revision, lifecycle status, expected incarnation, expected `RecoveryEpoch` and fence generation.

The transaction validates an HFA host-fence receipt against the configured trust store, binds it to the activation request and consumes its identifier once. The activation attempt and immutable receipt commit with the incarnation transition. A byte-equivalent retry with the same request identity returns the stored receipt; reuse with different canonical request content conflicts. Successful transitions remain in `Recovering`; the API has no transition to `Active` in this slice.

The route is management-listener-only under the existing listener allow-list. The production epoch-authority adapter returns unavailable, and an absent HFA trust configuration is also unavailable. Those gates make the route fail closed until the external authority and reviewed operational configuration exist. No Node startup client/lock, recovery operation, broker consumer, Docker integration, workload execution or host effects were added; Runner remains unchanged.

## Acceptance evidence

This PR adds contract tests for the initial zero sentinel, strict canonical request decoding, HFA signature/binding validation and fail-closed defaults. MySQL integration tests use the actual migration and independent contexts for initial-incarnation competing CAS and HFA receipt/epoch CAS behavior. They require `AUTOMATON_TEST_MYSQL_CONNECTION_STRING` to point to a disposable MySQL database.

Review amendments add an enrollment-status predicate to the incarnation update, exact HFA algorithm and not-yet-valid time checks, real enrollment-then-activation migration application, an independent-context revocation race, identical-request races for initial and HFA restart, positive-incarnation races and transaction rollback/retry proof that leaves an HFA receipt unconsumed until commit.

Verification on the implementation branch:

- `dotnet build src/Automaton.Api/Automaton.Api.csproj -c Debug --no-incremental --no-restore -m:1 -v minimal` — passed, 0 errors (884 warnings, including existing dependency/analyzer warnings).
- `dotnet build src/UnitTests/Automaton.Api.Tests/Automaton.Api.Tests.csproj -c Debug --no-incremental --no-restore -v quiet` — passed, 0 errors (963 warnings).
- `dotnet test src/UnitTests/Automaton.Api.Tests/Automaton.Api.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~NodeActivationContractTests" -v minimal` — passed, 6 passed, 0 failed, 0 skipped.
- `dotnet test src/UnitTests/Automaton.Api.Tests/Automaton.Api.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~NodeActivation" -v minimal` — passed, 6 passed, 0 failed, 10 skipped. The 10 MySQL cases, including migration, same-request races, positive-incarnation race, revocation race and rollback/retry, skipped because no database connection is configured.
- `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` — passed, 0 errors (1,254 warnings; NuGet audit retrieval was unavailable and SSH.NET advisories were reported).
- `AUTOMATON_TEST_MYSQL_CONNECTION_STRING` is unset, and the user confirmed no disposable MySQL instance is available. The MySQL migration/race tests remain unexecuted. Docker was excluded from the implementation scope.
- `git diff --check` — passed.

## Remaining gates

This implementation is not operational acceptance. Configure and independently review the HFA trust material and production recovery-epoch authority in their owning operational slice. Node-local durable identity, installation-lock ownership, receipt persistence, recovery-only startup, assignment recovery and readiness admission remain later implementation work. Independent security/operations approval and deployed-path verification are required before rollout. No rollout or production configuration is authorized by this PR.
