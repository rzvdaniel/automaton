# Automaton Auth Token Hardening

This document records the current token-handling model in Automaton after the pre-launch hardening pass.

## Current Model

- access token: short-lived bearer token
- access token storage: in-memory only on the Studio frontend
- refresh token: server-issued `HttpOnly` cookie
- refresh token storage: browser cookie, not readable from JavaScript
- refresh token rotation: on every successful refresh
- logout: revokes the server-side refresh token and clears the cookie

## Why This Model

The main goal is reducing the impact of browser-side token theft.

- tokens in `localStorage` are easy to steal if an XSS bug appears
- `HttpOnly` cookies are not readable from JavaScript
- in-memory access tokens disappear on full reload unless the cookie-backed refresh flow restores the session

This does not remove all XSS risk, but it removes the highest-value long-lived secret from the JavaScript runtime.

## Backend Implementation

Primary files:

- `src/Automaton.Studio.Server/Security/RefreshTokenCookieService.cs`
- `src/Automaton.Studio.Server/Controllers/AccountController.cs`
- `src/Automaton.Studio.Server/Controllers/TokenController.cs`
- `src/Automaton.Studio.Server/Controllers/GitHubAuthController.cs`
- `src/Automaton.Studio.Server/Application/Queries/Handlers/RefreshAccessTokenQueryHandler.cs`
- `src/Automaton.Studio.Server/Application/Commands/Handlers/RevokeRefreshTokenCommandHandler.cs`

Current behavior:

- login issues an access token and refresh token
- API writes the refresh token into the `automaton_refresh_token` cookie
- API clears the refresh-token field before returning the auth response to the frontend
- refresh reads the cookie when the request body does not provide a token
- refresh rotates the server-side token and rewrites the cookie
- logout revokes the server-side refresh token and clears the cookie
- password change revokes all refresh tokens for that user and clears the current refresh cookie
- password reset revokes all refresh tokens for that user
- GitHub OAuth callback also uses the cookie flow and does not send the refresh token through the frontend callback URL

## Cookie Settings

Refresh-token cookie settings currently include:

- `HttpOnly = true`
- `Secure = true`
- `Path = /`
- `IsEssential = true`
- `SameSite = Lax` for normal environments
- `SameSite = None` for localhost / `127.0.0.1` development

Why localhost is different:

- local Studio and local API often run on different origins during development
- `SameSite=None` avoids breaking cookie-based refresh in that setup

## Frontend Implementation

Primary files:

- `src/automaton-studio-vue/src/shared/services/api/axios-instance.ts`
- `src/automaton-studio-vue/src/features/auth/services/auth.service.ts`
- `src/automaton-studio-vue/src/features/auth/stores/auth.store.ts`
- `src/automaton-studio-vue/src/features/auth/views/GitHubCallbackView.vue`

Current behavior:

- Studio keeps the access token in memory only
- Studio does not persist refresh tokens in `localStorage`
- on app start, Studio attempts session restore by calling the refresh endpoint with `withCredentials`
- if the refresh cookie is valid, the API returns a fresh access token
- logout clears in-memory auth state and calls the API logout endpoint

## Security Properties

What this improves:

- no refresh token in `localStorage`
- no long-lived refresh secret exposed to JavaScript
- refresh-token rotation remains active
- GitHub callback no longer exposes the refresh token in frontend-visible auth state

What still matters:

- access tokens are still browser-readable while the session is active
- access token lifetime should stay short
- authenticated state-changing endpoints still need CSRF-aware cookie settings and CORS review
- XSS still matters because it can act as the user while the page is open

## Remaining Follow-Ups

- review CORS and cookie behavior for production origins
- review any remaining auth-related logs to ensure tokens are not written accidentally
- consider adding focused controller tests for cookie-based refresh/logout behavior
