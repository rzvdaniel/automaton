# Infrastructure Runner reprovisioning

Infrastructure Runner replacement is intentionally a host-side operation in the simplified Stage 4 design. Automaton does not ask an Infrastructure Runner to replace itself.

Administrators run the signed installer directly on the host:

```bash
curl -fsSL https://api.automaton.monster/api/infrastructure-runner/installer/latest.sh \
  | sudo bash -s -- --repair
```

The installer takes a host-local lock, preserves the canonical identity and configuration directory, resolves the configured immutable image, replaces and health-checks the container, and restores the previous container if replacement fails. Its exit status is the authoritative result.

For a pinned update or repair, pass `--version` with a full image reference in either `repository:tag` or `repository@sha256:<64 lowercase hex characters>` form. Do not pass a bare digest. The installer rejects whitespace and malformed references before it fetches RabbitMQ configuration or pulls the image. It pulls and resolves the replacement image before it removes the existing container.

The API remains observational: it records Runner disconnect/reconnect, current release metadata, and whether the host is online, Ready, and schedulable. While the Infrastructure Runner is disconnected or not Ready, new Hosted Runner placement and host workflows are rejected; existing application Runner containers are not affected.

The Studio administrator view should expose the command above alongside the host's current image digest and release version. Future unattended reprovisioning should use an independent host supervisor (for example, a systemd service), rather than a container replacing itself through the control plane.
