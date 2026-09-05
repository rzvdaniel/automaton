# Automaton Privileged Host Apps

Use a Privileged Host Runner only when the app needs approved host metrics,
Docker information, operating-system data, or another host capability.

Prefer a Standard or Hosted Runner for ordinary applications. Do not assign a
Privileged Host Runner as an automatic fallback, and never pass
`--acknowledge-privileged-host-access` unless the user has explicitly approved
privileged host access for that app and environment. If only a Privileged Host
Runner appears usable, stop and ask the user for that approval.

Privilege comes from the Runner assignment. It is not requested by
`automaton.json` or by an app request. A Standard Runner may not provide these
capabilities. Keep monitoring and administration apps private unless the user
approves a design that is safe to expose.

The Runner may provide `/host`, `/var/run/docker.sock`, host networking, and
host process visibility. Treat each capability as optional. Do not assume that
host utilities or a particular operating system are installed.

Use fixed, allowlisted files or narrow APIs. Parse only the fields the app
needs and return sanitized values. Never accept host paths, commands, process
IDs, container IDs, service names, or URLs from an HTTP request or app input.

Use a fixed subprocess only when no stable file or API is suitable. Pass a
fixed argument list with `shell=False`, a short timeout, bounded output, and a
generic unavailable result when the executable is missing or fails.

Never expose environment variables, credentials, Runner configuration, raw
Docker responses, mounts, commands, registry data, or host-file contents.
Do not use a host shell, `bash -c`, `sh -c`, `chroot`, or `nsenter`. Do not
perform package, service, user, firewall, or destructive filesystem operations.

Host integrations must fail independently. Missing host data or Docker access
should produce a partial result and keep the app runtime healthy.
