# Critical analyzer findings remediation plan

Status: batches 1–3 were merged in [PR #302](https://github.com/rzvdaniel/automaton/pull/302) (merge commit `c8b5899a`). The five rules `S4830`, `S5445`, `S4502`, `S2930`, and `S2190` are build errors on `main`. Batch 4 is being handled in smaller, reviewable slices; [PR #303](https://github.com/rzvdaniel/automaton/pull/303) updates request-scoped cancellation and replaces avoidable raw-header/query reads with model binding. Its review follow-up makes default-plan replacement atomic and wraps user-plan assignment, Hosted Runner state/provisioning events, and audit writing in the CAP-backed transaction with a request-independent completion token. The current Runner cancellation slice addresses `HubService` heartbeat ownership and shutdown. Remaining Runner background cancellation findings and the request-size dispositions are still open. `S6444` remains planned. This document does not authorize a production deployment.

Baseline: `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` on 2026-09-23, starting at commit `44b84cbf`. The build succeeded with 1,978 warning emissions. Counts below are distinct diagnostic locations after removing repeated output lines; rerun the command before each batch. The standalone analyzer reports potential defects, so each finding needs a code and behavior review before it is treated as confirmed.

## Goal and ordering

Remove confirmed security and reliability risks first, then turn the resolved rules into build errors where that produces a reliable safeguard. Keep the existing `S1764` and `S2757` error gates. Do not promote a rule that still has unexplained historical findings, silence a whole rule to pass CI, or change runtime behavior just to make a warning disappear.

| Priority | Rule and inventory | Why it is here | Initial decision |
| --- | --- | --- | --- |
| 1 | `S4830`, 4 locations | High security impact: disabled server certificate validation | Fix the unconditional SMTP bypass first; verify and replace development bypasses. |
| 1 | `S5445`, 2 locations | High security impact: temporary file creation and possible path races | Review file creation, permissions, execution, and cleanup. |
| 1 | `S4502`, 2 locations | High security impact: antiforgery disabled | Restore protection on the logout POST; assess the GET-only error page separately. |
| 2 | `S2930`, 2 locations | Blocker reliability classification: cancellation sources not disposed | Review Runner lifecycle and ownership before changing disposal. |
| 2 | `S2190`, 1 location | Blocker reliability classification: apparent endless loop | Verify the lease renewal loop exits through cancellation. |
| 3 | `S8949`, 62 locations | High reliability impact: cancellation token not propagated | Triage production operations by risk; avoid blind token propagation into durable work. |
| Next | `S5693`, 5 locations; `S6932`, 11 locations | Medium security impact; request size and raw-body handling | Validate actual limits and authentication/signature boundaries; handle genuine gaps. |
| Later | `S6444`, 35 locations | Low security impact: regex timeouts | Prioritize regexes operating on untrusted input after higher-impact work. |

These priorities use the rule metadata from SonarSource and the inspected code paths, not the number of warnings. Other diagnostics may also be important; rerun and review the inventory when analyzer versions change. `S8949` is numerous, but a bulk mechanical fix could violate the platform's retry and cancellation semantics.

## Batch 1 — Certificate validation (`S4830`)

### 1A. SMTP, immediate target

`src/Automaton.Infrastructure/Email/EmailService.cs` sets `ServerCertificateValidationCallback` to always return true before connecting and authenticating. That bypass is unconditional. Inspect actual email configuration and deployment assumptions before changing it, including the configured host, TLS mode, and certificate trust. Do not copy live credentials into tests or documents.

Implementation approach:

1. Remove the unconditional callback and use the library's normal certificate validation. Preserve the existing `SecureSocketOptions` contract unless tests show it permits an unintended cleartext authentication path.
2. If development uses a private certificate authority, configure trust in the development environment or use a narrowly scoped, explicit development mechanism. Do not silently fall back to accepting every certificate in production.
3. Add focused tests for a trusted certificate, an untrusted certificate, and the configured TLS modes. Where a real SMTP handshake cannot be exercised in unit tests, add an integration fixture or an injectable transport seam with a meaningful validation assertion.
4. Verify that a certificate failure is observable without logging credentials or message contents. Document any administrator-visible email configuration or failure behavior under the admin-docs skill.

Acceptance: an untrusted SMTP server certificate cannot be accepted in production; valid configured mail still sends; `S4830` no longer reports this location.

### 1B. Runner development bypasses

`src/Automaton.Runner/Program.cs` has two development-mode bypasses: a process-wide `ServicePointManager` callback and an HTTP-client-factory handler. `src/Automaton.Runner/Services/HubService.cs` adds a connection-scoped bypass under a development environment check. Inspect how the Runner receives API, SignalR, and local development certificates. A string comparison to `Development` is not itself a trust boundary.

Implementation approach:

1. Replace process-wide acceptance first. Prefer trusting the local development CA or certificate at the host/container boundary rather than bypassing validation in application code.
2. Trace all HTTP and SignalR clients that inherit the current bypasses, including callbacks created by reconnects. Do not weaken production validation or assume a single API instance.
3. Test development with its intended certificate setup, production with a valid certificate, and production with an invalid certificate. Check that a development configuration cannot leak into a production Runner image or deployment inadvertently.
4. If a narrowly scoped development exception remains unavoidable, document its exact environment guard and endpoint scope, and obtain a reviewed rule-specific disposition. Do not suppress `S4830` for the whole Runner project.

Acceptance: production Runner connections reject invalid certificates, normal development connections work through an explicit trust configuration, and the three Runner findings are resolved or individually justified. After all four findings are dispositioned, promote `S4830` to `error` in `.editorconfig` and prove both a clean full build and a deliberately violating build.

Runner code changes require the generic-runtime and distributed-platform checks. A production Runner image, if needed later, must come from the repository's Azure pipeline and follow the production-deployment skill. Do not deploy as part of this cleanup plan.

## Batch 2 — Temporary files and antiforgery (`S5445`, `S4502`)

### Temporary files

- `src/Automaton.Cli/Services/RunnerInstallService.cs` writes content to a temporary path, then invokes `sudo cp`. Check whether another local process could read or replace that file before the elevated copy. Use a private directory, restrictive permissions, atomic creation, and cleanup on cancellation and failure. Preserve the current installation and privilege boundaries.
- `src/Automaton.Core.Scripting/Engines/Batch/BatchScriptEngine.cs` appends `.bat` to a path returned by `GetTempFileName()` and writes executable content there. The appended path is a different file, so check its creation race, execution permissions, and cleanup. Replace this with an owned, safely created executable path and a `try/finally` lifecycle. Do not merely substitute `GetRandomFileName()` without secure creation.

Test file ownership and cleanup on success, failed process start, cancellation, and concurrent runs. Check the operating systems where each feature actually runs. Acceptance: neither path can be swapped or exposed between creation and use; temporary artifacts do not remain after failure; both findings are resolved. Then promote `S5445` to an error if no legitimate case remains.

### Antiforgery

- `src/Automaton.Api/Areas/Identity/Pages/Account/LogOut.cshtml` disables antiforgery and performs sign-out on POST. The existing login partial submits a Razor form. Remove the exemption after confirming the form emits a token; verify valid logout succeeds and a forged POST is rejected. Check all other callers of the page.
- `src/Automaton.Api/Pages/Error.cshtml.cs` applies the exemption to a page currently exposing `OnGet`. Check whether it is needed at all. Prefer removing an unnecessary attribute; confirm ordinary error-page GET still works and no POST behavior is accidentally introduced.

Acceptance: cookie-authenticated state-changing POSTs have antiforgery protection; error display still works. Resolve or individually justify both locations, then promote `S4502` to an error. If user-visible behavior changes, update the relevant Admin documentation.

## Batch 3 — Runner and deployment lifecycle reliability (`S2930`, `S2190`)

`HubService` creates `retryTokenSource` for a background retry loop and cancels it on disconnect, but disposal and reconnect ownership require review. `RunnerRuntimeContainerManager` creates `supervisionCts`, cancels it during `StopAsync`, and awaits the task. Determine the correct disposal point after all consumers have stopped; avoid disposing a source while another task still uses its token.

`DeploymentOperationService.RenewLeaseAsync` uses `while (true)`, but `ExecuteAsync` creates a cancellation source, cancels it in `finally`, and awaits the renewal task. This may be an intentional long-running loop rather than runaway recursion. Verify stop behavior under normal completion, caller cancellation, exceptions, API process interruption, and ownership change. Confirm the lease update retains its conditional operation/attempt predicate and that an operation cannot be completed by a stale owner. Do not add an arbitrary retry count or terminate renewal merely to clear `S2190`.

Acceptance: tests prove cancellation and disposal without race or resource leak, and renewal cannot continue after its owner stops. Resolve each finding where appropriate. If `S2190` is a false positive for the deliberate cancellation loop, record a narrowly scoped explanation and suppression only after the behavioral tests pass. Promote `S2930` and `S2190` to errors only after the entire solution is clear or individually justified, and after a deliberate violation is shown to fail in the standalone analyzer.

## Batch 4 — Cancellation and request boundaries (`S8949`, `S5693`, `S6932`)

### Current slice — request-scoped cancellation and model binding

The in-progress slice propagates request or operation cancellation through the affected admin plan operations, Runner deployment preparation and process stop paths, API response writes, and selected app-update/provider calls. Default-plan changes clear the previous default and save the replacement in one serializable transaction. Plan creation and its audit write share a relational transaction. User plan assignment, Hosted Runner preparation/disable state, CAP provisioning publication, and audit writing share the CAP-backed transaction and use an independent cancellation token after request validation, so a disconnected request does not leave these operations half complete. The API CAP executor disables CAP auto-commit and commits only after the complete operation succeeds; failures explicitly roll back. The file-upload and tunnel registries cancel their timeout continuations when the associated request ends. `ApplicationBuildExecutor` keeps its bounded output-drain cleanup independent of cancellation because the reader cancellation source is only canceled after the drain timeout. The queued app-action dispatch likewise uses an explicit independent lifetime because it represents persisted work that must continue after the initiating request disconnects.

Test-run IDs, runtime-auth headers, Runner enrollment credentials, MCP API-key headers, and the restricted-app runtime token are bound as action parameters. The Stripe webhook retains a raw-body read because signature validation depends on the exact payload; the read now observes request cancellation. Broader `HubService` and `RunnerHostService` findings still require lifecycle-by-lifecycle review before any token is propagated into reconnects, file transfers, or durable callbacks.

`S5693` currently points at actions that already enforce explicit API request caps: 50 MiB for archive import and deployed-file ZIP upload, and 200 MiB for app-file uploads, with matching multipart parser caps on the latter. These are intentional product limits rather than missing request limits. The analyzer warnings remain pending a separate deployment-edge review; do not shrink the product caps just to clear the diagnostic.

This slice does not promote `S8949`, `S5693`, or `S6932` to build errors. The remaining diagnostic inventory needs a fresh build and a documented disposition before any rule is considered for promotion.

The latest focused Debug builds of the API test project and Runner project show no remaining API `S8949` or `S6932` findings in this slice. A fresh Runner build before the heartbeat lifecycle slice reported 32 unique `S8949` locations; afterward it reports 30: 17 in `RunnerHostService` and 13 in `HubService`. The heartbeat loop now uses its connection-lifetime token for sends, and stop/reconnect paths cancel and await the loop before disposing its token source. The API still reports the five `S5693` locations described above. This is a partial resolution of the original 62-location cancellation inventory; do not treat the rule as cleared.

For the 62 `S8949` locations, first group by operation owner: HTTP request, durable deployment/message consumer, Runner background loop, and test. Review the production paths with side effects first. Propagate cancellation when it safely stops a request-scoped operation. Use an explicit independent lifetime when work must survive request cancellation, and test retry/partial-completion behavior. Do not treat `CancellationToken.None` as a mechanical fix. Work through small feature batches, not one repository-wide edit. Promote the rule only after remaining findings have a reviewed disposition and the standalone gate is proven to fire.

`S5693` flags five upload-related attributes in `AppsController` and `AppFilesController`, although the inspected actions already declare `RequestSizeLimit` values and the file endpoints also declare multipart limits. Verify effective limits at the API, form parser, runtime proxy, and edge, including oversized chunked requests. Distinguish an analyzer limitation from an actual unbounded path; preserve configured product limits. Do not reduce limits solely to clear the warning.

`S6932` flags eleven raw-request reads, including the Stripe webhook. Some consumers need exact raw bytes for signature validation or protocol handling. For each location, record why raw access is necessary, whether size limits apply before buffering, how signatures or credentials are checked, and what cancellation does. Add model binding only where it preserves the wire contract. Resolve genuine validation gaps; scope any analyzer exception to a reviewed endpoint.

Acceptance: effective size and authentication limits are demonstrated, request and durable operation lifetimes remain correct, and any unresolved warning has an explicit rationale. Promote medium-impact rules only when the remaining set is small and the analyzer proves useful in this codebase.

## Verification and gate rollout

For each batch, capture a before/after diagnostic list with `dotnet build <affected-project.csproj> -c Debug --no-incremental -v minimal`, then run the affected behavioral and architecture tests. Finish with `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` and `git diff --check`. Build success alone does not establish the security behavior; use the negative tests described above.

For a promoted rule, add `dotnet_diagnostic.<rule>.severity = error` to `.editorconfig`, verify the full solution passes in Debug and Release, and prove a disposable deliberate violation fails. Keep the normal CI build path and architecture tests active. The standalone package does not supply a hosted quality gate or automatically rank warnings by severity; the reviewed priority table is the current ordering mechanism.

Keep the batches independently reviewable. Any administrator-visible change needs the admin-docs skill. Runtime, Runner, and distributed changes need the generic-runtime and distributed-platform skills; Runner image release or production operations need the production-deployment skill and a separate explicit deployment request.

## References

- [Current analyzer baseline](analyzer-priority-baseline.md)
- [Repository coding contract](../../AGENTS.md)
- [Sonar rule impact model](https://docs.sonarsource.com/sonarqube-cloud/standards/managing-rules/rules)
- [Certificate validation rule S4830](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S4830.json)
- [Temporary file rule S5445](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S5445.json)
- [Antiforgery rule S4502](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S4502.json)
- [Cancellation rule S8949](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S8949.json)
- [Request size rule S5693](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S5693.json)
