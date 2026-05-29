# GitHub OAuth Local Testing With Cloudflare Tunnel

This document explains how to test Automaton Studio GitHub OAuth locally by exposing the local Studio frontend and local API through Cloudflare Tunnel.

## Goal

Expose these local services:

- `https://studio-dev.automaton.monster` -> `http://localhost:5173`
- `https://api-dev.automaton.monster` -> `https://localhost:7091`

Then configure the GitHub OAuth app to use the public tunnel URLs instead of `localhost`.

## Prerequisites

- Cloudflare account with access to the `automaton.monster` zone
- `cloudflared` installed on Windows
- Local Studio frontend running on port `5173`
- Local ASP.NET API running on port `7091`
- GitHub OAuth app credentials

## 1. Install cloudflared

```powershell
winget install --id Cloudflare.cloudflared
```

Verify:

```powershell
cloudflared --version
```

Official docs:

- https://developers.cloudflare.com/tunnel/downloads/

## 2. Log in to Cloudflare

```powershell
cloudflared tunnel login
```

This opens a browser window. Select the Cloudflare account and the `automaton.monster` zone.

Cloudflare stores credentials under:

```text
C:\Users\Razvan\.cloudflared
```

## 3. Create a named tunnel

```powershell
cloudflared tunnel create automaton-local
```

This command prints a tunnel UUID and creates a credentials file:

```text
C:\Users\Razvan\.cloudflared\<TUNNEL-UUID>.json
```

## 4. Create the Cloudflare Tunnel config

Create:

[config.yml](</C:/Users/Razvan/.cloudflared/config.yml>)

Use this content:

```yaml
tunnel: automaton-local
credentials-file: C:\Users\Razvan\.cloudflared\<TUNNEL-UUID>.json

ingress:
  - hostname: studio-dev.automaton.monster
    service: http://localhost:5173

  - hostname: api-dev.automaton.monster
    service: https://localhost:7091
    originRequest:
      noTLSVerify: true

  - service: http_status:404
```

Replace `<TUNNEL-UUID>` with the real filename created by `cloudflared tunnel create`.

`noTLSVerify: true` is needed for the local ASP.NET dev certificate.

If the local API is running on HTTP instead of HTTPS, change the API service to:

```yaml
service: http://localhost:7091
```

## 5. Create DNS routes

```powershell
cloudflared tunnel route dns automaton-local studio-dev.automaton.monster
cloudflared tunnel route dns automaton-local api-dev.automaton.monster
```

This creates the Cloudflare DNS records pointing both hostnames at the same tunnel.

## 6. Validate the tunnel config

```powershell
cloudflared tunnel ingress validate
cloudflared tunnel ingress rule https://studio-dev.automaton.monster
cloudflared tunnel ingress rule https://api-dev.automaton.monster
```

Expected mapping:

- `studio-dev.automaton.monster` -> `http://localhost:5173`
- `api-dev.automaton.monster` -> `https://localhost:7091`

## 7. Run the tunnel

```powershell
cloudflared tunnel run automaton-local
```

Leave this terminal window open while testing.

## 8. Run the local frontend and backend

Frontend:

```powershell
cd C:\Work\automaton\src\automaton-studio-vue
npm run dev
```

Backend:

```powershell
cd C:\Work\automaton\src\Automaton.Studio.Server
dotnet run
```

## 9. Configure local Automaton settings

Update:

[appsettings.Development.json](/C:/Work/automaton/src/Automaton.Studio.Server/appsettings.Development.json)

Set:

```json
"AppConfig": {
  "WebsiteUrl": "http://localhost:5173"
},
"GitHub": {
  "ClientId": "YOUR_GITHUB_CLIENT_ID",
  "ClientSecret": "YOUR_GITHUB_CLIENT_SECRET",
  "ApiBaseUrl": "https://api-dev.automaton.monster",
  "FrontendUrl": "https://studio-dev.automaton.monster"
}
```

Keep `AppConfig:WebsiteUrl` on its normal local value so other local-development links keep working.

`GitHub:ApiBaseUrl` forces the public API origin used for the OAuth `redirect_uri` sent to GitHub.

`GitHub:FrontendUrl` is used only for the final OAuth redirect back into Studio after the API finishes GitHub authentication.

## GitHub config reference

Backend GitHub OAuth uses these settings:

- `GitHub:ClientId`
  - GitHub OAuth app client ID
- `GitHub:ClientSecret`
  - GitHub OAuth app client secret
- `GitHub:ApiBaseUrl`
  - public API origin used when building the OAuth callback URL sent to GitHub
  - example: `https://api-dev.automaton.monster`
  - callback path is still `/signin-github`
- `GitHub:FrontendUrl`
  - frontend origin used for the final redirect back into Studio after the API finishes GitHub authentication
  - local example: `https://studio-dev.automaton.monster`
  - production example: `https://studio.automaton.monster`

Typical local tunnel split:

```json
"AppConfig": {
  "WebsiteUrl": "http://localhost:5173"
},
"GitHub": {
  "ClientId": "YOUR_GITHUB_CLIENT_ID",
  "ClientSecret": "YOUR_GITHUB_CLIENT_SECRET",
  "ApiBaseUrl": "https://api-dev.automaton.monster",
  "FrontendUrl": "https://studio-dev.automaton.monster"
}
```

Typical production split:

```json
"GitHub": {
  "ClientId": "YOUR_GITHUB_CLIENT_ID",
  "ClientSecret": "YOUR_GITHUB_CLIENT_SECRET",
  "ApiBaseUrl": "https://api.automaton.monster",
  "FrontendUrl": "https://studio.automaton.monster"
}
```

## 10. Configure frontend API base URL

Set the Studio frontend env var to the public tunneled API origin:

```env
VITE_API_BASE_URL=https://api-dev.automaton.monster
```

Use the frontend env file that your local setup already uses.

If you want GitHub OAuth to use the tunnel but keep the rest of the frontend on the normal local API URL, use a separate env var:

```env
VITE_API_BASE_URL=https://localhost:7091
VITE_GITHUB_AUTH_API_BASE_URL=https://api-dev.automaton.monster
```

`VITE_GITHUB_AUTH_API_BASE_URL` is used only for the `Continue with GitHub` flow.

## 11. Configure the GitHub OAuth app

In GitHub OAuth App settings, use:

- Homepage URL: `https://studio-dev.automaton.monster`
- Authorization callback URL: `https://api-dev.automaton.monster/signin-github`

Important:

- The GitHub callback URL is the API tunnel URL, not the frontend URL
- The callback is `/signin-github`, because that is the ASP.NET OAuth middleware callback path
- Do not use `localhost` in the GitHub OAuth app config

## 12. Test the flow

Open either:

- `http://localhost:5173/login`
- or `https://studio-dev.automaton.monster/login`

Click:

- `Continue with GitHub`

Expected flow:

1. Browser opens Studio on localhost or the Studio tunnel
2. Studio redirects to `https://api-dev.automaton.monster/auth/github/login`
3. GitHub prompts for approval
4. GitHub redirects back to `https://api-dev.automaton.monster/signin-github`
5. API completes OAuth and redirects to `GitHub:FrontendUrl/auth/github/callback`
6. Studio stores the Automaton JWT and refresh token
7. User lands in Studio authenticated

## Production reference

Production hostnames in this repo are split by role:

- Website: `https://automaton.monster`
- Studio: `https://studio.automaton.monster`
- API: `https://api.automaton.monster`

For production GitHub OAuth:

- Homepage URL: `https://studio.automaton.monster`
- Authorization callback URL: `https://api.automaton.monster/signin-github`

Recommended production config:

```json
"GitHub": {
  "ClientId": "YOUR_GITHUB_CLIENT_ID",
  "ClientSecret": "YOUR_GITHUB_CLIENT_SECRET",
  "FrontendUrl": "https://studio.automaton.monster"
}
```

## Troubleshooting

- If GitHub says the callback URL is invalid, verify the exact callback path is `/signin-github`
- If GitHub still shows `redirect_uri=https://localhost:7091/signin-github`, verify `GitHub:ApiBaseUrl`
- If the API tunnel fails against local HTTPS, keep `originRequest.noTLSVerify: true`
- If OAuth returns to the wrong frontend URL, verify `GitHub:FrontendUrl`
- If the frontend cannot call the API after login, verify `VITE_API_BASE_URL`
- If only GitHub login should use the tunnel, verify `VITE_GITHUB_AUTH_API_BASE_URL`
- If the API does not receive the callback, verify the DNS route and active `cloudflared tunnel run`

## References

- https://developers.cloudflare.com/tunnel/downloads/
- https://developers.cloudflare.com/tunnel/advanced/local-management/create-local-tunnel/
- https://developers.cloudflare.com/tunnel/advanced/local-management/configuration-file/
