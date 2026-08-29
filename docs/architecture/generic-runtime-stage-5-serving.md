# Stage 5: Generic deployment, health, and routing

Stage 5 completes the serving boundary for the generic application contract.
Automaton decides whether a deployment can be routed from deployment identity
and healthy runtime state; it does not decide how the application serves HTTP.

## Serving pipeline

```text
deployment package
    -> isolated runtime candidate
    -> TCP or configured HTTP health readiness
    -> durable Ready runtime
    -> route registration for app/target identity
    -> public or private application URL
```

`RunnerRuntimeProcessManager` remains the owner of candidate startup,
readiness probing, replacement, promotion, process draining, and restart
recovery. The route layer consumes the resulting runtime endpoint and target
identity. It does not inspect source files, artifacts, commands, or technology
names.

## Runtime gateway

The Runner runtime gateway forwards every application method and path to the
registered healthy loopback runtime port. It no longer serves a special static
frontend directory or reserves `/api` for a different execution path. An
application may use `/api`, `/`, or any other safe HTTP path according to its
own runtime contract.

Self-hosted browser traffic still uses the existing SignalR runtime tunnel to
reach the same gateway. The separate controller/FastAPI tunnel and Action
execution contract remain intentionally deferred; they are not part of the
generic deployment route.

## Control-plane route registration

`BackendRouteConfigService` emits one runtime router and one runtime rewrite per
private target and public slug. The route points at the hosted runner runtime
endpoint or the self-hosted runtime tunnel service and rewrites only the
external host/path envelope to the trusted app/target gateway path. There are
no frontend/API route branches in this generic path.

Routes are generated only for deployment targets marked as having a successful
runtime deployment. Replacement failure therefore leaves the existing route
and healthy runtime untouched; cleanup refreshes the same route set after the
target is stopped and removed.

The legacy API-side static publication projection and Studio/controller preview
helpers remain outside this generic runtime path for later migration. They are
not used by managed runtime route registration.

## Scope boundary

This stage does not redesign Studio, templates, CLI configuration, or the
Python Action/controller implementation. It also does not add generic build or
runtime execution behavior; those contracts and supervisors were established
in Stages 1–4.
