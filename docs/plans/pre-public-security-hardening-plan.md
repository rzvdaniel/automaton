# Automaton Pre-Public Security Hardening Plan

## Goal

Before announcing Automaton publicly, prioritize the most common early-stage SaaS compromise paths over deep edge-case hardening. Focus first on auth abuse, authorization gaps, token handling, secret exposure, and runner-facing attack surfaces.

This plan is implementation-oriented and mapped to the current codebase.

Related implementation notes:

- `docs/security/rate-limiting.md`

## Priorities

1. Rate limit high-risk anonymous and token endpoints.
2. Enforce lockout and email confirmation in auth flows.
3. Harden refresh token handling and reduce browser token exposure.
4. Audit server-side authorization across apps, flows, steps, projects, organizations, and runners.
5. Add cheap high-value platform hardening: security headers, input/request limits, generic auth responses.
6. Review secrets, logging, and runner-specific attack surfaces.

## Current State

Useful building blocks already exist:

- Refresh token rotation exists in `src/Automaton.Studio.Server/Application/Queries/Handlers/RefreshAccessTokenQueryHandler.cs`.
- Project-scoped authorization handlers exist in `src/Automaton.Studio.Server/Authorization/ProjectAuthorizationHandlers.cs`.
- User API keys are hashed and raw values are only returned at creation in:
  - `src/Automaton.Studio.Server/Services/ApiKeyService.cs`
  - `src/Automaton.Studio.Server/Controllers/ApiKeysController.cs`

Visible gaps from the current implementation:

- No app-wide ASP.NET rate limiting wiring was found in `src/Automaton.Studio.Server/Program.cs`.
- Login currently uses direct password validation in `src/Automaton.Studio.Server/Application/Queries/Handlers/SignInUserQueryHandler.cs`, which does not appear to enforce lockout.
- Access and refresh tokens are stored in `localStorage` in `src/automaton-studio-vue/src/shared/services/api/axios-instance.ts`.
- Email confirmation fields exist on users, but registration/login do not appear to enforce confirmed email before normal product use.
- Security header middleware was not found in the API bootstrap.

## Phase 1: Rate Limiting

### Objective

Make brute force, credential stuffing, spam registration, password reset abuse, and token hammering materially harder.

### Backend changes

Add ASP.NET Core rate limiting in:

- `src/Automaton.Studio.Server/Program.cs`

Apply named policies to:

- `src/Automaton.Studio.Server/Controllers/AccountController.cs`
  - `RegisterUser`
  - `LoginUser`
  - `LoginRunnerUser`
  - `ForgotUserPassword`
  - `ResetUserPassword`
- `src/Automaton.Studio.Server/Controllers/TokenController.cs`
  - `RefreshAccessToken`
  - `RefreshRunnerAccessToken`
- `src/Automaton.Studio.Server/Controllers/ApiKeysController.cs`
  - `ValidateApiKey`

### Suggested starting limits

- Login: 5 per minute per IP, plus 10 per 15 minutes per email
- Registration: 3 per hour per IP
- Forgot password: 3 per hour per email, 10 per hour per IP
- Reset password: 5 per hour per IP
- Refresh token: 30 per 5 minutes per IP
- API key validate: 20 per minute per IP

### Notes

- Return generic 429 responses.
- Do not leak whether an email address exists.
- Use stricter limits on reset and API key validation than on login.

### Bot Protection

Use Cloudflare Turnstile or equivalent on the highest-abuse public forms first.

Vue placement:

- `src/automaton-studio-vue/src/features/auth/views/RegisterView.vue`
- `src/automaton-studio-vue/src/features/auth/views/ForgotPasswordView.vue`
- Optional later if needed: `src/automaton-studio-vue/src/features/auth/views/LoginView.vue`

ASP.NET placement:

- verify the Turnstile token in `src/Automaton.Studio.Server/Controllers/AccountController.cs`
- enforce verification on:
  - `RegisterUser`
  - `ForgotUserPassword`
  - optional later: `LoginUser`

Implementation notes:

- Start with registration and forgot-password only.
- Keep rate limiting and lockout even when bot protection is enabled.
- Treat Turnstile as an additional abuse-control signal, not a replacement for backend protections.
- Keep server responses generic when verification fails.

## Phase 2: Lockout and Failed Login Protection

### Objective

Stop repeated login guessing against the same account.

### Backend changes

Update auth flow in:

- `src/Automaton.Studio.Server/Application/Queries/Handlers/SignInUserQueryHandler.cs`

Replace plain password validation with Identity sign-in flow that:

- increments failed attempts
- enforces lockout
- resets failure counters on success

Configure lockout options in:

- `src/Automaton.Studio.Server/Program.cs`

### Suggested defaults

- `MaxFailedAccessAttempts = 5`
- `DefaultLockoutTimeSpan = 15 minutes`
- `AllowedForNewUsers = true`

### Notes

- Keep login error messages generic: `Invalid credentials.`
- Do not reveal whether the account exists or is locked.

## Phase 3: Email Confirmation

### Objective

Reduce fake-account churn and prevent unverified accounts from using sensitive product features.

### Backend changes

Add confirmation-token generation and email flow in:

- `src/Automaton.Studio.Server/Controllers/AccountController.cs`

Add confirm-email and resend-confirmation endpoints.

### Frontend changes

Update:

- `src/automaton-studio-vue/src/features/auth/views/RegisterView.vue`
- `src/automaton-studio-vue/src/features/auth/views/LoginView.vue`

Add confirmation handling route/view in the auth feature.

### Enforcement

Block login entirely, or at minimum block these actions until email is confirmed:

- API key creation
- app publishing
- runner registration
- billing-sensitive actions

### Notes

- Rate limit resend-confirmation flow.
- Keep responses generic.

## Phase 4: Token Handling Hardening

### Objective

Reduce the blast radius of XSS or browser-side token theft.

### Current gap

Tokens are stored in browser `localStorage` in:

- `src/automaton-studio-vue/src/shared/services/api/axios-instance.ts`

GitHub callback currently passes tokens through the frontend flow in:

- `src/automaton-studio-vue/src/features/auth/views/GitHubCallbackView.vue`

### Changes

Move refresh token to a secure `HttpOnly` cookie.

Keep access token short-lived.

Revoke refresh tokens on:

- password change
- password reset
- explicit logout

Review GitHub callback token delivery to avoid query-string exposure in browser history, logs, and third-party tooling.

## Phase 5: Server-Side Authorization Audit

### Objective

Find and close cross-user, cross-project, and cross-organization data access bugs before public traffic arrives.

### Controllers to audit first

- `src/Automaton.Studio.Server/Controllers/AppsController.cs`
- `src/Automaton.Studio.Server/Controllers/AppsControllerExecutionController.cs`
- `src/Automaton.Studio.Server/Controllers/AppsFlowExecutionController.cs`
- `src/Automaton.Studio.Server/Controllers/AppsStepExecutionController.cs`
- `src/Automaton.Studio.Server/Controllers/AppFilesController.cs`
- `src/Automaton.Studio.Server/Controllers/AppRunnerBackendFilesController.cs`
- `src/Automaton.Studio.Server/Controllers/FlowsController.cs`
- `src/Automaton.Studio.Server/Controllers/FlowExecutionsController.cs`
- `src/Automaton.Studio.Server/Controllers/CustomStepsController.cs`
- `src/Automaton.Studio.Server/Controllers/RunnersController.cs`
- `src/Automaton.Studio.Server/Controllers/ProjectsController.cs`
- `src/Automaton.Studio.Server/Controllers/ProjectResourcesController.cs`
- `src/Automaton.Studio.Server/Controllers/ProjectMembersController.cs`
- `src/Automaton.Studio.Server/Controllers/OrganizationsController.cs`
- `src/Automaton.Studio.Server/Controllers/UserAppsController.cs`

### What to verify

Every `get by id`, `update`, `delete`, `execute`, `publish`, `logs`, `download`, and file-management endpoint must enforce ownership or membership on the server, not just in the frontend.

### Tests

Add denial-path tests in:

- `src/UnitTests`

Focus on:

- cross-user app access
- cross-project resource access
- cross-org access
- runner/file access outside authorized scope

## Phase 6: Cheap Hardening With High Payoff

### Objective

Raise baseline security without large architecture changes.

### Changes

Add security headers middleware in:

- `src/Automaton.Studio.Server/Program.cs`

Headers to add:

- `Content-Security-Policy`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy`
- `Permissions-Policy`
- `X-Frame-Options` or CSP `frame-ancestors`

Add request-size and upload-size limits for:

- app files
- screenshots/media uploads
- runner file paths
- logs or other large request bodies

Make forgot/reset flows return generic responses that do not reveal account existence.

Add basic bot protection on:

- registration
- forgot password

## Phase 7: Secrets, Logging, and Runner Surfaces

### Objective

Prevent accidental credential leakage and secure the most sensitive non-browser interfaces.

### Secret/logging review

Review these areas for accidental raw-secret exposure:

- `src/Automaton.Studio.Server/Program.cs`
- `src/Automaton.Studio.Server/Controllers/ApiKeysController.cs`
- `src/Automaton.Studio.Server/Controllers/AiApiKeysController.cs`
- `src/Automaton.Studio.Server/Controllers/SensitiveVariablesController.cs`
- auth controllers and handlers

Confirm that logs never capture:

- raw API keys
- AI provider keys
- refresh tokens
- password reset tokens
- sensitive variables

### Runner-focused review

Review and harden:

- runner registration
- API key validation
- runner-side file browsing
- runner-side file deletion

Primary surfaces:

- `src/Automaton.Studio.Server/Controllers/RunnersController.cs`
- `src/Automaton.Studio.Server/Controllers/ApiKeysController.cs`
- `src/Automaton.Studio.Server/Controllers/AppRunnerBackendFilesController.cs`

### Notes

- Rate limit runner registration and API key validation too.
- Verify runner file operations cannot escape authorized app directories.

## Recommended Execution Order

1. Implement Phase 1 rate limiting.
2. Implement Phase 2 lockout and failed login protection.
3. Implement Phase 3 email confirmation.
4. Implement Phase 4 token hardening.
5. Run Phase 5 authorization audit and add denial tests.
6. Implement Phase 6 security headers and request limits.
7. Complete Phase 7 secret/logging and runner review.

## Definition of Done

This plan is complete when:

- auth endpoints are rate-limited
- login lockout is enforced
- email confirmation is enforced for normal use
- refresh token handling no longer depends on `localStorage`
- core resource controllers pass cross-tenant denial tests
- security headers and request limits are in place
- secret exposure paths have been reviewed and hardened
- runner-facing endpoints have equivalent abuse protection
