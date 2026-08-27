# Stage 3: Generic build executor

Automaton builds are explicit commands from `ApplicationContract.Build`. The
platform does not infer a package manager, language, framework, or source layout.

The shared `ApplicationBuildExecutor` executes the structured command with its
configured arguments, working directory, environment, timeout, and optional
shell mode. It captures stdout/stderr, reports deterministic exit/timeout/
cancellation state, validates workspace boundaries, and kills the entire
process tree when the build is canceled or times out.

Normal remote deployment packages the source and contract without executing the
application build locally. Runner deployment executes the build once against
the extracted source workspace before snapshot cleanup or live-runtime
mutation. The shared artifact rule selects `build.outputDirectory` when set,
otherwise the runtime command working directory, otherwise the source
workspace root. The selected post-build directory is copied into the staged
runtime artifact. A failed build therefore leaves the current runtime,
routing, metadata, and artifacts untouched.

The CLI may still reuse `ApplicationBuildExecutor` for an explicitly local
workflow, but `automaton deploy` does not implicitly build before uploading.

Build stdout/stderr and deterministic timeout/cancellation state are returned in
deployment logs. Timeout and cancellation attempt tree termination, use a
bounded non-cancelled wait, and use the Windows tree-kill fallback when needed.

The legacy controller upload path still has Python virtual-environment and
requirements handling. That coupling is intentionally deferred to the later
Action/controller execution stages; it is not used by the generic deployment
build path.
