<div class="doc-hero hero-getting-started">
  <div class="doc-hero-content">
    <div class="doc-hero-icon">🎯</div>
    <h1>Quick Start Guide</h1>
    <p class="hero-subtitle">
      Get up and running with Automaton in minutes. This guide walks you through your first automation workflow, from setup to execution.
    </p>
    <div class="doc-hero-stats">
      <div class="doc-hero-stat">
        <span class="doc-hero-stat-number">5</span>
        <span class="doc-hero-stat-label">Minutes to First Flow</span>
      </div>
      <div class="doc-hero-stat">
        <span class="doc-hero-stat-number">✓</span>
        <span class="doc-hero-stat-label">Step-by-Step</span>
      </div>
      <div class="doc-hero-stat">
        <span class="doc-hero-stat-number">🎓</span>
        <span class="doc-hero-stat-label">Beginner Friendly</span>
      </div>
    </div>
  </div>
</div>

# Quick Start Guide

Welcome to Automaton! This guide will help you get started with the automation platform quickly.

## What is Automaton?

Automaton is a lightweight, cross-platform automation platform that helps you streamline and orchestrate software tasks. With its visual flow builder and extensible architecture, you can create powerful automation workflows without deep programming knowledge.

## Quick Start Steps

- [ ] **Register/Login** - Create your account or sign in
- [ ] **Install Automaton Desktop** (Optional) - For a native desktop experience  
- [ ] **Install a Runner** - Set up an execution agent
- [ ] **Create your first Flow** - Build a simple automation
- [ ] **Execute and Monitor** - Run your automation and view results

## Getting Started

### 1. Register or Login

Visit the Automaton platform and either:
- **Register**: Create a new account with your email
- **Login**: Sign in with your existing credentials

### 2. Install Applications (Optional)

#### Automaton Desktop
For a native desktop experience on Windows:
1. Go to **Downloads Hub** in the platform
2. Navigate to **Desktop** section
3. Download and install **Automaton Studio Desktop**
4. Launch and login with your credentials

#### Automaton Runner
Runners execute your automation flows. To set up your first runner:

**🐳 Docker (Recommended):**
```bash
docker run -d \
  --name automaton-runner \
  -e AUTOMATON_API_KEY=ak_your_api_key_here \
  rzvdaniel/automaton-runner:latest
```

The Docker image includes Python 3.13 and common packages pre-installed.

**📦 Native Executable:**
1. Go to **Settings → API Keys** and create a new API key with "Runner" scope
2. Download the runner from **Downloads Hub → Console Runner**
3. Run the one-command installer (Windows PowerShell or Linux/macOS Terminal)
4. Run `automaton-runner` and follow the interactive setup

**👉 For detailed instructions, see the [Runner Setup Guide](user-guide/runner-setup.md)**

This includes:
- Docker deployment (Docker CLI, Docker Desktop, Docker Compose)
- Complete installation instructions for all platforms
- CI/CD integration examples
- Troubleshooting common issues
- System service configuration
### 4. Creating Your First Flow

Let's create a simple "Hello World" automation:

#### Navigate to Flows
- Click **Flows** in the left navigation menu
- Click **Create New Flow**

#### Design Your Flow
- Drag a **Display Message** step onto the canvas
- Configure the message text: "Hello, World!"
- Connect the Start node to your Display Message step

#### Configure Execution
- Click **Settings** in the flow designer
- Select your runner from the dropdown
- Save your flow

#### Execute Your Flow
- Click **Run Flow** in the designer
- Monitor execution in the **Logs** tab
- View the result in the **Activity** tab

## Understanding the Platform

### Key Concepts

**Flows** are your automation workflows - visual representations of tasks executed in sequence or parallel.

**Steps** are individual actions within a flow:
- Display messages
- HTTP requests  
- File operations
- Python scripts
- Conditional logic

**Runners** are execution agents that run your flows on different machines.

**Variables** store data that flows between steps, making automations dynamic and reusable.

### Platform Pages

- **Dashboard**: Overview of your flows and runners
- **Flows**: Create and manage automation workflows
- **Runners**: View and manage connected execution agents  
- **Custom Steps**: Build your own reusable automation components
- **API Keys**: Manage authentication keys for runners and integrations
- **Downloads Hub**: Download Automaton Desktop and Runner applications
- **Settings**: Configure your account and preferences

## Next Steps

Now that you have Automaton running:
- Explore the **Flow Designer** to create more complex automations
- Visit **Downloads Hub** to set up additional runners
- Learn about the [Platform Pages](user-guide/platform-pages.md) for detailed interface guidance

Welcome to automated workflows with Automaton! 🚀