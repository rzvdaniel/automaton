# AGENTS.md

This document provides a comprehensive summary of the Automaton project, intended to guide developers and agents working on this codebase.

## Project Overview

Automaton is a lightweight, cross-platform automation platform designed to streamline and orchestrate software tasks. It provides a visual interface for creating and managing task flows, while supporting extensibility through Python scripting and modular logic components.

### Key Features
- **Flow Builder**: A visual interface for designing automation pipelines.
- **Python Integration**: Create and embed custom steps using Python code.
- **Remote Execution**: Run flows on any connected system with a compatible runner.
- **Logic Blocks**: Conditionals, loops, and HTTP request steps.
- **Self-Hosted**: Deploy on any web server supporting .NET 7 and Blazor.
- **Persistent Storage**: Built-in support for MySQL-backed state and flow management.

The project is inspired by [Workflow Core](https://github.com/danielgerlag/workflow-core).

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
