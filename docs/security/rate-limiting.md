# Automaton Rate Limiting

This document records the rate limiting currently implemented in Automaton, why each protected endpoint was chosen, and which additional endpoints are strong candidates for protection next.

## Implementation Location

Current implementation is split across:

- `src/Automaton.Studio.Server/Security/RateLimitingServiceCollectionExtensions.cs`
  - ASP.NET Core global limiter plus endpoint-specific policies
  - generic `429 Too Many Requests` response
- `src/Automaton.Studio.Server/Middleware/EmailRateLimitFilter.cs`
  - custom per-email throttling for selected auth endpoints
- controller attributes on protected endpoints

Policy names are defined in:

- `src/Automaton.Studio.Server/Constants/RateLimitPolicyNames.cs`

## Global Limiter

Automaton now also uses a broad ASP.NET Core `GlobalLimiter` that applies automatically to all requests before any endpoint-specific policy is evaluated.

Current baseline behavior:

- anonymous requests: per IP
- authenticated requests: per user identity
- public app execution routes: still keep their stricter endpoint-specific limits

Current baseline values:

- anonymous: 200 requests per minute per IP
- authenticated: 1000 requests per minute per user

Why this exists:

- catches broad internet noise and scanning across the whole API
- adds a safety net for unprotected endpoints
- stays loose enough that Studio/editor traffic is unlikely to break during normal use
- lets stricter auth and execution limits continue doing the real abuse control

## Cloudflare Complement

Cloudflare should sit in front of these API protections as a coarse edge layer, not as a replacement for backend rate limiting.

Recommended Cloudflare baseline for Automaton:

- rate limit login
- rate limit registration
- rate limit forgot-password / reset-password routes
- challenge suspicious IPs with Managed Challenge
- use Turnstile on public auth forms such as registration and forgot password

Recommended split:

- Cloudflare:
  - edge rate limits
  - suspicious-traffic challenge
  - form bot friction
- ASP.NET:
  - global baseline limiter
  - endpoint-specific limits
  - per-email auth throttles
  - later per-user/per-resource limits

Notes:

- Turnstile is best for form submissions.
- Managed Challenge is better than immediate hard blocking for early suspicious traffic.
- Enterprise WAF is not required for this baseline.

### Suggested Cloudflare rules checklist

1. `POST /api/Account/LoginUser`
   - action: `Managed Challenge`
   - suggested threshold: `10-20 requests per minute per IP`

2. `POST /api/Account/LoginRunnerUser`
   - action: `Managed Challenge`
   - suggested threshold: `10-20 requests per minute per IP`

3. `POST /api/Account/RegisterUser`
   - action: `Managed Challenge`
   - suggested threshold: `5-10 requests per hour per IP`

4. `POST /api/Account/ForgotUserPassword`
   - action: `Managed Challenge`
   - suggested threshold: `5-10 requests per hour per IP`

5. `POST /api/Account/ResetUserPassword`
   - action: `Managed Challenge` first
   - suggested threshold: `10-20 requests per hour per IP`

6. `GET /auth/github/login`
   - action: `Managed Challenge`
   - suggested threshold: `20-30 requests per 10 minutes per IP`

7. `GET /auth/github/callback`
   - action: `Managed Challenge`
   - suggested threshold: `30-60 requests per 10 minutes per IP`
   - note: keep this looser than login-start rules

8. `POST /api/web-apps/{appSlug}/controllers/{controllerName}/actions/{actionName}/execute`
   - action: `Managed Challenge`
   - suggested threshold: `20-60 requests per minute per IP`

9. `POST /api/web-apps/{appSlug}/flows/{flowIdentifier}/execute`
   - action: `Managed Challenge`
   - suggested threshold: `20-60 requests per minute per IP`

10. `POST /api/web-apps/{appSlug}/steps/{stepIdentifier}/execute`
   - action: `Managed Challenge`
   - suggested threshold: `20-60 requests per minute per IP`

11. Turnstile rollout
   - add to register form
   - add to forgot-password form
   - optionally add to login later if credential-stuffing noise shows up

12. Origin hygiene
   - keep the API behind Cloudflare
   - avoid exposing the origin IP publicly if possible

Cloudflare tuning notes:

- Keep Cloudflare thresholds looser than the ASP.NET endpoint-specific limits.
- Use Cloudflare as a coarse edge screen.
- Keep the API as the authoritative app-aware limiter.

Turnstile guidance:

- keep rate limiting as mandatory backend protection
- add Turnstile on registration and forgot-password from day one
- avoid Turnstile on normal login initially unless abuse shows up
- if login abuse appears, add Turnstile conditionally after repeated failed attempts rather than on the first login screen view

Turnstile local-development guidance:

- prefer Cloudflare's official test keys for local development
- expected local test site key: `1x00000000000000000000AA`
- expected local test secret key: `1x0000000000000000000000000000000AA`
- the widget will show a visible `For testing only` banner when those keys are used
- real production widgets may fail on `localhost` unless `localhost` is explicitly allowed in the widget hostname settings

## Protected So Far

### `POST /api/Account/RegisterUser` in `AccountController.cs`

Why: stops signup spam, fake-account floods, and basic bot abuse.

Current limits:

- per IP via `AuthRegistration`
- per email via `EmailRateLimit("auth:register:email", 3, 3600)`

### `POST /api/Account/LoginUser` in `AccountController.cs`

Why: slows brute force and credential stuffing against user accounts.

Current limits:

- per IP via `AuthLogin`
- per email via `EmailRateLimit("auth:login:email", 10, 900)`

### `POST /api/Account/LoginRunnerUser` in `AccountController.cs`

Why: same login abuse risk, but against runner-oriented auth.

Current limits:

- per IP via `AuthLogin`
- per email via `EmailRateLimit("auth:login-runner:email", 10, 900)`

### `POST /api/Account/ForgotUserPassword` in `AccountController.cs`

Why: prevents reset-email flooding and reduces password-reset abuse against a single account.

Current limits:

- per IP via `AuthForgotPassword`
- per email via `EmailRateLimit("auth:forgot-password:email", 3, 3600)`

### `POST /api/Account/ResendConfirmation` in `AccountController.cs`

Why: confirmation resend can be abused for notification spam and should stay throttled even though the endpoint is authenticated.

Current limits:

- per IP via `AuthResendConfirmation`

### `POST /api/Account/ResetUserPassword` in `AccountController.cs`

Why: slows repeated password reset attempts and token guessing.

Current limits:

- per IP via `AuthResetPassword`

### `POST /api/Token/RefreshAccessToken` in `TokenController.cs`

Why: prevents refresh-token hammering and noisy abuse against token rotation.

Current limits:

- per IP via `AuthRefreshToken`

### `POST /api/Token/RefreshRunnerAccessToken` in `TokenController.cs`

Why: same token-abuse risk as normal refresh, but on the runner auth path.

Current limits:

- per IP via `AuthRefreshToken`

### `POST /api/apikeys/validate` in `ApiKeysController.cs`

Why: anonymous credential-validation endpoints are attractive for key spraying and probing.

Current limits:

- per IP via `ApiKeyValidation`

### `POST /api/web-apps/{appSlug}/controllers/{controllerName}/actions/{actionName}/execute` in `AppsControllerExecutionController.cs`

Why: anonymous published app execution can trigger real backend work and is an abuse surface for runner exhaustion and cost amplification.

Current limits:

- per IP via `PublicAppControllerExecution`

### `POST /api/web-apps/{appSlug}/flows/{flowIdentifier}/execute` in `AppsFlowExecutionController.cs`

Why: anonymous published flow execution can trigger runner work and should be protected against burst abuse and repeated public execution pressure.

Current limits:

- per IP via `PublicAppFlowExecution`

### `POST /api/web-apps/{appSlug}/steps/{stepIdentifier}/execute` in `AppsStepExecutionController.cs`

Why: anonymous published custom-step execution is another direct backend execution surface and is worth protecting for the same exhaustion and cost reasons.

Current limits:

- per IP via `PublicAppStepExecution`

## Current Policy Values

These are configured under `RateLimiting` in `appsettings.json` and bound by `RateLimitingOptions.cs`.

- `GlobalAnonymous`: 200 requests per minute per IP
- `GlobalAuthenticated`: 1000 requests per minute per user
- `AuthLogin`: 5 requests per minute per IP
- `AuthRegistration`: 3 requests per hour per IP
- `AuthForgotPassword`: 10 requests per hour per IP
- `AuthResendConfirmation`: 3 requests per hour per IP
- `AuthResetPassword`: 5 requests per hour per IP
- `AuthRefreshToken`: 30 requests per 5 minutes per IP
- `ApiKeyValidation`: 20 requests per minute per IP
- `PublicAppControllerExecution`: 10 requests per minute per IP
- `PublicAppFlowExecution`: 10 requests per minute per IP
- `PublicAppStepExecution`: 10 requests per minute per IP

Custom per-email throttles:

- register: 3 per hour per email
- login: 10 per 15 minutes per email
- login runner: 10 per 15 minutes per email
- forgot password: 3 per hour per email

## Why These Were Prioritized

These endpoints were protected first because they match the most common early abuse patterns for a small SaaS:

- broad anonymous API scanning and noisy request floods
- account creation spam
- brute-force login attempts
- credential stuffing
- password reset abuse
- token refresh hammering
- API key probing

These are high-value protections because they are cheap to implement and directly reduce common compromise and abuse paths before public launch.

## Endpoints Worth Protecting Next

The list below is ordered by practical priority based on the current codebase.

### Priority 1: GitHub auth entrypoints

Routes:

- `GET /auth/github/login` in `GitHubAuthController.cs`
- `GET /auth/github/callback` in `GitHubAuthController.cs`

Why:

- login start can be abused for repeated external-auth initiation noise
- callback can be hammered to generate app noise and error-path churn
- callback currently returns tokens to the frontend callback flow, so reducing noisy abuse there is reasonable

Recommendation:

- rate limit `login` by IP
- consider lighter rate limiting on `callback` by IP to protect error-path churn without breaking legitimate auth flows

### Priority 2: Public app execution endpoints

Routes:

- `POST /api/web-apps/{appSlug}/controllers/{controllerName}/actions/{actionName}/execute` in `AppsControllerExecutionController.cs`
- `POST /api/web-apps/{appSlug}/flows/{flowIdentifier}/execute` in `AppsFlowExecutionController.cs`
- `POST /api/web-apps/{appSlug}/steps/{stepIdentifier}/execute` in `AppsStepExecutionController.cs`

Why:

- these are `AllowAnonymous` and enforce authentication conditionally based on app settings
- they can trigger real runner work and backend execution
- they are good cost-amplification and runner-exhaustion targets

Recommendation:

- add per-IP rate limiting to all published execution routes
- use stricter limits for execution than for simple GET endpoints
- consider separate policies for:
  - controller actions
  - flow execution
  - step execution

### Priority 3: Runner-facing endpoints

Routes:

- `POST /api/Runners` in `RunnersController.cs`
- `PUT /api/Runners/{id}` in `RunnersController.cs`
- `GET /api/Runners/by-name/{name}` in `RunnersController.cs`
- `GET /api/Runners/{id}/script-settings` in `RunnersController.cs`
- `PUT /api/Runners/{id}/script-settings` in `RunnersController.cs`

Why:

- these are machine-facing and allowed under `AllowApiKey`
- even though they are not anonymous, machine-facing endpoints get probed heavily
- they are part of runner registration, metadata, and operational control surfaces

Recommendation:

- add per-IP or per-key rate limiting to `POST` and `PUT` runner endpoints first
- be more conservative on `GET` endpoints unless telemetry shows noise
- keep `GetStatus` lower priority since it is authenticated and lightweight

### Priority 4: File upload and preview surfaces

Routes:

- `POST /api/web-apps/preview/{appId}/files/upload` in `AppFilesController.cs`
- `POST /api/web-apps/{slug}/files/upload` in `AppFilesController.cs`
- `GET /api/web-apps/preview/{appId}/files/{sessionId}` in `AppFilesController.cs`
- `GET /api/web-apps/{slug}/files/{sessionId}` in `AppFilesController.cs`
- `POST /api/apps/{appId}/preview` in `AppPreviewController.cs`
- `GET /app-preview/{userId}/{appId}/{*path}` in `AppPreviewController.cs`

Why:

- file and preview endpoints are classic resource-exhaustion targets
- uploads are especially worth protecting because they consume bandwidth, memory, and runner coordination
- preview serving is less sensitive than uploads, but still worth watching if it becomes noisy

Recommendation:

- rate limit upload endpoints before download/preview GET endpoints
- keep existing file-size limits and add request-rate limits as a second layer

### Priority 5: AI conversation endpoints

Routes:

- `POST /api/ai/app/conversation/stream` in `AiAppConversationController.cs`
- `POST /api/ai/step/conversation/stream` in `AiStepConversationController.cs`

Why:

- these endpoints are authenticated, but they are expensive
- they are direct token-burn and compute-burn targets
- authenticated abuse still matters if a single user or stolen token can drive large cost

Recommendation:

- add authenticated-user rate limiting, not just IP limiting
- rate limit by user ID and possibly by resource ID for app/step sessions
- keep these behind auth and complement with budget caps where appropriate

### Priority 6: Email confirmation resend

Route:

- not implemented yet, but should be protected when Phase 3 adds it

Why:

- same abuse class as forgot-password
- high spam potential with low attacker cost

Recommendation:

- use the same shape as forgot-password:
  - per IP limit
  - per email limit

## Selection Rule

Good candidates for rate limiting are:

- anonymous auth endpoints
- anonymous credential-validation endpoints
- expensive compute endpoints
- public execution endpoints
- machine-facing endpoints

Ordinary authenticated CRUD endpoints usually do not need aggressive rate limiting unless abuse appears in logs or telemetry.

## Testing

Current automated coverage includes:

- unit tests for the custom per-email filter in `EmailRateLimitFilterTests.cs`
- lightweight ASP.NET pipeline tests for:
  - login
  - forgot password
  - reset password
  - refresh token
  - API key validation

Test files:

- `src/UnitTests/Automaton.Studio.Server.Tests/EmailRateLimitFilterTests.cs`
- `src/UnitTests/Automaton.Studio.Server.Tests/RateLimitingPipelineTests.cs`
