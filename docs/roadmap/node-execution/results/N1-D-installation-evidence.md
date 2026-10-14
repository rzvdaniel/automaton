# N1-D — installation and doctor preparation

**Status: bounded pure doctor diagnostic model and formatter are implemented, independently contract/source-reviewed and acceptance-tested.** No installer, package publisher/verifier, service controller, host probe, credential revocation, update, rollback or uninstall behavior is implemented. This result records only the N0-D-authorized preparation scope; N1-D and N1 overall are not complete.

## Baseline and owners

Baseline worktree HEAD: `9077057c2d52e587df001ec504b1ce52632b6694`, with the N1-A implementation, N1-B codec and N1-C model changes present in the working tree. `Automaton.Node` currently starts through `Program.cs` and `NodePreparationHost`/`NodePreparationService`; it holds the installation lock, validates local protected state and remains `RecoveryRequired`. `Capabilities/NodeCapabilityValidator` accepts caller-provided capability-shaped facts but explicitly does not establish host eligibility. `Activation/NodeActivationReadbackService` provides read-only API status for a supplied identity, but the Node host does not compose its activation client. There is no Node installer, package verifier, update coordinator or doctor acquisition service. Existing Runner install scripts and services are not Node packaging owners and are not changed by this preparation.

N0-D allows package/doctor specification, pure diagnostic formatting and explicit unsupported-platform messages. Real installation, credentials, service/restart/update/rollback, restore startup and host-facing doctor acquisition remain blocked by N1-A–C and actual B1 review. Production package publication also needs an approved provenance path. Existing N1-C read-only models are preparation only and do not provide an inventory or host-probe authority.

## Proposed read-only doctor contract

The bounded implementation candidate is a pure formatter over caller-supplied, immutable, already-normalized facts. It performs no filesystem, process, service-manager, Docker, cgroup, network, database, broker or API access. It does not turn the current preparation service or capability validator into a probe. No DI registration or CLI/HTTP endpoint is added.

The input is `NodeDoctorInputV1` with exactly one schema version and up to 32 `NodeDoctorFactV1` items. Each item contains one closed diagnostic code, its required freshness value, and an optional UTC observation time. Facts are immutable and duplicate diagnostic codes are rejected. Undefined codes/freshness values, invalid/non-UTC times and over-bound input reject as a whole; no truncation or partial result is allowed. Empty input is valid and produces the generated `NoObservationsSupplied` output code. The formatter does not accept free-form subjects, states, platform names or messages. Freshness is supplied by a future reviewed acquisition owner; the formatter cannot infer current authority from a timestamp. No acquisition owner is implemented here, so constructing a fact in a test or caller is not authority evidence.

The complete v1 input-to-output vocabulary is:

| Input code | Required freshness | Output severity | Stable output message |
| --- | --- | --- | --- |
| `PlatformLinuxX64Reported` | `NotApplicable` | `Information` | `Reported platform profile: Linux x64.` |
| `PlatformUnsupportedOs` | `NotApplicable` | `Blocking` | `The reported operating system is outside the supported Node profile.` |
| `PlatformUnsupportedArchitecture` | `NotApplicable` | `Blocking` | `The reported architecture is outside the supported Node profile.` |
| `PlatformUnknown` | `Unknown` | `Blocking` | `The platform profile is unknown.` |
| `InstallationLockUnavailable` | `Current` | `Blocking` | `The installation lock is unavailable.` |
| `IdentityMissing` | `NotApplicable` | `Blocking` | `The protected Node identity is missing.` |
| `IdentityInvalid` | `Current` | `Blocking` | `The protected Node identity is invalid.` |
| `IdentityStale` | `Stale` | `Blocking` | `The protected Node identity is stale.` |
| `IdentityUnknown` | `Unknown` | `Blocking` | `The protected Node identity state is unknown.` |
| `RecoveryAuthorityMissing` | `NotApplicable` | `Blocking` | `Recovery authority evidence is missing.` |
| `RecoveryAuthorityStale` | `Stale` | `Blocking` | `Recovery authority evidence is stale.` |
| `RecoveryAuthorityUnknown` | `Unknown` | `Blocking` | `Recovery authority state is unknown.` |
| `RecoveryEpochMismatch` | `Current` | `Blocking` | `The recovery epoch does not match the current authority.` |
| `CredentialMissing` | `NotApplicable` | `Blocking` | `The Node credential is missing.` |
| `CredentialRevoked` | `Current` | `Blocking` | `The Node credential is revoked.` |
| `CredentialStale` | `Stale` | `Blocking` | `The Node credential status is stale.` |
| `CredentialUnknown` | `Unknown` | `Blocking` | `The Node credential status is unknown.` |
| `ActivationRecoveryRequired` | `Current` | `Blocking` | `Node activation requires recovery.` |
| `ActivationFenced` | `Current` | `Blocking` | `The Node activation is fenced.` |
| `ActivationUnknown` | `Unknown` | `Blocking` | `The Node activation state is unknown.` |
| `JournalUnsettled` | `Current` | `Blocking` | `A durable Node operation remains unsettled.` |
| `JournalUnknown` | `Unknown` | `Blocking` | `The durable Node operation state is unknown.` |
| `PackageUnverified` | `Unknown` | `Blocking` | `Node package provenance is unverified.` |
| `PackageInvalid` | `Current` | `Blocking` | `The Node package failed verification.` |
| `ServiceUnavailable` | `Current` | `Blocking` | `The Node service is unavailable.` |
| `EngineUnsupported` | `NotApplicable` | `Blocking` | `The reported container engine is unsupported.` |
| `EngineUnknown` | `Unknown` | `Blocking` | `The container engine state is unknown.` |
| `CgroupUnsupported` | `NotApplicable` | `Blocking` | `The reported cgroup profile is unsupported.` |
| `CgroupUnknown` | `Unknown` | `Blocking` | `The cgroup profile is unknown.` |
| `StorageUnsupported` | `NotApplicable` | `Blocking` | `The reported storage profile is unsupported.` |
| `StorageUnknown` | `Unknown` | `Blocking` | `The storage profile is unknown.` |

The formatter returns an immutable list of `NodeDoctorDiagnosticV1` values containing only output code, fixed severity, required freshness, optional observation time and fixed message. For every nonempty fact, `OutputCode` equals that fact's input `Code`; severity, freshness and message are the exact corresponding table values. It sorts by the ordinal output-code name; an empty input returns only `NoObservationsSupplied`, severity `Information`, freshness `Unknown`, with message `No diagnostic observations were supplied.` No message contains a value copied from input. Output is deterministic for a given valid input. Unknown/unrecognized values reject; a future transport parser must separately define canonical encoding and size limits.

The output is explicitly descriptive. It contains no `CanAdmitWorkloads`, `CanRepair`, `CanActivate`, permission, remediation command, automatic repair hint, or success claim. `RecoveryRequired`, unknown authority, stale identity/epoch, missing recovery evidence, blocked journal, unavailable package provenance and unsupported platform facts remain visible as supplied; formatting cannot clear or downgrade them. The formatter cannot report the host as installed, active, recoverable or ready unless a future reviewed acquisition contract proves each statement. The current contract therefore has no aggregate “healthy/ready” boolean.

Supported profile messages are limited to the reviewed initial Linux/x64 target. Windows, ARM, unknown OS/architecture, and unsupported engine/cgroup/storage profile facts produce explicit unsupported/unknown diagnostics without probing or mutation. A message does not authorize conversion or migration of storage drivers, cgroups, filesystems or services.

## Concrete acceptance cases

The pure formatter implementation, after independent review of this vocabulary, must cover:

- The same valid input yields byte-for-byte identical diagnostic ordering and text; caller collection mutation after construction cannot alter the result.
- Every one of the 31 input code mappings and the generated empty-input code has a representative exact input/output-code equality, severity/freshness/message case; undefined values, duplicate codes, invalid/non-UTC time and 33-item input reject without partial output.
- `Unknown`, `Stale`, `Blocked`, `RecoveryRequired`, missing epoch/register evidence and stale credential facts remain visible as blocking or unknown and never become healthy through formatting.
- Linux/x64 is represented as the declared target profile; Windows, ARM and unknown platform values produce explicit unsupported/unknown codes with no automatic remediation.
- Sentinel secrets, paths, exception text and raw authority responses cannot enter the closed input model and never appear in diagnostic fields or messages. The test asserts typed output fields only; no serialization contract is implied.
- The output type exposes no activation/admission/repair permission, no action/remediation command and no aggregate readiness flag. The Node host service collection continues to contain no doctor, probe, installer or package-verification consumer.
- Empty input yields a neutral “no observations supplied” result, not “healthy”, “installed” or “ready”.

These tests establish only deterministic formatting and value-boundary behavior. They do not establish OS detection, package authenticity, installation safety, service lifecycle, credential revocation, storage health, freshness of host/API evidence or operational recovery.

## Ordered N1-D work and gates

1. **Ready now:** review this doctor contract; implement a pure data model/formatter and meaningful tests if the contract passes review. Keep the runtime composition unchanged.
2. **Blocked consumer:** specify package provenance with the existing release owners, then implement package verification and installer/update/service orchestration only after N1-A–C consumer prerequisites and B1 are approved. Do not create an alternate publishing or signing authority.
3. **Blocked recovery consumer:** implement credential revocation, restore startup, update rollback, safe uninstall and the host-facing doctor only with fresh authority, journal/ownership state, explicit service ownership and approved operational custody. Retain persistent tenant data by default.
4. **Acceptance before N1-D exit:** disposable Linux/x64 host evidence for clean install/rerun, malformed digest/package, interruption/partial install, safe update/rollback, service failure, revocation, reboot/recovery, old workloads remaining healthy, and uninstall retaining volumes. Record exact OS/kernel/engine/package hashes and teardown. A fake or pure formatter test cannot satisfy this gate.

Outstanding prerequisites include actual independent B1 approval; the N1-A PR #379 implementation is merged, while operational activation/authority gates remain pending and positive-incarnation checkpoint follow-up `325c97b1` is under review; stateful N1-B dispatch/journal/settlement; N1-C owner persistence/inventory/adoption and Linux storage/cgroup proof; an approved package provenance owner; operational credential/recovery custody; and disposable installer/reboot/revocation fixture evidence. No authority or external evidence is created by this contract or its reviewer.

## Package provenance proposal for review

Source inspection found `.github/workflows/release.yml` is specifically the Runner Console release: it publishes framework-dependent and self-contained Runner archives, adjacent SHA-256 files, GitHub Release assets and a Runner GHCR image. It uses tagged actions, does not emit a signed artifact provenance statement, and does not build or publish `Automaton.Node`. `scripts/install-infrastructure-runner.sh` installs a different privileged containerized Runner product and is not a Node packaging or trust owner. Neither path establishes a trusted Node package origin.

**Proposed, not approved:** extend the existing release workflow with a distinct Linux/x64 Node package job and GitHub Release artifact; do not create an alternate publisher, use the Runner container image, or publish to production while this proposal is pending. The package manifest should bind product/component, semantic version, source commit, target `linux-x64`, runtime-dependency mode, archive digest and file inventory. Publish a SHA-256 checksum for transfer diagnostics, but do not treat an adjacent checksum as authenticity. A GitHub Actions artifact provenance attestation should bind the exact archive digest to the Automaton repository, reviewed release workflow path, source commit/ref, Node project and target. A future installer must reject packages with a missing/invalid attestation, wrong repository/workflow/project/ref/target, digest mismatch, unsupported version, ambiguous archive contents or path traversal before replacing installed files. Retained rollback packages must pass the same verification.

Required package acceptance vectors for that future verifier are `NodePackageProvenanceAcceptsReviewedReleaseArtifact`; `NodePackageRejectsChecksumOnly`; `NodePackageRejectsWrongRepositoryOrWorkflow`; `NodePackageRejectsWrongCommitVersionOrTarget`; `NodePackageRejectsDigestMismatchAndTamperedArchive`; `NodePackageRejectsAmbiguousManifestAndPathTraversal`; and `NodeRollbackPackageRequiresSameVerifiedProvenance`. They must use generated local artifacts and the actual selected attestation verifier; hand-built valid-looking metadata or checksum-only tests are insufficient.

Independent review found no blocking finding for owner/security review of this proposal at document SHA-256 `A7F0324D14975B3744DC98F46E47530386D0B7E5254641051B514C41FD0118A4`. Review prerequisites remain unresolved: security/operations must approve GitHub Actions as the package provenance authority, the exact attestation trust predicate, allowed source refs, least-privilege workflow permissions and immutable release-tag policy. The Node runtime dependency mode and archive layout also need a reviewed install contract. Current workflow source is only a candidate integration point; no GitHub repository policy, protected-tag setting, workflow permission or live release evidence was inspected. This proposal does not approve changing `.github/workflows/release.yml`, signing/publishing, consuming packages, or installing Node.

No runtime, production configuration, host, credential or package-publishing changes were made. No live access or disposable host fixture was used.

## Pure formatter implementation and acceptance

The approved implementation is confined to `src/Automaton.Node/Diagnostics/NodeDoctorContracts.cs`, `NodeDoctorFormatter.cs` and `NodeDoctorFormatterTests.cs`, plus the lifecycle regression that asserts no doctor service is registered. It maps all 31 input codes to fixed messages/severity/freshness, validates UTC timestamps, rejects undefined or duplicate codes and over-bound input, copies collections, and sorts deterministically. It has no acquisition, filesystem, process, service, network, API, Docker, database or broker access and is not composed into the Node host.

Contract review of this exact vocabulary approved only the pure uncomposed formatter at document SHA-256 `6D4BFCA2068CD8E485CD78F777352FC6672266F85E5CAA5034A7757746DE60EC`. Independent source review found no blocking findings at base HEAD `9077057c2d52e587df001ec504b1ce52632b6694`. Reviewed file SHA-256 values: `NodeDoctorContracts.cs` `59BABA2EB38335EE48B6BC3235D685EBEE2F3337D3D4A9AAD3209FF8E8D0F9F0`; `NodeDoctorFormatter.cs` `A30DE233148CD436615FE53DE20B8D0F1A689E692EF528BB83BC69EF224CCBE3`; `NodeDoctorFormatterTests.cs` `23AD3A3EB495987735E51C57610177939A1C237E29FDCAD14E6B5A675D194501`; lifecycle test `A83CB0B760C8284127B2AA481CF824145792DCDF7651286DD39F86FAA16EB6FB`.

Independent QA ran against HEAD `9077057c2d52e587df001ec504b1ce52632b6694` and the exact unchanged 54-file Node/Node.Tests/Architecture.Tests source manifest SHA-256 `ae46d44c69b6ab1158a9d03cdb2defa12e7d7eada9d432662029098696ac28bb`:

- `dotnet build src/Automaton.Node/Automaton.Node.csproj -c Debug --no-incremental -v minimal`, and the equivalent nonincremental commands for `src/UnitTests/Automaton.Node.Tests/Automaton.Node.Tests.csproj` and `src/UnitTests/Automaton.Architecture.Tests/Automaton.Architecture.Tests.csproj`: each passed with 0 warnings and 0 errors.
- `dotnet test src/UnitTests/Automaton.Node.Tests/Automaton.Node.Tests.csproj -c Debug --no-build --no-restore --filter FullyQualifiedName~NodeDoctorFormatterTests`: 36 passed, 0 failed, 0 skipped. Full Windows Node.Tests with `--no-build --no-restore`: 96 passed, 0 failed, 0 summary skips; 11 Linux-only cases printed as platform skips. Architecture.Tests with `--no-build --no-restore`: 10 passed, 0 failed, 0 skipped.
- Full `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal`: passed with 0 errors and 1,253 warnings. The warnings include the existing SSH.NET NU1903 advisory and historical compiler/analyzer warnings; no new warning came from the changed Node project.
- `git diff --check`: passed.
- Linux fixture used pinned `mcr.microsoft.com/dotnet/sdk:9.0@sha256:1330d3c4144aec2695036a125d9a3b49a1fc555c9024c981248fa83d40fa8b23`, resolved image ID `sha256:bc7fe02413a7664b5f1c43cf9f59503618f054fbd61d0ab61a835a2e28c16091` (`linux/amd64`). Container had read-only source/root, private `/tmp`, `/root` and `/run` tmpfs, no network, 2 CPUs, 4 GiB memory, 128 PIDs, no privileged mode/socket/production mounts, all capabilities dropped except test-required `CHOWN`/`DAC_OVERRIDE`, and `no-new-privileges`. Node.Tests passed 107/107 and Architecture.Tests 10/10 with no failures/skips. `--rm` teardown and a follow-up container listing confirmed removal. The initial sandboxed Docker attempt was denied before creating a container; the final authorized isolated run succeeded.

MySQL and broker fixtures are not applicable to this stateless formatter. The acceptance above does not cover installation, package signing/provenance, actual host probes, service control, update/rollback, revocation, restore recovery, tenant workloads, host effects or operational N1-D exit gates.

## Exact PR #384 follow-up review and QA

Independent source review of commit `2568346865bda55c0f6cc1124bc06db6faf32437` found no blocking findings; the bounded snapshot-copy and result-time cases reviewed in that commit do not change the formatter's purity or composition. Independent QA at the same exact source revision ran the full Windows Node.Tests suite with summary 89 passed, 0 failed, 0 skipped (10 Linux-only cases emitted platform skip lines outside the summary); Linux Node.Tests passed 99, 0 failed, 0 skipped. Architecture.Tests passed 10, 0 failed, 0 skipped on both platforms. Nonincremental Node, Node.Tests and Architecture.Tests builds passed with 0 warnings/errors; the full solution build passed with 0 errors and 1,253 existing warnings; `git diff --check` passed. Linux used pinned SDK image `mcr.microsoft.com/dotnet/sdk:9.0@sha256:1330d3c4144aec2695036a125d9a3b49a1fc555c9024c981248fa83d40fa8b23`, a read-only source mount, private tmpfs, no network, bounded resources and no privileged mode/socket/production mounts. The container was removed and verified absent. MySQL is inapplicable. Installation, package verification, host acquisition and operational recovery remain unimplemented and untested.
