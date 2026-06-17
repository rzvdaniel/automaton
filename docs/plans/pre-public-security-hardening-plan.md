# Automaton Pre-Public Security Hardening Plan

## Goal

Before announcing Automaton publicly, prioritize the most common early-stage SaaS compromise paths over deep edge-case hardening. Focus first on auth abuse, authorization gaps, token handling, secret exposure, and runner-facing attack surfaces.

This plan is implementation-oriented and mapped to the current codebase.

Related implementation notes:

- `docs/security/rate-limiting.md`
- `docs/security/auth-token-hardening.md`

## Priorities

1. Rate limit high-risk anonymous and token endpoints.
2. Enforce lockout and email confirmation in auth flows.
3. Harden refresh token handling and reduce browser token exposure.
4. Audit server-side authorization across apps, flows, steps, projects, organizations, and runners.
5. Add cheap high-value platform hardening: security headers, input/request limits, generic auth responses.
6. Review secrets, logging, and runner-specific attack surfaces.

## Current State

Useful building blocks already exist:

- Refresh token rotation exists in `src/Automaton.Api/Application/Queries/Handlers/RefreshAccessTokenQueryHandler.cs`.
- Project-scoped authorization handlers exist in `src/Automaton.Api/Authorization/ProjectAuthorizationHandlers.cs`.
- User API keys are hashed and raw values are only returned at creation in:
  - `src/Automaton.Api/Services/ApiKeyService.cs`
  - `src/Automaton.Api/Controllers/ApiKeysController.cs`

Visible gaps from the current implementation:

- Security header middleware was not found in the API bootstrap.
- A broad authorization audit across apps, flows, steps, projects, organizations, and runners is still pending.
- Access tokens are still browser-readable and need to remain short-lived.
- Refresh token revocation needs an explicit verification pass on logout, password change, and password reset flows.

## Phase 1: Rate Limiting

### Objective

Make brute force, credential stuffing, spam registration, password reset abuse, and token hammering materially harder.

### Backend changes

Add ASP.NET Core rate limiting in:

- `src/Automaton.Api/Program.cs`

Apply named policies to:

- `src/Automaton.Api/Controllers/AccountController.cs`
  - `RegisterUser`
  - `LoginUser`
  - `ForgotUserPassword`
  - `ResetUserPassword`
- `src/Automaton.Api/Controllers/TokenController.cs`
  - `RefreshAccessToken`
- `src/Automaton.Api/Controllers/ApiKeysController.cs`
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

Cloudflare edge protection should complement the API protections, not replace them.

Cloudflare recommendations for pre-announcement:

- Turnstile on registration and forgot-password forms
- rate limit login
- rate limit registration
- rate limit password reset / forgot-password endpoints
- challenge suspicious IPs with Managed Challenge rather than hard blocking first
- rate limit public execution paths if they are exposed through Cloudflare

Recommended split of responsibility:

- Cloudflare:
  - rate limiting on public auth routes
  - challenge suspicious traffic
  - basic bot friction on public forms
- API:
  - endpoint-specific limits
  - global loose baseline limits
  - per-email auth throttles
  - per-user limits later for authenticated expensive endpoints

Notes:

- Enterprise WAF is not required for this baseline.
- Turnstile is best for form submissions.
- Managed Challenge is best for suspicious edge traffic.
- Keep backend protections in place even when Cloudflare rules exist.

Suggested Cloudflare rules checklist:

1. Login edge rule
   Path:
   - `POST /api/Account/LoginUser`
   Suggested action:
   - `Managed Challenge`
   Suggested starting threshold:
   - `10-20 requests per minute per IP`

2. Registration edge rule
   Path:
   - `POST /api/Account/RegisterUser`
   Suggested action:
   - `Managed Challenge`
   Suggested starting threshold:
   - `5-10 requests per hour per IP`

3. Forgot-password edge rule
   Path:
   - `POST /api/Account/ForgotUserPassword`
   Suggested action:
   - `Managed Challenge`
   Suggested starting threshold:
   - `5-10 requests per hour per IP`

4. Reset-password edge rule
   Path:
   - `POST /api/Account/ResetUserPassword`
   Suggested action:
   - `Managed Challenge` first, `Block` later if abuse is obvious
   Suggested starting threshold:
   - `10-20 requests per hour per IP`

5. GitHub auth entrypoint rule
   Path:
   - `GET /auth/github/login`
   Suggested action:
   - `Managed Challenge`
   Suggested starting threshold:
   - `20-30 requests per 10 minutes per IP`

6. GitHub callback rule
   Path:
   - `GET /auth/github/callback`
   Suggested action:
   - `Managed Challenge`
   Suggested starting threshold:
   - `30-60 requests per 10 minutes per IP`
   Notes:
   - keep this looser than login start to avoid breaking legitimate auth flows

7. Public app execution rules
   Paths:
   - `POST /api/web-apps/{appSlug}/controllers/{controllerName}/actions/{actionName}/execute`
   - `POST /api/web-apps/{appSlug}/flows/{flowIdentifier}/execute`
   - `POST /api/web-apps/{appSlug}/steps/{stepIdentifier}/execute`
   Suggested action:
   - `Managed Challenge`
   Suggested starting threshold:
   - `20-60 requests per minute per IP`
   Notes:
   - keep Cloudflare looser than the stricter ASP.NET per-endpoint limits

8. Turnstile rollout
   Forms:
   - register
   - forgot password
   Optional later:
   - login
   Notes:
   - verify on the server
   - keep generic failure responses

9. Origin protection
   Actions:
   - keep the API behind Cloudflare
   - avoid exposing the origin IP publicly if possible
   - use Cloudflare as the normal public entry point for auth and app traffic

Rule-shaping notes:

- Prefer `Managed Challenge` before `Block` for login and registration.
- Use `Block` only after you see clearly abusive patterns that should never be legitimate.
- Keep Cloudflare thresholds looser than ASP.NET endpoint-specific thresholds so Cloudflare acts as a coarse edge screen and the API keeps the authoritative app-aware limits.

Vue placement:

- `src/automaton-studio-vue/src/features/auth/views/RegisterView.vue`
- `src/automaton-studio-vue/src/features/auth/views/ForgotPasswordView.vue`
- Optional later if needed: `src/automaton-studio-vue/src/features/auth/views/LoginView.vue`

ASP.NET placement:

- verify the Turnstile token in `src/Automaton.Api/Controllers/AccountController.cs`
- enforce verification on:
  - `RegisterUser`
  - `ForgotUserPassword`
  - optional later: `LoginUser`

Implementation notes:

- Start with registration and forgot-password only.
- Keep rate limiting and lockout even when bot protection is enabled.
- Treat Turnstile as an additional abuse-control signal, not a replacement for backend protections.
- Keep server responses generic when verification fails.

Turnstile versus rate limiting:

- rate limiting protects API capacity and slows brute force, spam bursts, and repeated reset attempts
- Turnstile adds bot friction before sensitive public forms reach the backend
- they solve different problems and should be used together on the highest-abuse flows

Recommended rollout policy:

| Area | Rate limiting | Turnstile |
|------|---------------|-----------|
| Login | Yes | Only after suspicious attempts or later if abuse appears |
| Registration | Yes | Yes |
| Password reset / forgot password | Yes | Yes |
| Contact / support forms | Yes | Yes |
| App browsing / public pages | Maybe | No |
| Authenticated API calls | Yes | No |

Login rollout notes:

1. let users try login normally
2. if the same IP or email shows repeated failed attempts, require Turnstile
3. if failures continue, rely on lockout and throttling

Practical recommendation:

- keep rate limiting mandatory on sensitive routes
- add Turnstile immediately on registration and forgot-password
- do not add Turnstile to normal login on day one unless telemetry shows abuse

Local testing note:

- use Cloudflare's official Turnstile test keys in development
- expect the widget to show a `For testing only` banner locally
- do not use real production widget keys for localhost unless the widget hostname allowlist explicitly includes `localhost`

## Phase 2: Lockout and Failed Login Protection

### Objective

Stop repeated login guessing against the same account.

### Backend changes

Update auth flow in:

- `src/Automaton.Api/Application/Queries/Handlers/SignInUserQueryHandler.cs`

Replace plain password validation with Identity sign-in flow that:

- increments failed attempts
- enforces lockout
- resets failure counters on success

Configure lockout options in:

- `src/Automaton.Api/Program.cs`

### Suggested defaults

- `MaxFailedAccessAttempts = 5`
- `DefaultLockoutTimeSpan = 15 minutes`
- `AllowedForNewUsers = true`

### Notes

- Keep login error messages generic: `Invalid credentials.`
- Do not reveal whether the account exists or is locked.

## Phase 3: Email Confirmation With Sensitive-Action Gating

### Objective

Reduce fake-account churn and prevent unverified accounts from using sensitive product features, without blocking basic product onboarding.

### Backend changes

Add confirmation-token generation and email flow in:

- `src/Automaton.Api/Controllers/AccountController.cs`

Add endpoints:

- `POST /api/Account/ResendConfirmation`
- `GET /api/Account/ConfirmEmail?userId=...&token=...`

### Frontend changes

Update:

- `src/automaton-studio-vue/src/features/auth/views/RegisterView.vue`
- `src/automaton-studio-vue/src/features/auth/views/LoginView.vue`

Add a persistent “Confirm your email” banner in Studio and a confirmation-result handling route/view in the auth feature.

### Enforcement

Allow login for unconfirmed users, but block sensitive actions until email is confirmed:

- API key creation
- app publishing
- runner registration
- hosted runner usage/provisioning
- billing-sensitive actions
- email/password change
- invitations/org-sharing flows
- optionally backend deployment actions

Still allow:

- login
- browsing Studio
- creating draft/local apps
- editing app source
- resending confirmation
- onboarding/docs/basic exploration

### Notes

- Rate limit resend-confirmation flow.
- Keep resend responses generic.
- URL-encode the token in the email link.
- Redirect the confirm-email endpoint back to Studio with a success/failure indicator.
- Enforce sensitive-action gating on the server, not only in the frontend.

## Phase 4: Token Handling Hardening

### Objective

Reduce the blast radius of XSS or browser-side token theft.

### Implemented so far

- refresh token moved out of browser `localStorage`
- refresh token now uses a secure `HttpOnly` cookie via `src/Automaton.Api/Security/RefreshTokenCookieService.cs`
- Studio auth now keeps the access token in memory instead of persistent browser storage in `src/automaton-studio-vue/src/shared/services/api/axios-instance.ts`
- refresh endpoint rotates the refresh token and rewrites the cookie in `src/Automaton.Api/Controllers/TokenController.cs`
- logout clears the refresh-token cookie and revokes the server-side refresh token in `src/Automaton.Api/Controllers/TokenController.cs`
- GitHub callback no longer sends the refresh token through the frontend callback flow in `src/Automaton.Api/Controllers/GitHubAuthController.cs`

### Remaining checks

- verify refresh-token revocation also happens on password change and password reset flows
- confirm cookie settings stay correct across localhost, tunnel, staging, and production origins
- keep access token lifetime short enough that a stolen in-memory token has a limited window
- review any remaining frontend auth flows for accidental token persistence or logging

## Phase 5: Server-Side Authorization Audit

### Objective

Find and close cross-user, cross-project, and cross-organization data access bugs before public traffic arrives.

### Controllers to audit first

- `src/Automaton.Api/Controllers/AppsController.cs`
- `src/Automaton.Api/Controllers/AppsControllerExecutionController.cs`
- `src/Automaton.Api/Controllers/AppsFlowExecutionController.cs`
- `src/Automaton.Api/Controllers/AppsStepExecutionController.cs`
- `src/Automaton.Api/Controllers/AppFilesController.cs`
- `src/Automaton.Api/Controllers/AppRunnerBackendFilesController.cs`
- `src/Automaton.Api/Controllers/FlowsController.cs`
- `src/Automaton.Api/Controllers/FlowExecutionsController.cs`
- `src/Automaton.Api/Controllers/CustomStepsController.cs`
- `src/Automaton.Api/Controllers/RunnersController.cs`
- `src/Automaton.Api/Controllers/ProjectsController.cs`
- `src/Automaton.Api/Controllers/ProjectResourcesController.cs`
- `src/Automaton.Api/Controllers/ProjectMembersController.cs`
- `src/Automaton.Api/Controllers/OrganizationsController.cs`
- `src/Automaton.Api/Controllers/UserAppsController.cs`

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

- `src/Automaton.Api/Program.cs`

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

- `src/Automaton.Api/Program.cs`
- `src/Automaton.Api/Controllers/ApiKeysController.cs`
- `src/Automaton.Api/Controllers/AiApiKeysController.cs`
- `src/Automaton.Api/Controllers/SensitiveVariablesController.cs`
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

- `src/Automaton.Api/Controllers/RunnersController.cs`
- `src/Automaton.Api/Controllers/ApiKeysController.cs`
- `src/Automaton.Api/Controllers/AppRunnerBackendFilesController.cs`

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
