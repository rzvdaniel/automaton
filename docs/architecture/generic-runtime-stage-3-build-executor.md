# Stage 3: Generic build executor

Automaton builds are explicit commands from `ApplicationContract.Build`. The
platform does not infer a package manager, language, framework, or source layout.

The shared `ApplicationBuildExecutor` executes the structured command with its
configured arguments, working directory, environment, timeout, and optional
shell mode. It captures stdout/stderr, reports deterministic exit/timeout/
cancellation state, validates workspace boundaries, and kills the entire
process tree when the build is canceled or times out.

Runner deployment executes the build against the extracted source workspace
before snapshot cleanup or live-runtime mutation. A successful configured
output directory is copied into the staged runtime artifact. A failed build
therefore leaves the current runtime, routing, metadata, and artifacts
untouched.

The legacy controller upload path still has Python virtual-environment and
requirements handling. That coupling is intentionally deferred to the later
Action/controller execution stages; it is not used by the generic deployment
build path.
