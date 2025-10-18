# AGENTS.md

This document provides a comprehensive summary of the Automaton project, intended to guide developers and agents working on this codebase.

## Project Overview

Automaton is a lightweight, cross-platform automation platform designed to streamline and orchestrate software tasks. It provides a visual interface for creating and managing task flows, while supporting extensibility through Python scripting and modular logic components.

### Site Map and Key Features

This section provides a sitemap of the Automaton Studio application and a map of its key features.

#### Sitemap

*   `/`: Dashboard / Home
*   `/flows`: Flows
    *   `/flow/designer/{flowid}/{flowname}`: Flow Designer
    *   `/flow/logs/{flowid}/{flowname}`: Flow Logs
    *   `/flow/activity/{flowid}/{flowname}`: Flow Activity
    *   `/flow/schedule/{flowid}/{flowname}`: Flow Schedule
    *   `/flow/settings/{flowid}/{flowname}`: Flow Settings
*   `/runners`: Runners
*   `/customsteps`: Custom Steps
    *   `/stepdesigner/{stepId}`: Custom Step Designer
*   `/apikeys`: API Keys
*   `/downloads-hub`: Downloads Hub
    *   `/downloads`: Runner Downloads
    *   `/desktop`: Desktop App Download
*   `/settings`: Settings
*   `/onboarding`: Onboarding
*   `/account`: Account
*   `/notavailable`: Page Not Available
*   `/404`: Not Found
*   Logout (Action)

#### Key Features

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
    *   A hub for downloading different versions of the runner and the desktop application.
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

- **`Automaton.Studio`**: The main application for designing and managing automation flows. It has several versions:
    - **`Automaton.Studio.Web`**: The web-based version of the studio.
    - **`Automaton.Studio.Desktop`**: A desktop version of the studio built with WPF and Blazor.
    - **`Automaton.Studio.Server`**: The backend server for the studio, handling business logic and data persistence.
- **`Automaton.Runner`**: Executes the automation flows. It comes in two flavors:
    - **`Automaton.Runner.Console`**: A cross-platform command-line runner.
    - **`Automaton.Runner`**: A GUI version of the runner for Windows.
- **`Automaton.Core`**: A shared library containing the core logic for workflows, steps, and data models.
- **`Automaton.Steps`**: A library of built-in, reusable steps for use in automation flows.
- **`UnitTests`**: Contains the unit tests for the project.

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
For production deployments, the runner can be configured as a system service. See the comprehensive guide at `docs/runner-as-service.md` which provides manual setup instructions for:
- **Windows**: Using NSSM (Non-Sucking Service Manager)
- **Linux**: Using systemd
- **macOS**: Using launchd

**Note**: The runner does NOT include built-in service installation commands. Service configuration uses platform-native tools for better transparency and control.

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

### Automaton Studio (Desktop)
- **Mixed Blazor and WPF controls**: It's possible to mix Blazor and WPF controls. In `MainWindow.xaml`, you can add WPF controls, and in `MainWindow.xaml.cs`, you can interact with the Blazor application's state.

### API Key Scopes
The console runner accepts any API key starting with `ak_` and at least 10 characters long. The specific scope and permissions are determined by your Automaton Studio server configuration.
The server configuration may use different API key formats depending on the use case:

| Example Format | Description | Use Case |
|----------------|-------------|----------|
| **ak_runner_xxx** | Traditional runner keys | Console runners, automation agents |
| **ak_execute_xxx** | Traditional execution keys | CI/CD pipelines, external integrations |
| **ak_readonly_xxx** | Traditional read-only keys | Monitoring, reporting tools |
| **ak_xxxxxxxxx** | Flexible format keys | Any scope (determined by server configuration) |
