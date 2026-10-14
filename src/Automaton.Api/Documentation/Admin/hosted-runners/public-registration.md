---
title: Pause Public Registration
order: 21
---

# Pause public registration

Signup and Hosted Runner provisioning have separate switches. Pausing signup does not disable existing accounts, revoke keys, change plans, delete data or stop workloads.

Deploy only the reviewed release hotfix based on the currently deployed revision; building current main could include unrelated Node changes and migrations. After the hotfix release, cherry-pick the same registration commit into main so later deployments retain the gate.

After deployment and approval, set the existing API `AppConfig:UserSignUp` to `false`. For the production Compose installation, edit only this property in the mounted `/opt/automaton/config/appsettings.Production.json`:

```json
"AppConfig": {
  "UserSignUp": false
}
```

Keep other properties in the section. An environment override `AppConfig__UserSignUp` takes precedence; set it to `false` too if one is configured. Apply through the normal API blue-green deployment workflow: the flag is bound at process startup. Editing the file alone is not sufficient for an already running process. Studio reads the state from the API; no Studio build-time setting is needed.

Verify `GET /api/Account/RegistrationStatus` reports `enabled: false`, `POST /api/Account/RegisterUser` returns 403, and GitHub authentication for a missing account returns the pause message without creating a user. Studio `/register` hides both signup controls and displays:

> Public registration is temporarily paused while we prepare Automaton for wider access.

Password login, GitHub login for existing accounts, refresh, email confirmation and password recovery remain available. Existing administrator-controlled project/organization account creation and invitation completion remain available. Invitation completion requires a valid unconsumed token; it is not public self-registration. The API does not register Identity's default registration UI.

To reopen, set the same property to `true` and apply it through the normal API deployment workflow. No invitation system or data migration is required.

## Existing-account execution review

Signup is not an execution safeguard. Review each account in Administration > Users for roles, assigned/effective plan and hosted-runner test-plan access, then inspect its runners, apps and pending execution operations. Unassigned accounts may inherit the default active plan. Test-plan visibility controls catalog access; it is not an execution suspension.

Hosted provisioning checks the platform provisioning switch, effective plan capability, active specification, account runner count, host capacity and storage locality. Existing execution depends on runner/app/project access and API-key permissions. Administrators have broader management rights.

Before wider access, independently validate live image provenance, tenant authorization on execution and secret grants, privileged Infrastructure Runner separation, CPU/memory/PID limits, capability dropping/no-new-privileges, filesystem and Docker-socket isolation, writable-layer/durable-storage limits, outbound network restrictions, cancellation/timeouts and retry behavior. Source inspection and unit tests do not establish live enforcement. Do not revoke permissions, change subscriptions or stop workloads as part of this review.

## GitHub login build configuration

Studio uses `VITE_GITHUB_AUTH_API_BASE_URL` when explicitly configured, otherwise it uses `VITE_API_BASE_URL`. Leave the GitHub override empty when both flows use the same API. These are build-time values: rebuilding Studio is required after changing them. The Docker image leaves the GitHub override empty so production Compose's configured API domain is used instead of a development localhost address.

When GitHub returns the exact registration-pause message for a missing account, Studio displays that message and offers **Back to Sign In**, without an immediate GitHub retry prompt. Other OAuth errors retain generic wording. Existing GitHub users continue through the normal sign-in flow.
