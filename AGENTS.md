# AGENTS.md

This document provides a comprehensive summary of the Automaton project, intended to guide developers and agents working on this codebase.

## Project Overview

Automaton is a .NET-based automation platform that combines a visual workflow designer, a secure execution runner, and an integrated web app builder. It is implemented as a Blazor web front-end (`Automaton.Studio.Web`), a backend server (`Automaton.Studio.Server`), shared core libraries (`Automaton.Core`), and a cross-platform console runner (`Automaton.Runner.Console`) that executes flows.

Primary goals:
- Offer an integrated Web Apps platform so users can build frontends (HTML/CSS/JS) that call Flows as their backend.
- Enable users to author, test, and run automation Flows composed of built-in and custom Steps.
- Provide a secure, scalable execution surface (Runners) for flows with API-driven integration points.

Core capabilities:
- Web Apps: author, preview, and publish frontend apps inside the Studio and execute flows from those apps via a secure client helper (`window.automatonApi`).
- Visual Flow Designer with drag-and-drop step placement, variable mapping, triggers, and execution controls.
- Custom Steps authoring (Python, PowerShell, Batch) with an integrated Step Designer including code editor, test variables, and logs.
- Cross-platform Runner (`Automaton.Runner.Console`) for executing flows; installers and service scripts are provided for production deployment.

This README is based on the actual implementation found in the repository (`src/Automaton.Studio/**`, `src/Automaton.Runner.Console`, `src/Automaton.Core`, `src/Automaton.Steps`, and `examples/`).

### Site Map and Key Features

This section provides a sitemap of the Automaton Studio application and a map of its key features.

#### Sitemap

-   `/`: Dashboard / Home
-   `/flows`: Flows
    -   `/flow/designer/{flowid}/{flowname}`: Flow Designer
    -   `/flow/logs/{flowid}/{flowname}`: Flow Logs
    -   `/flow/activity/{flowid}/{flowname}`: Flow Activity
    -   `/flow/schedule/{flowid}/{flowname}`: Flow Schedule
    -   `/flow/settings/{flowid}/{flowname}`: Flow Settings
-   `/runners`: Runners
-   `/apps`: Apps
    -   `/appdesigner/{appId}`: App Designer
-   `/customsteps`: Custom Steps
    -   `/stepdesigner/{stepId}`: Custom Step Designer
-   `/apikeys`: API Keys
-   `/ai-keys`: AI API Keys
-   `/downloads`: Downloads Hub (runner downloads & service scripts)
-   `/settings`: Settings
-   `/onboarding`: Onboarding
-   `/account`: Account
-   `/notavailable`: Page Not Available
-   `/404`: Not Found
-   Logout (Action)

#### Key Features

*   **Apps (Web App Platform):**
    *   Author, preview, publish, and manage small web apps (HTML/CSS/JS) that call Flows as their backend. Includes an App Designer with Monaco editor, preview iframe, PWA options, and AI-assisted code helpers.
*   **Dashboard:** The main landing page.
*   **Flows Management:**
    *   List, create, and manage automation flows.
    *   **Flow Designer:** A visual editor to design and build flows.
    *   **Flow Logs:** View logs for a specific flow.
    *   **Flow Activity:** View the activity and history of a flow.
    *   **Flow Schedule:** Schedule flows to run at specific times.
    *   **Flow Settings:** Configure settings for a specific flow.
*   **Runners Management:**
    *   List and manage connected runners.
*   **Custom Steps Management:**
    *   List, create, and manage custom steps.
    *   **Custom Step Designer:** A designer to create new custom steps.
*   **API Keys Management:**
    *   Create, manage, and revoke API keys for authentication.
*   **Downloads:**
    *   A hub for downloading different versions of the runner and service setup scripts.
*   **Settings:**
    *   Configure application settings.
*   **Onboarding:**
    *   A "Get Started" guide for new users.
*   **User Account Management:**
    *   Manage user account details.
*   **Authentication:**
    *   Login/Logout functionality.

## Project Structure

The Automaton project is a .NET solution composed of several key components:

 - **`Automaton.Studio`**: Shared UI components, pages, and modules used by the Studio front-end. It contains the primary UI surface and is the umbrella project for Studio functionality. The concrete web implementation is provided in:
    - **`Automaton.Studio.Web`**: The web-based implementation of the Studio (main web UI).
    - **`Automaton.Studio.Server`**: The backend server for the studio, handling business logic and data persistence.
- **`Automaton.Runner.Console`**: A cross-platform command-line runner.
- **`Automaton.Core`**: A shared library containing the core logic for workflows, steps, and data models.
- **`Automaton.Steps`**: A library of built-in, reusable steps for use in automation flows.
- **`UnitTests`**: Contains the unit tests for the project.
 - **`Automaton.Core`**: A shared library containing the core logic for workflows, steps, and data models.
 - **`Automaton.Core.Scripting`**: Scripting helpers and runtime integrations used by custom steps and the Step Designer.
 - **`Automaton.Mcp.Client`**: Client libraries for the MCP (model-context-protocol) integrations and inter-process communication used across Studio and Runner components.
 - **`Automaton.Steps`**: A library of built-in, reusable steps for use in automation flows.
 - **UI modules**: lightweight UI/feature modules used by the Studio front-end and account flows:
     - `Automaton.App.Account`
     - `Automaton.App.Authentication`
     - `Automaton.App.Install`
     - `Automaton.Client.Auth`
 - **`UnitTests`**: Contains the unit tests for the project. Key test projects include:
     - `Automaton.Core.Tests`
     - `Automaton.Runner.Console.Tests`
     - `Automaton.Studio.Server.Tests`
     - `Automaton.Studio.Tests`

## Development Environment Setup

### Prerequisites
- .NET 8.0 SDK
- An IDE such as Visual Studio or JetBrains Rider.

### Entity Framework CLI
The `dotnet ef` tool is required for managing database migrations. Install it as a global tool:
```bash
dotnet tool install --global dotnet-ef
```

## Building and Running the Project

### Automaton Runner Console

The console runner is a key component for executing flows.

#### Building from Source
```bash
cd src/Automaton.Runner.Console
dotnet build -c Release
dotnet publish -c Release --self-contained false
```

#### Authentication
The console runner uses API Key authentication. You can get an API key from the Automaton Studio web interface under **Settings > API Keys**.

#### Running the Runner
The easiest way to get started is with the interactive setup:
```bash
automaton-runner
```
The runner will guide you through the process of setting up authentication and registering the runner.

For automated deployments, you can set the API key as an environment variable:
```bash
export AUTOMATON_API_KEY="your_api_key"
automaton-runner
```

#### Running as a Service
For production deployments, the runner can be configured as a system service. We provide:

1. **Automated Setup Scripts** (Recommended):
   - `scripts/setup-service.ps1` for Windows
   - `scripts/setup-service.sh` for Linux/macOS

2. **Manual Setup Guide**: See `docs/runner-as-service.md` for detailed instructions:
   - **Windows**: Using NSSM (Non-Sucking Service Manager)
   - **Linux**: Using systemd
   - **macOS**: Using launchd

**Note**: The runner does NOT include built-in service installation commands. Service configuration uses platform-native tools (NSSM, systemd, launchd) for better transparency and control. The setup scripts automate the manual process documented in the guide.

### Automaton Studio Server
The server is the backend for the studio. The Swagger API documentation can be found at:
- `https://localhost:7091/swagger/v1/swagger.json`
- `https://localhost:7091/swagger/index.html`

## Important Developer Notes

### Automaton Studio (Web)
- **Bottom menu item in NavMenu**: To place a menu item at the bottom of the navigation menu, use the following style:
  ```html
  <MenuItem Key="4" Icon="poweroff" RouterMatch="NavLinkMatch.Prefix" Class="logout" Style="position: absolute; bottom: 0; z-index: 1; transition: all 0.2s;">Logout</MenuItem>
  ```
- **Get authentication state and user information**:
  ```csharp
  var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
  var user = authState.User;
  var claims = user.Claims?.ToList();
  ```

### API Key Scopes
The console runner accepts any API key starting with `ak_` and at least 10 characters long. The specific scope and permissions are determined by your Automaton Studio server configuration.
The server configuration may use different API key formats depending on the use case:

| Example Format | Description | Use Case |
|----------------|-------------|----------|
| **ak_runner_xxx** | Traditional runner keys | Console runners, automation agents |
| **ak_execute_xxx** | Traditional execution keys | CI/CD pipelines, external integrations |
| **ak_readonly_xxx** | Traditional read-only keys | Monitoring, reporting tools |
| **ak_xxxxxxxxx** | Flexible format keys | Any scope (determined by server configuration) |

## Apps (Web App Platform)

The Studio now includes an integrated Web Apps feature that lets users create, preview, and publish frontend applications (HTML/CSS/JavaScript) and connect them to backend workflows (Flows). Each App can act as a full web application front-end while delegating backend logic to existing Automaton Flows which can be executed via the studio/runner API.

Key ideas and behavior:
- **Frontend + Backend:** Apps are authored as standard web frontends (HTML/CSS/JS) inside the Studio and can call server-side Flows (workflows) as their backend. Workflows can be executed directly (for preview) or exposed/consumed through RESTful endpoints by the server (typical pattern: app -> REST API -> Flow execution).
- **Pages & Files:** App UI is implemented under `src/Automaton.Studio/Pages/Apps/`.
    - `AppsPage.razor` — lists apps, create/publish/unpublish, view public URL, quick actions.
    - `AppDesignerPage.razor` — full app designer: Monaco code editor, Preview (iframe via `srcdoc`), Tabs for Code/Preview/Settings, PWA settings, Runner selection for preview/execution, and Publish flow.
- **Publish / Access:** Apps can be Draft or Published. Published apps have a `PublicUrl` (`/web/{publicUrl}`) and can be marked Public (no login) or Protected (requires Automaton login). The designer supports Publish/Unpublish flows and shows published status badges.
- **Preview & Execution:** The designer provides an iframe preview using `srcdoc` with sandbox attributes (`allow-scripts allow-same-origin allow-modals allow-forms`) and a Runner selector controlling which runner is used for executions triggered from the app (also used for preview execution).
- **PWA support:** The App Designer exposes PWA settings (Enable PWA, PWA Name/ShortName, Theme/Background color) so apps can be packaged as progressive web apps.
- **AI-assisted editing:** The App Designer includes an AI Chat panel (`WebAiChatPanel`) that can generate or modify frontend code using configured AI API keys.
- **Security considerations:** Public apps (no authentication) are accessible to anyone with the link; protected apps rely on the Studio authentication. App-backend calls should use scoped API keys (see API Keys and WebApp scope) and follow the same rate/usage monitoring policies used elsewhere in Studio.

Notes for maintainers / reviewers:
- The App Designer stores the app definition including HTML/JS/CSS and a `Definition.Settings` object (PWA flags, theme colors, etc.).
- Preview uses `srcdoc` so any references to remote assets should be considered for same-origin and sandbox implications.
- When documenting or extending the RESTful API for flows consumed by apps, ensure there are clear examples showing how to call flows from frontends (authentication header with WebApp-scoped API keys, CORS policy, and any rate limiting).

Example: calling a Flow REST endpoint from a published web app

When a published app needs to execute a Flow, it should call the Studio/Server REST endpoint that executes flows. For security, published apps should use a `WebApp`-scoped API key (limited permissions) or call a short-lived token endpoint rather than embedding a full privileged key in client-side code. Also ensure the server allows CORS for the published app origin.

JavaScript (browser) example using the Studio-provided helper `window.automatonApi`:

The Studio exposes a small client API at `src/Automaton.Studio.Web/wwwroot/automatonApi.js` which registers a global `window.automatonApi` object and helpers:

```javascript
window.automatonApi = {
  executeFlow: executeFlow,
  customCall: customCall,
  getAppSlug: getAppSlug
};
```

Important: Published web apps should call `window.automatonApi.executeFlow(...)` rather than calling the server directly with `fetch`. The helper injects the authenticated JWT (so the server can identify and authorize the caller) and handles any app-scoped behavior (CORS, tokens) for you.

Basic usage (example):

```javascript
const flowId = 'your-flow-id';

const payload = {
  input: { name: 'Alice', email: 'alice@example.com' },
  metadata: { source: 'webapp', appId: await window.automatonApi.getAppSlug() }
};

// executeFlow(flowId, payload) -> Promise
window.automatonApi.executeFlow(flowId, payload)
  .then(result => console.log('Execution result', result))
  .catch(err => console.error('Execution error', err));
```

If you need to call other Studio endpoints, use `automatonApi.customCall(method, path, body)` which also attaches the authentication token:

```javascript
// customCall(method, path, body)
window.automatonApi.customCall('POST', `/api/some/endpoint`, { foo: 'bar' })
  .then(r => console.log(r))
  .catch(console.error);
```

Helper: `automatonApi.getAppSlug()` returns the app's public slug (useful when building metadata or dynamic URLs).

There is a full runnable example in the repo demonstrating usage and a real-world flow call: `examples/nytimes-headlines.html` (fetches NYTimes headlines and calls the Studio flow via `automatonApi`).

Example request/response shapes (what the server expects/returns):

Request body (JSON) — matches `WebAppFlowExecutionRequest`:

```json
{
    "inputs": {
        "name": "Alice",
        "email": "alice@example.com"
    },
    "runnerNames": ["runner-1", "runner-2"]
}
```

Example response (server) — matches `WebAppFlowExecutionResponse`:

```json
{
    "success": true,
    "flowId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "status": "Completed",
    "runnerCount": 2,
    "executedAt": "2025-11-09T12:34:56Z",
    "message": "Execution finished successfully",
    "outputVariables": {
        "messageId": "msg_123",
        "summary": "Processed 10 items"
    },
    "logs": [
        "Started execution on runner-1",
        "Step: FetchData - OK",
        "Step: ProcessItems - OK",
        "Execution completed"
    ]
}
```

Security notes:
- Do not embed full-access API keys in client-side code. Use `WebApp`-scoped keys, server-side proxies, or short-lived tokens issued by the Studio for published apps.
- Ensure CORS is configured on the server for the published app origin, or route requests through the Studio server so credentials are not exposed to the public.
- Limit the scope and lifetime of keys used by apps, and monitor usage via the API Keys page.


## Pages summary (UI pages)

Below are concise notes about the main Studio pages and what they do in the UI.

- **Flows & Flow Designer** (`src/Automaton.Studio/Pages/Flows/`, `src/Automaton.Studio/Pages/FlowDesigner/`)
    - Pages: flow list UIs under `Pages/Flows/` and the visual designer under `Pages/FlowDesigner/`.
    - Purpose: Create, design, and manage automation flows (workflows). The Flow Designer is a visual canvas where users add nodes/steps, connect data paths, configure triggers, map variables, and wire custom steps or built-in steps together.
    - Key behaviors: drag-and-drop step placement, property panels for step configuration, test/run controls, save/publish actions, and access to execution logs. Flows can be executed by runners or via RESTful execute endpoints.

- **Runners** (`src/Automaton.Studio/Pages/Runners/`)
    - Purpose: List and manage connected runners (agents that execute flows). UI supports registration status, last-seen timestamps, runner metadata, and actions like disable/unregister.
    - Notes: Runners authenticate with API keys; the Downloads page and setup guides reference runner installation and service setup scripts.

- **Apps** (`src/Automaton.Studio/Pages/Apps/`)
    - Purpose: Create, preview, publish, and manage small web apps (HTML/CSS/JS) that can call flows as their backend. See the `Apps` section above for details.

- **Custom Steps & Step Designer** (`src/Automaton.Studio/Pages/CustomSteps/`, `src/Automaton.Studio/Pages/StepDesigner/`)
    - Purpose: Author reusable steps (Python, PowerShell, Batch) and edit them with the Step Designer. Steps include input/output variable definitions, inline testing, logs, and AI-assisted code generation.
    - Key files: `CustomStepsPage.razor`, `StepPage.razor`, `StepViewModel.cs`, `InputVariableDialog.razor`.

- **API Keys** (`src/Automaton.Studio/Pages/ApiKeys/`)
    - Purpose: Generate and manage API keys for runners, web apps, and integrations. Keys have configurable scopes and optional expirations; newly-created keys are shown once for copying.

- **AI API Keys** (`src/Automaton.Studio/Pages/AiApiKeys/`)
    - Purpose: Store and manage API keys for AI providers (OpenAI, Anthropic, Google, custom). These keys power the various AI panels in the Studio (app/step designers, chat panels).

- **Downloads** (`src/Automaton.Studio/Pages/Downloads/`)
    - Purpose: Provide runner download artifacts, install snippets for multiple OSes, and service setup scripts (NSSM, systemd, launchd). Also includes quick-install commands and manual download links.

- **Settings** (`src/Automaton.Studio/Pages/Settings/`)
    - Purpose: Application and tenant-level configuration—server URLs, CORS, default behaviors, integrations, and other global Studio settings. This is where administrators configure behavior that affects flows, runners, and apps.

- **Account** (`Automaton.App.Account/Account` + `src/Automaton.Studio/Pages/Account`)
    - Purpose: User account management (profile, password, MFA, tokens). The Studio reuses the `Automaton.App.Account` area for account-related UI and settings.

- **Onboarding** (`src/Automaton.Studio/Pages/Onboarding/`)
    - Purpose: Guided first-run experience: get-started checklist, create first flow, register a runner, and add API/AI keys.

- **Docs / Help** (`src/Automaton.Studio/Pages/DocsPage.razor` and `wwwroot/docs`) 
    - Purpose: Built-in documentation viewer (docsify) and help pages. The `wwwroot/docs` folder contains the docs site content referenced by the UI.


## Documentation location

- The Studio includes a docsify-based documentation site under the web project: `src/Automaton.Studio.Web/wwwroot/docs`. Use that folder for user guide pages; Studio links in the UI open sections under `/docs/#/...`.
