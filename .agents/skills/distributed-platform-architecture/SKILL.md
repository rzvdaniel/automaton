---
name: distributed-platform-architecture
description: Use when changing Automaton control-plane state, runtime placement, runners, deployments, workers, messaging, scheduling, routing, persistent storage, container execution, or infrastructure providers.
license: Apache-2.0
---

# Distributed platform architecture

Load this skill before designing or implementing changes that cross the Automaton control plane, workers, runners, runtime placement, deployments, scheduling, routing, persistent storage, container execution, or infrastructure providers.

Automaton may run one API, one worker, or one provider today. That is a deployment choice, not a platform invariant. Design control-plane operations as if multiple API replicas, multiple worker replicas, and execution hosts from different providers already exist.

## Control plane

- Do not assume there is exactly one API or worker process.
- Do not require a request to return to the process that started an operation.
- Do not keep authoritative state only in process memory, and do not make a worker instance the permanent owner of an operation.
- Application runtime state belongs behind the application/runtime boundary rather than only in the API process.
- A runner connection to one API replica must not prevent other replicas from interacting with that runner.
- Use durable/shared state for information that must survive restarts or be visible across replicas. Local caches and other explicitly ephemeral state are allowed.

## Workers and messaging

Assume multiple worker replicas can consume concurrently, messages can be retried or delivered more than once, and a worker can fail midway through an operation. Prefer explicit operation or job IDs, idempotent and retry-safe behavior, concurrency-safe state transitions, and optimistic concurrency or distributed coordination where the operation actually requires it. Do not add distributed locks speculatively.

## Runners, hosts, and providers

- An app may run on a different machine from the API and from a worker.
- Runners may belong to Hetzner, OVH, AWS, Azure, customer infrastructure, bare metal, or a future provider.
- Do not make an application permanently dependent on a physical server, or confuse host identity or container identity with Automaton application/runtime identity.
- Keep the conceptual dependency direction as follows:

  ```text
  Automaton domain/application concepts
          ↓
  runtime / placement / runner abstractions
          ↓
  infrastructure implementations
          ↓
  Docker / Hetzner / Traefik / provider mechanisms
  ```

  Provider or container implementation details must not leak upward into core application semantics unless they are an unavoidable runner/runtime contract. The CLI, Studio, AI, and MCP are clients of API capabilities; reusable business and runtime capabilities should not exist only as CLI shell behavior.

## Application runtime and storage

- Address applications through stable Automaton application and runtime/deployment identities. Treat container IDs as execution artifacts.
- Keep the intended lifecycle close to `source → build → immutable artifact/image → deploy → runtime container`.
- Operational exec/job capabilities may exist, but live container mutation must not become the canonical application editing mechanism.
- Persistent storage changes placement. A movable container does not imply a movable application. Account for the authoritative volume, whether it is host-local or network-accessible, migration requirements, host loss, and storage affinity before changing placement or scheduling.
- Do not introduce distributed/network storage as part of a safeguard task unless the task explicitly requires it.

## Mandatory checklist

Apply these questions whenever touching runtime placement, Infrastructure Runners, deployments, workers, RabbitMQ or message handling, persistent volumes, scheduling, runtime routing, app lifecycle, container execution, control-plane state, providers, or distributed coordination:

- Would this still work with 3 API replicas?
- Would this still work with 10 worker replicas?
- Could the same message safely be delivered twice?
- What happens if the executing process dies halfway through?
- Where does authoritative state live? Is any required state available only in local process memory?
- Would this work if the app runs on another physical server, and if that server is not hosted by Hetzner?
- Does this couple Automaton semantics to Docker, Traefik, or provider details unnecessarily?
- Does persistent storage constrain placement?
- Am I using container identity where I should use Automaton application/runtime identity?
- Can another replica continue this operation safely?

## Safeguard scope

Prefer compiler/API boundaries and focused dependency or behavior tests where the current architecture exposes a stable seam. Document assumptions that cannot yet be enforced. Do not add Kubernetes, clustering, distributed locks, shared storage, provider migrations, or a new runner abstraction merely to satisfy this skill.
