# Automaton — archived reference backlog

Archived 2026-10-11 from the Node-first master plan. This document preserves historical research and test ideas only; it is not an implementation roadmap. The active [Automaton_Staged_Master_Plan.md](Automaton_Staged_Master_Plan.md) takes precedence.


The following historical briefs retain useful detailed tests and research methods. Their old priorities, dependencies, Runner-specific wording and suggested first tasks are NOT the execution order. N0–N4/P1–P3/R1–R2 above take precedence. Port remaining requirements to Node ownership; do not build features against the retired topology. They remain incomplete unless verified in the canonical roadmap.

<!-- SOURCE: 02-m3-usage-visibility.md -->

# S02 — Finish M3: accurate limits, usage and admission diagnostics

**Priority:** REQUIRED for credible managed hosting. **Depends on:** S01 authoritative quota/reservation contracts. **Maps to:** canonical M3.

## User story

In Studio, a customer can answer: **How many apps do I have? How many are running? What resources are allocated, actually applied and used? Is a storage sample fresh? Why was my deployment rejected?** An operator can inspect the same truth via Admin/API.

## S02-A — Audit measurement authorities and schema

1. Trace `AccountStorageAllocation`, host/filesystem identity, XFS project ID, Hosted Runner reservation snapshots, usage reporting and API responses. Identify source-of-truth fields versus telemetry snapshots.
2. Inventory existing API/Studio/Admin components and tests. Do not create a second quota authority, new metrics database or duplicate schema if existing models suffice.
3. Define a small response contract distinguishing `requested`, `applying`, `applied`, `blocked`, `failed`, plus `observed usage`, `verifiedAt`, `sampleSource`, and `freshness`.
4. Write an explicit glossary: reserved CPU/memory vs measured usage; filesystem allocated bytes vs logical sizes; durable data vs static/control-plane assets vs Docker/system overhead.

**Exit:** contract design, source mapping, existing coverage and minimal next PR.

## S02-B — API correctness and sample fencing

1. Return existing applied limits and policy revision, not merely latest requested plan.
2. Identify one storage sample by durable `(filesystem/storage domain, project ID, policy/verification revision)` as appropriate; prevent double counting when several runners report the same quota domain.
3. Reject or ignore duplicate, older and mismatched verification updates. Don't turn `stale`, `unavailable` or `error` into `0`.
4. Ensure stale usage cannot authorize quota shrink, placement or any safety-sensitive operation. Inability to measure must produce a deliberate blocked/retryable state.
5. Bound history and endpoint pagination. Keep Admin diagnostics guarded by existing permissions.

**Test cases:** out-of-order measurements, duplicate domain reports, deleted/replaced runner reports, denied shrink on stale usage, cross-account denial, no samples, clock/freshness boundary, JSON/API contract changes.

## S02-C — Studio/admin presentation

1. Display saved apps versus running apps; active/pending resources and verified storage usage; time of last credible sample; plainly labeled excluded categories.
2. Surface actionable messages for `insufficient capacity`, `stale measurement`, `quota exceeded`, `host offline`, `cleanup pending` and `policy not applied`.
3. Reuse existing Vue/PrimeVue design and typed API service pattern. Follow `.agents/skills/studio-design/SKILL.md` for visual proof, plus OpenAPI type generation where needed.
4. Never display a green 'healthy' value solely because the API returned an empty field or zero placeholder.

**Test cases:** API DTO tests, Vue type-check/build, relevant UI/component tests, accessibility and small viewport, unknown/failure rendering.

## S02-D — Documentation and acceptance

Publish a clear included/excluded resource ledger, definition of verification age and the difference between plan, applied quota and observed usage. Compare UI with one verified Linux XFS measurement and one intentionally stale/unavailable test. Do not change marketing/pricing yet.

## Non-goals

No live billing engine, per-second usage invoices, cross-provider accounting, Prometheus replacement, organization-billing migration, speculative distributed cache or global storage redesign.

## Suggested first Luna prompt

> Execute **S02-A only**: find existing sources of quota, applied policy and telemetry, write the smallest durable API/data contract and precise tests, and stop before coding broader S02. Follow the stage handoff template.

## Stage exit gate

All user-visible numbers are traceable to authoritative values or explicitly marked telemetry; concurrency/stale/replay handling is tested. Studio never reports unknown as healthy zero.

---

<!-- SOURCE: 03-m5-app-lifecycle-and-static.md -->

# S03 — Finish M5: dependable app publishing, actions and shared static hosting

**Priority:** REQUIRED. **Depends on:** S01 resource safety; can coordinate with S02, but keep PRs independent. **Maps to:** canonical M5.

## Product journey

A developer builds locally with any AI agent, uses the CLI/MCP to deploy, views the app in Studio, updates it, runs an Action, sees logs, reverts or deletes safely, and can repeat this on managed and permitted BYO infrastructure. Simple static sites must not require a permanent app process.

## S03-A — Read-only lifecycle matrix and detection policy

1. Inspect generic runtime contract and current static publishing, CLI and Studio paths. Read `.agents/skills/generic-runtime/SKILL.md` and distributed/platform skills.
2. Build a matrix for: static-only, HTTP OCI API with volume, Actions-only, static+backend as one project if currently supported, and BYO. For each: create/import/fork, upload, build, deploy, health, publish/unpublish, Action, restart, update, rollback, cleanup/delete.
3. Record **supported**, **unsupported**, **broken** separately. Don't invent a multi-component manifest or silently promise mixed Vue + .NET components if the current one-runtime contract doesn't support them. A component proposal needs its own later product decision.
4. Verify actual build detection. Make explicit build/runtime contract canonical; treat automatic inference as a *proposal* that is confirmed/resolved, not as infallible language detection.

**Exit:** minimum complete user journeys and exact failure evidence.

## S03-B — Shared static serving economics and security

1. Verify current published static files are served through a shared host/ingress route without allocating a per-app always-on backend process. Reuse existing static publishing if yes.
2. If not, implement the smallest static deployment path using immutable published artifacts, atomic version/pointer promotion, configured routing and TLS. Keep `source/` inaccessible; never serve private volume, `.env`, logs, artifacts not marked public, or traversal/symlink escapes.
3. Apply account ownership, organization permissions, auth toggle, storage/traffic and count admission. Bound control-plane artifact use across replicas; avoid process-local storage accounting.
4. Test 1, 10, 100 idle static sites in a controlled environment and measure shared memory/process count/disk overhead. A meaningful improvement should be measured, not asserted.

**Test cases:** concurrent publish/update, failed promotion, malicious archive entry, symlink/path traversal, cross-tenant URL/data denial, content hash mismatch, replica routing/cache invalidation, rollback and deletion.

## S03-C — Dynamic deployment and runtime convergence

1. Confirm durable states for prepared candidate -> health readiness -> promoted runtime; failure must not delete the healthy previous runtime or persistent data.
2. Test crash windows before candidate readiness, during route promotion and during cleanup, including duplicate commands and competing deployment attempts.
3. Check terminal CLI success means the server-side operation finished and supported health/routing requirements passed. An HTTP 200 on the API alone is not enough.
4. Prove stop/start/actual process exit behavior converges with desired state; restart loops and backoff are bounded. Data locality must block unsafe automatic reassignment.

**Test cases:** parallel deploy, Runner lost/recovered, process exits repeatedly, late result after cancellation, failed promotion while prior app stays healthy, version rollback, retained data.

## S03-D — Actions as first-class finite operations

1. Reuse existing App Actions and scheduled Action system. Do not create a second job engine.
2. Test creation/execution, manual and scheduled triggers, unique execution ID, scope/authorization, bounded concurrency, cancellation, result/log retrieval, retained journal and duplicate delivery.
3. Verify an Actions-only app can remain free of an unnecessary public HTTP runtime, where supported by the existing contract.
4. Compare resource costs of existing hosted-runner allocation with ephemeral execution possibilities; **do not introduce microVMs or new runners in this slice**.

**Test cases:** worker restart after reservation, action failure, uncertain outcome after crash, cancellation before execution, long-running bounded action, secrets redaction and cross-account denial.

## S03-E — BYO/on-prem parity and deletion

1. Repeat supported journeys against an authorized customer node, including internal/private routing and management-plane disconnection. Do not expose an internal app publicly without explicit owner configuration.
2. Verify secrets are scoped and not copied when remixing/forking; org membership and permissions hold through API/CLI/MCP/Studio.
3. Confirm unpublish vs undeploy vs delete semantics and durable cleanup debt for offline runners. A user must see what data is retained and what will be deleted.
4. Use approved, disposable test tenants/data for destructive acceptance.

## Non-goals

No new generic app builder, cross-host automatic volume migration, scale-to-zero, V8 function runtime, Kubernetes controller, organization billing model or universal multi-container manifest. Detect real friction and keep each fix small.

## Suggested first Luna prompt

> Execute **S03-A only**: create a code-and-test-grounded lifecycle matrix. Separate existing functionality from new proposals, especially static-only and Actions-only hosting. Recommend ONE missing correctness gap for the next implementation PR. No runtime rewrite or production change.

## Stage exit gate

An end-to-end example site, volume-backed API and finite Action can be deployed and managed under documented resource/authorization policies; their failures and cleanup are tested. Static-only serving has no forced per-app idle runtime, or the limitation is documented and staged before calling the stage complete.

---

<!-- SOURCE: 04-m6-recovery-operations.md -->

# S04 — Finish M6: recoverability, security and low-overhead operations

**Priority:** REQUIRED before promising managed production availability. **Depends on:** S01 and key S03 lifecycle paths. **Maps to:** canonical M6.

## Principle

A platform is not reliable because a container remains running in a normal test. Prove what happens when the API, Worker, broker, Runner, storage or whole host disappears, and what a human operator must do. All destructive fault injection occurs only in disposable, isolated fixtures with explicit approval.

## S04-A — Failure-mode and ownership inventory

1. Review RM transport and recovery evidence. Do not repeat successful rapid cutover/tunnel acceptance unless changes invalidate it.
2. Map durable authority for deployment, Action execution, Runner assignments, files/artifacts, billing transitions, storage measurement and route config.
3. Build `Fault | Expected state | Recovery mechanism | Data impact | Test | Live proof | Operator action` matrix for API restart/blue-green, 2 API replicas, 2 Worker replicas, RabbitMQ restart, Runner restart, host disappearance, degraded storage, missing artifacts, offline BYO node, stuck cleanup, bad upgrade and expired credentials.
4. Include documented open limits: API artifacts/package volume available only on same host, Worker image source provenance, no guaranteed cross-host persistent data failover, ARM64 unverified. Treat each as a conscious bounded support statement, not automatic scope expansion.

**Exit:** ranked, testable recovery backlog with no assumptions hidden behind 'self-healing'.

## S04-B — Control-plane and command recovery

1. Test duplicate, delayed, reordered and abandoned commands across API/Worker restarts. Terminal status and idempotent journal must prevent accidental re-execution of uncertain Actions.
2. Ensure consumer startup/shutdown, cancellation, poison message handling and retry budgets are bounded, visible and operator-recoverable.
3. Verify route reconciliation after persisted auth changes, and supported BYO tunnel restoration within documented constraints.
4. Verify enrollment, revoked credentials, dead/deleted Runner registrations, host/drain/maintenance admission and reconnect logic. Avoid resurrecting deleted identities or queue users.

**Test cases:** replays after lost callback, Runner accepted then crashed, API publishes twice, Worker dies during transaction, invalid command, revoked Runner, absent routing artifact.

## S04-C — Backups and reproducible restore

1. Inventory control-plane database, encryption key material/configuration needed to decrypt durable operations, deployment artifacts, app volumes, secrets, backups and access controls. Document what belongs in backups and what must never be copied in plaintext.
2. Choose modest, explicit RPO/RTO **targets only after measuring** restore time. Avoid presenting aspirations as guarantees.
3. Build a restore procedure into a separate test environment, including TLS/domain mapping and retained ownership. Restore at least one stateful app and one action record from backup.
4. Verify consistency at the chosen checkpoint: database and app data can be restored together or documented as potentially inconsistent. Verify backup age, checksums, permissions and retention.
5. Demonstrate a replacement node or same-node restore without claiming automatic cross-host live migration.

**Exit:** operator can rebuild and restore the service from written steps without access to a developer's local machine or undocumented state.

## S04-D — Storage exhaustion and data-safety drills

1. Use an isolated XFS quota-controlled environment for tenant quota exhaustion and low-free-disk behavior. Never fill the shared production root partition to test recovery.
2. Exercise build staging and published artifact pressure, host write limits, rollback image retention and cleanup. Prove management path remains usable for diagnosis/recovery.
3. Test teardown of an account with retained data and prevention of orphan/foreign mounts or unapproved hard deletion.
4. Document operator-approved cleanup commands with preflight, dry-run and explicit ownership selection. Do not replace safe image cleanup with broad `docker system prune` in production.

## S04-E — Observability, security and operational docs

Instrument or verify a small **useful** set: operation/queue age, failed retries, Runner presence age, cgroup/host exhaustion, XFS verification age, orphan cleanup debt, app crash loops, backup freshness and mismatch between plan/applied state. Use existing telemetry if possible.

Verify tenant-scoped access, safe path joins and symlink protections, secret redaction/rotation, bound app logs and archive handling, trusted action allowlists, and that hosted apps cannot access the host Docker socket/control plane credentials. Update production runbooks and admin-facing docs.

## Non-goals

No promise of automatic high availability across arbitrary hosts, distributed database or storage cluster, Kubernetes installation, ambitious auto-scaling or new home-grown observability stack. No production chaos without isolated environment and explicit approval.

## Suggested first Luna prompt

> Execute **S04-A only**: build a failure matrix from implemented code, RM acceptance, and docs. Identify the single highest-risk untested recovery path. Do not change infrastructure or runtime code yet.

## Stage exit gate

A second engineer can follow documented procedures to recover the tested failure scenarios; backup/restore is demonstrably functional, not theoretical; limitations have explicit customer/operator impact and evidence.

---

<!-- SOURCE: 05-dogfood-and-costs.md -->

# S05 — Dogfood managed and BYO hosting; establish actual unit economics

**Priority:** RECOMMENDED for the personal platform, learning portfolio and commercial decision. **Depends on:** S03/S04 baseline operational safety. **Maps to:** measured economics portion of M7 plus developer experience. Does **not** authorize pricing or infrastructure migration.

## Goal

Host real, modest workloads for at least a repeatable observation window. The important output is **useful operation and data**, not a new framework. Choose a fixed, comfortable monthly infrastructure budget before adding machines.

## S05-A — Three reference workloads

Deploy into controlled managed and BYO environments where supported:

1. **Static Vue site**: generated output, published URL, version update and rollback, no per-app permanent process.
2. **Persistent ASP.NET Core API**: health endpoint, resource caps, small durable dataset, deploy/update/restart, volume backup/restore.
3. **Finite Action**: manual trigger and schedule, success/failure/cancel, log/result retention, bounded resources.

For every example, document: CLI steps, Studio experience, environment configuration, runtime requirements, measurable cost/resource usage, failure modes, and cleanup. Use disposable/sample data; don't expose personal information.

**Exit:** another developer can repeat all three experiences from README and templates.

## S05-B — Developer experience and portable BYO installation

1. Test a fresh Linux node using the supported Runner installer, then application deploy, monitoring and uninstall. Verify outbound-only control connectivity and the documentation's actual port/TLS requirements.
2. Test loss/recovery of API connectivity, offline/online status in Studio, credential rotation/revocation and remote shutdown of an app without exposing private files.
3. Record friction: required inputs, commands, confusing statuses, logs, error text, repeated manual steps, DNS/TLS setup. Fix one high-frequency pain point at a time using existing CLI/API/Studio concepts.
4. Keep fully offline self-hosted control plane **out of scope**; customer-owned execution via Automaton SaaS is already meaningful. Design and document the boundary honestly.
5. Do not introduce a new Node Agent merely to rename the existing Runner. Consolidation would require an independently justified architecture proposal and migration plan.

## S05-C — Measure cost and density

Gather a repeatable measurement harness (scripts/docs only unless a small product metric is missing): idle and active memory/CPU of each process, static serving overhead, bounded concurrent builds, action executions, artifact/disk/log growth, network traffic, backup cost and per-server reserve.

Use actual invoice/server billing numbers and explicit utilization assumptions. Keep model variables for: purchased host capacity, shared API/Worker/database, static storage, compute/runtime, host image caches, persistent volumes, outbound traffic, backups, monitoring, support time and tax/payment fees if relevant.

Avoid dividing a fixed server price by an arbitrary maximum theoretical number of apps and calling it guaranteed per-app cost. Report marginal vs allocated cost separately. Calculate safe density only with observed workload p95, burst headroom and risk policy.

**Measurements:** 1/10/100 idle static sites as feasible, 1/5/10 active web apps as safe, a bounded set of concurrent Actions/builds, and a reference backup restoration. Never overload production customer workloads.

## S05-D — Decide: personal platform, research project or pilot business

Produce a one-page scorecard:

- User value: apps actually used; deployment frequency; time to first publish; failed deploys.
- Operations: intervention hours per month; recoveries; backup freshness; incidents.
- Economics: monthly spending, allocated cost/workload, practical capacity and biggest cost driver.
- Learning: engineering case studies with architecture diagram, benchmark, test report and failure recovery.
- Commercial signal (if any): outside users, their repeated usage and willingness to pay.

Do **not** proceed to paid launch automatically. A credible decision can be: keep Automaton as a personal platform, improve BYO, open-source examples, or start a small restricted cohort.

## Non-goals

No marketing launch, paid Stripe rollout, Kubernetes, cloud-provider switch, function/microVM production rollout, generalized multi-component manifests or arbitrary server fleet expansion.

## Suggested first Luna prompt

> Execute **S05-A only**. Inspect existing example apps and docs, then prepare the smallest reproducible reference-workload package for static publishing, .NET with persistent data, and one Action. First report which examples already exist and what is missing. Do not deploy without approval.

## Stage exit gate

The owner uses Automaton for genuine workloads, documented BYO steps work as tested, and the cost and operational scorecard contains **measured inputs** rather than estimates disguised as results.

---

<!-- SOURCE: 06-m4-policy-billing-conditional.md -->

# S06 — Finish M4: safe policy transitions and billing (conditional)

**Priority:** DEFER until there is genuine intent to offer paid managed plans. **Depends on:** S01/M2 + S02/M3, with recovery evidence from S04. **Maps to:** canonical M4.

## Why conditional

Automaton can be useful as a personal and BYO platform without implementing every Stripe/payment edge case. But once customers pay for resources, billing and applied infrastructure state become a high-stakes correctness boundary. Do not promise paid limits without implementing this stage.

## S06-A — Audit one authority for resources and payments

1. Read `src/Automaton.Api/Services/StripeService.cs`, `StripeWebhookService.cs`, `BillingController.cs`, subscription resource models, admin assignment and Hosted Runner reprovisioning. Confirm paths before editing.
2. Trace desired plan -> effective policy revision -> pending host enforcement -> applied snapshot -> visible customer entitlement. Separate *payment state* from *resource-applied state*.
3. Inventory stripe event receipts, idempotency, stale/out-of-order event handling, concurrent checkout, customer/provider reconciliation and test-mode coverage. Do not introduce an independent duplicate subscription authority.
4. Decide documented behavior for failed payment, recovery, cancellation, grace period, invalid plan, data retention, resource downsizing, and app suspension. Obtain product approval for ambiguous commercial semantics before implementation.

## S06-B — Durable transitions

Implement or finish `requested -> applying -> applied/blocked/failed` using current durable infrastructure rather than a new orchestration engine. Preserve existing resource reservations and data until host readback confirms a change. A request to shrink storage below measured use must **block safely**; it must never delete content to force compliance. Retain conflict guard for shared XFS-project histories.

**Tests:** concurrent admin and provider changes, repeat same desired policy, failed reprovision while old container remains, stale usage blocks shrink, current/pending reservations, replay after API restart, correct UI display.

## S06-C — Payment reconciliation

1. Add/verify a durable receipt/inbox keyed by provider event identity, order-independent reconciliation against provider's current subscription, and bounded retries.
2. Protect concurrent customer/checkout/subscription creation and distinguish canceled/free/unpaid/active states correctly.
3. Handle duplicate events, delivery out of order, recovery after unpaid period, canceled then resumed, webhook while API restarted, and idempotent Free-plan transition.
4. Use Stripe test mode and replay fixtures; never call live payment mutation endpoints from automated acceptance without explicit approval.

## S06-D — Present truth and customer recovery

Studio/API must distinguish `billing active`, `resource upgrade pending`, `applied`, `blocked by disk`, `payment failure`, `suspended` and `cancel scheduled`. Keep predictable customer-facing remediation steps. Update Admin guides with manual reconciliation/rollback.

## Non-goals

No final product prices, complicated metered billing, multi-currency tax compliance promises, organization resource-owner migration or new payment provider.

## Suggested first Luna prompt

> Execute **S06-A only**, with read-only code/provider-flow audit and contract proposal. No Stripe or production changes. Flag business-policy choices that need owner approval rather than inventing them.

## Stage exit gate

Provider test-mode scenarios and host policy acceptance show durable convergence; cancellation and unpaid states never silently remove durable data; reported entitlements distinguish desired from actually applied. S07 controls pricing and release.

---

<!-- SOURCE: 07-m7-commercial-pilot-conditional.md -->

# S07 — Finish M7: cost-backed product limits and a restricted commercial pilot (conditional)

**Priority:** DEFER unless preparing external paid users. **Depends on:** S02 resource truth, S03 product journeys, S04 recovery, S05 measured economics, S06 payment/policy correctness. **Maps to:** canonical M7.

## S07-A — Capacity and service contract

1. Use actual S05 benchmarks and invoices. Define realistic host headroom, CPU/memory/PID reservations, volume quota, storage measurement freshness, build/action concurrency, saved apps, running apps, static sites and network/backup limits.
2. Decide what each plan guarantees: uptime support policy, RPO/RTO as measured and contractually worded, data durability/retention, cancellation, upgrades/downgrades and capacity admission when a node is full.
3. Keep static/BYO Free economically bounded; do not provision an idle managed Hosted Runner to every signup. Don't advertise 'unlimited' where storage, traffic or incident handling are finite.
4. Calculate low/mid/high utilization scenarios, including support time, payment fees, reserve capacity, non-paying users and recovery costs; express contribution margin, not just server price divided by app count.

**Exit:** written go/no-go decision and versioned plan/resource table, with assumptions and evidence attached.

## S07-B — Align one catalog and permissions

1. Compare real server plan/specification policy with website, Studio, billing API, admin docs and CLI messaging; remove contradictory historic AI-credit/hosting claims.
2. Enforce that test/hidden/deactivated plans cannot be purchased by directly calling an API.
3. Ensure resource identity and applied policy are consistent through signup, free trial, checkout, downgrade, cancellation and loss of payment.
4. Regression-test both UI and API; test direct unauthorized calls, not just hidden buttons.

## S07-C — Restricted pilot with rollback criteria

1. Invite a small, explicitly approved cohort. Set monthly spend and support caps, capacity headroom and incident escalation contacts.
2. Track: successful deployment rate, activation, repeat usage, CPU/RAM p95, queue age, static artifact growth, backup restore result, cost per active customer and support tickets.
3. Test voluntary cancellation, deletion, data export, plan change and payment recovery with disposable or authorized accounts.
4. Establish stop conditions: security breach, inability to restore, repeated quota escape, excessive burn, unpredictable bill, unusable recovery time or severe operational burden.
5. Keep release provenance and previous immutable versions for safe rollback. Do not call a limited cohort a broadly available SLA-backed service.

## Non-goals

No public launch target date, enterprise SLA, managed database offering, multi-region failover, auto-scaling or global CDN promise without independent evidence. No speculative fundraising/product marketing scope.

## Suggested first Luna prompt

> Execute **S07-A only**, as a cost-backed analysis from measured S05 inputs and current plan settings. Explicitly list missing measurements; do not invent pricing or change customer entitlements.

## Stage exit gate

The published promise matches enforced and tested behavior; commercial economics are plausible under conservative assumptions; the owner deliberately approves any cohort expansion.

---

<!-- SOURCE: 08-workerd-functions-experiment.md -->

# S08 — Optional research: a low-idle `workerd` / V8 function runtime

**Priority:** OPTIONAL, research only. **Depends on:** S05 demonstrating meaningful short-lived HTTP/Action workloads and a measurable cost hypothesis. **Not part of current M0–M7 delivery gates.**

## Decision hypothesis

For many tiny, infrequently invoked JavaScript/WasM handlers, a shared function runtime may provide substantially lower idle CPU/memory than keeping one container process per user. This does **not** imply it can run arbitrary ASP.NET Core, native Linux applications or every existing Action. Test rather than assume.

**Security warning:** A V8 isolate is not a hardware virtualization boundary. An embeddable JS runtime is not automatically a production-grade hostile-multitenancy service. Do not deploy untrusted external tenant code into an unreviewed pooled process.

## S08-A — Standalone lab specification

1. Create an experiment **outside production**, with no changes to the stable Automaton deployment contract.
2. Compare a trivial HTTP handler, JSON transform, CPU-bound handler and small WebAssembly workload in a warm minimal OCI container versus `workerd` isolate as supported by current versions.
3. Define fixed hardware, concurrency, request pattern, cold/warm distinction, memory measurements (PSS/RSS clearly labeled), CPU, p50/p95/p99, errors, binary/runtime version, and startup behavior.
4. Inspect official `workerd` hosting/security documentation before making claims; document unsupported APIs and deployment requirements.

**Exit:** reproducible harness and test matrix before code is integrated.

## S08-B — Minimal proof of concept

1. Run a local `workerd` instance serving 2–3 independent lab handlers, with explicit routing and minimal capabilities.
2. Implement only what is needed to run/measure the handlers. No customer API, subscriptions, billing, new scheduler, or production control plane integration.
3. Enforce/request-limit body size, CPU/time/concurrency and allowed network capabilities where supported; document anything not enforceable.
4. Measure isolated and combined workloads under idle, steady traffic and burst. Record resource/process overhead and failure behavior.
5. Test isolation boundaries as far as this lab supports: inability to access unrelated handler state, filesystem and network without configured capability. Label limitations; do not claim a security certification.

## S08-C — Economic and architecture decision

Build a table for each workload: execution model, idle/resident memory, cold/warm latency, host overhead, supported libraries, isolation assumptions, throughput, operational complexity and estimated fleet utilization. Compare to static serving and ephemeral OCI Actions, not merely warm always-on containers.

A positive result may justify an **optional** function workload type with a defined source/build artifact and runtime capability. It must never silently change an existing generic OCI app's semantics. A negative result is a valid successful research outcome.

## Non-goals

No direct migration of .NET/Python services to V8, no production tenant isolation promise, no global Cloudflare infrastructure dependency, no S3/R2 dependency, no replacement of all Actions, and no automatic runtime detection implemented in production during this spike.

## Suggested first Luna prompt

> Execute **S08-A only** in a separate experimental folder/branch, using official verified `workerd` semantics, with a benchmark plan and no production integration. Explain how results will be falsifiable. Do not touch existing Runtime or Runner contracts.

## Stage exit gate

A reproducible benchmark and a documented adopt/defer/reject decision. Integrate only after a new explicitly approved stage and threat model.

---

<!-- SOURCE: 09-firecracker-microvm-experiment.md -->

# S09 — Optional research: Firecracker microVMs for high-risk Actions

**Priority:** OPTIONAL, research only. **Depends on:** S05 demonstrating a clear need to run untrusted native code / AI agents and S04 recovery/security foundations. **Not part of current M0–M7 delivery gates.**

## Decision hypothesis

MicroVMs may provide a stronger boundary for untrusted native workloads, such as arbitrary binaries or AI coding agents, than normal shared-host containers. They also introduce guest-kernel, startup, image distribution, networking, device, snapshot and storage-management overhead. Evaluate the trade-off, not the novelty.

## S09-A — Threat model and lab safety

1. Define the attacker/tenant, host assets, shared tenants, escape impact, authorized external network destinations, permitted secrets, persistent data and abuse controls.
2. Use a disposable Linux/KVM-capable **dedicated lab host**; verify hardware virtualization and kernel support. Never require nested virtualization or a production host's actual Docker socket to make the demo work.
3. Define measurement targets: microVM launch-to-ready, p50/p95 boot latency, host and guest RAM, CPU overhead, concurrency density, disk/image download overhead, cleanup, and time to terminate/revoke.
4. Choose a safe, known guest image/root filesystem; pin and verify build provenance. Treat guest kernel/security patching as a platform responsibility.

**Exit:** reviewed threat model, bounded lab plan and cost cap; no new Automaton code yet.

## S09-B — Standalone execution lifecycle

Implement a minimal runner wrapper **in the lab only**:

1. Accept a synthetic job ID and a known immutable guest artifact.
2. Create the Firecracker jail/VM using official supported lifecycle, configure bounded vCPU/RAM, read-only base root where suitable, disposable scratch and permitted network only.
3. Supply a synthetic non-secret input, execute one finite job, cap deadline/output size, collect terminal status and logs, and destroy disposable resources.
4. Simulate cancellation, missing artifact, guest crash, host process crash, duplicate job ID and network restriction.
5. Ensure persistent data is not deleted unless explicitly within the synthetic job's disposable scope. Document limits when a VM disappears mid-execution; never claim exactly-once side effects.

Do **not** give guests the host Docker socket, shared system volume or arbitrary host filesystem mounts. Do not run malicious code to test containment on a shared production host.

## S09-C — Compare with ephemeral OCI Action

Use the same fixture where possible. Compare cold start, steady throughput, host RAM and CPU, deployment/storage complexity, cleanup safety, operational effort and relative security boundary. Make a decision:

- **Adopt later:** a real risk profile requires VM isolation and measured overhead is acceptable;
- **Defer:** useful but workload volume/needs do not justify integration;
- **Reject for now:** operational overhead outweighs benefits.

If adopted, propose a small, separately reviewed `IsolatedJob` capability at the execution boundary. A new execution engine must not force every customer app into a VM or rewrite the existing Runner/hosted-storage model.

## Non-goals

No microVM production fleet, persistent VM-per-customer, Kubernetes/Kata integration, distributed network storage, platform-wide Node Agent replacement or price plan adjustment in this stage.

## Suggested first Luna prompt

> Execute **S09-A only**, drafting the threat model, exact isolated host prerequisites, failure-test plan and reproducible measurements. Do not execute VM provisioning or change production infrastructure without approval.

## Stage exit gate

There is a documented adopt/defer/reject decision with measured comparison to existing Actions and explicit attack/isolation limits. A decision to integrate requires a new implementation and security plan.

---
