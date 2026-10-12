# Execution contract for Node migration briefs

Read this file with ONE assigned slice, the [master plan](../Automaton_Staged_Master_Plan.md), root AGENTS.md and relevant repository skills. These are implementation instructions, not completed architecture evidence.

## Readiness and authority

Consult the [index and result records](README.md) for current readiness. Prerequisites refer to evidence-backed outputs, not merely merged brief files. A specification-ready proposal permits dependent design work; independent review remains required before implementing its privileged/tenancy/fencing consumers. Audit/design slices may proceed without production access: record missing live facts. Missing live inventory blocks only the dependent migration/release decision, not unrelated lab development.

Before coding, write a specification in the slice's results file containing:
1. Exact current HEAD, relevant paths/symbols, reused mechanisms and gap.
2. Owning service/model and durable source of truth.
3. Concrete request/command/result fields: types, required/null behavior, validation, authorization, bounds and error results.
4. Database keys/indexes/transactions; Node journal version and atomic persistence where applicable.
5. State transitions, generation checks, effect/acknowledgement order and recovery for each crash boundary.
6. Data/secret/privilege boundaries, resource admission/release and locality constraints.
7. Named test scenarios, fixture requirements and observable expected results.
8. Remaining decisions, consumer(s) blocked and independent review evidence.

Derive these details from preceding results and current source. Do not invent a second quota ledger, scheduler or transport. Resolve routine technical decisions autonomously within the approved architecture. A material unresolved product/security choice blocks its dependent code and should be surfaced clearly; do not repeatedly ask permission for routine edits.

Specification completion is a local task step within the assigned slice. Later briefs are provided now; only their repository-grounded contracts need refinement as prior work produces evidence. Independent review is required for privileged execution, tenancy, fencing, quota/accounting and migration contracts before consumer implementation/release. Do not claim a review that did not occur.

## Status and evidence

Status labels: authored/dependency-gated; audit-ready; specification-ready; implemented; tested; deployed; live-accepted; blocked. Track each separately in README and linked results. Tests do not imply deployment; deployment does not imply live acceptance.

The master plan is the scope authority. The removed historical managed-platform roadmap is evidence from Git history, not a missing prerequisite that should be recreated. Architecture docs must distinguish current behavior from proposal.

Store actual findings in results/<slice>-<topic>.md as specified in the brief. No empty success records: create a results document when work produces findings. Do not overwrite earlier evidence without retaining relevant provenance, limitations and release identity.

## Scope and operation rules

- Use existing checkout and preserve worktree changes. Do one assigned slice; split large coding changes into cohesive PRs with recorded subtask order.
- Reuse MySQL/CAP/RabbitMQ/.NET/Docker/Traefik and validated RM paths. No permanent dual executor fallback or unsolicited compatibility framework.
- Preserve account/app identity, local filesystem/XFS quota domains, source/live/data privacy and aggregate resource budgets.
- Node is privileged management, not a tenant execution process. Tenant builds/apps/Actions use constrained workloads without management credentials/socket.
- Duplicate/stale messages, cancellation, uncertain effects and control-plane outages must have explicit behavior. No exactly-once external effect claims.
- Do not perform production rollout, infrastructure changes, retained-data deletion or credential rotation merely because a brief includes an operational step. Follow explicit session authorization and repository deployment procedures.
- Tests using fault injection/destructive effects run only in declared disposable fixtures. Never fill/crash shared production hosts. Fixtures need explicit ownership/cleanup and bounded resources.
- No pricing, commercial expansion or new runtime integration in N0–N4.

## Verification

For docs-only work: git diff --check, relative-link review and source-evidence review; no unnecessary C# build.
For C# changes: follow AGENTS.md baseline/post-change affected-project nonincremental builds, all-project nonincremental build and relevant tests. Do not weaken analyzer gates or hide historical failures.
For Studio changes: type-check/build and repository visual guidance. For scripts: shell syntax checks and disposable lifecycle evidence as relevant.
Use real independent database transactions for admission/concurrency races and real isolated Linux/XFS/cgroup fixtures for kernel/storage guarantees. Fakes cannot prove kernel enforcement.
List exact commands, pass/fail/skips, unavailable checks and why. Record sanitized artifact/release identities without secrets.

## Required handoff

- Baseline and source/test owners.
- Scope/reuse/gap and specification readiness.
- Changes with purpose.
- Tests and evidence by code/test/deploy/live status.
- Duplicate/crash/cancel/stale/auth/quota/partial-state handling.
- Data compatibility and bounded rollback.
- Unverified scenarios, blockers, roadmap/index status and next smallest task.
- Commit/PR only if actually created.

## Invocation template

> Execute <slice> ONLY using its brief and EXECUTION-CONTRACT.md. Inspect current source and prior results, complete the specification step, then perform only the authorized audit/design/implementation scope. Preserve current worktree and data. Do not infer production authorization. Report the required handoff and update readiness from evidence.
