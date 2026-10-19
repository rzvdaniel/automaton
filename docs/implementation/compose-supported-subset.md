# Supported Compose subset

This is the accepted Compose contract for the M2 multi-service deployment slice. The CLI parses the file into the typed application contract, and the API/Runner validate that same contract before using the typed host-container boundary. Unsupported Compose fields fail validation before any image, volume, network or container is created.

## Supported

- Image-based services. Images go through Automaton's existing image validation and immutable digest resolution policy.
- Any number of internal services on the project network, with list-form `depends_on`, `expose`, `command` and `entrypoint`, plus `working_dir` and non-sensitive literal `environment` values. Scalar `command` and `entrypoint` are rejected; use argument lists so execution is unambiguous.
- At most one externally routed HTTP service, with one container port. Use a container-port-only entry such as `ports: ["8080"]`; Automaton binds it to the configured private runtime address and supplies the existing app route/access policy. Host IPs, host ports and published UDP ports are not accepted.
- Named app data volumes declared with an empty mapping (`data: {}`) and mounted at absolute container paths, optionally read-only. Automaton creates app/deployment-target-scoped Docker volumes with ownership labels. Updates reuse these volumes; project cleanup does not remove them.
- The existing server-derived CPU, memory, PID, shared-memory and log limits, applied to each service container.

Example:

```yaml
services:
  web:
    image: ghcr.io/example/web:1.2
    ports: ["8080"]
    environment:
      APP_MODE: production
    depends_on: [worker]
    volumes: [uploads:/srv/uploads]
  worker:
    image: ghcr.io/example/worker:1.2
    expose: ["9000"]
    volumes: [uploads:/srv/uploads:ro]
volumes:
  uploads: {}
```

Environment values in the Compose file are literal, non-sensitive configuration. They are part of app source/deployment metadata and may be visible to users who can inspect that app. Do not place credentials there.

## Rejected and deferred

The parser rejects unknown fields and forms, including `build`, Compose `secrets`, bind/host mounts, privileged mode, host networking, custom networks, devices, extra capabilities, arbitrary security settings, external/driver-configured volumes, host port mappings, multiple routed services, multiple ports, interpolation and environment-file references. Rejection happens before resource creation.

Compose builds and secure secret delivery are explicitly deferred. Source/Dockerfile builds require their own bounded build-container flow; credential delivery requires a protected secret contract from API storage to the workload without placing credentials in source, manifests, command lines or logs. Until both flows are implemented, do not use credential-dependent Compose examples or replace them with generated source files or log-visible values. Image-pull credentials are also not supplied through Compose environment fields.

The first delivery supports one HTTP route per app, not arbitrary ingress, TLS configuration, UDP, host-level networking or Docker Compose lifecycle hooks. Internal service names resolve on the project network; Automaton routes only the declared public service.
