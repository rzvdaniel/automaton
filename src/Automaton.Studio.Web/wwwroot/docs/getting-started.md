# Getting Started with Automaton

Welcome to Automaton! This guide will help you get up and running with the automation platform quickly.

## What is Automaton?

Automaton is a lightweight, cross-platform automation platform that helps you streamline and orchestrate software tasks. With its visual flow builder and extensible architecture, you can create powerful automation workflows without deep programming knowledge.

## Quick Start Checklist

- [ ] **Install Automaton Studio** - Set up the main application
- [ ] **Deploy a Runner** - Set up an execution agent  
- [ ] **Create your first Flow** - Build a simple automation
- [ ] **Execute and Monitor** - Run your automation and view results

## Installation Options

### Option 1: Web-based Studio (Recommended)

The web-based studio runs in your browser and provides the complete Automaton experience:

1. **Download the latest release** from [GitHub Releases](https://github.com/rzvdaniel/automaton/releases)
2. **Extract and run** the Automaton Studio Web server
3. **Open your browser** to `https://localhost:7091`
4. **Complete the setup wizard** to configure your installation

### Option 2: Desktop Application

For a native desktop experience on Windows:

1. **Download the desktop application** from the Downloads Hub
2. **Install and launch** the Automaton Studio Desktop
3. **Follow the onboarding wizard** to get started

## Setting Up Your First Runner

Runners are agents that execute your automation flows. Here's how to set up your first one:

### Console Runner (Cross-platform)

1. **Download the runner** from the Downloads Hub or build from source:
   ```bash
   cd src/Automaton.Runner.Console
   dotnet build -c Release
   dotnet publish -c Release --self-contained false
   ```

2. **Get an API Key**:
   - Go to **Settings > API Keys** in Automaton Studio
   - Click **Create New Key**
   - Copy the generated key (starts with `ak_`)

3. **Configure and start the runner**:
   ```bash
   # Interactive setup (recommended for first-time setup)
   automaton-runner
   
   # Or set API key as environment variable
   export AUTOMATON_API_KEY="your_api_key"
   automaton-runner
   ```

4. **Verify connection**: The runner should appear in the **Runners** section of your studio

## Creating Your First Flow

Let's create a simple "Hello World" automation:

### Step 1: Navigate to Flows
- Click **Flows** in the left navigation menu
- Click **Create New Flow**

### Step 2: Design Your Flow
- Drag a **Display Message** step onto the canvas
- Configure the message text: "Hello, World!"
- Connect the Start node to your Display Message step

### Step 3: Configure Execution
- Click **Settings** in the flow designer
- Select your runner from the dropdown
- Save your flow

### Step 4: Execute
- Click **Run Flow** in the designer
- Monitor execution in the **Logs** tab
- View the result in the **Activity** tab

## Key Concepts

### Flows
**Flows** are your automation workflows - visual representations of tasks that need to be executed in sequence or parallel.

### Steps  
**Steps** are individual actions within a flow, such as:
- Display messages
- HTTP requests  
- File operations
- Python scripts
- Conditional logic

### Runners
**Runners** are execution agents that run your flows. They can be:
- Local console applications
- Remote agents on different machines
- Containerized instances

### Variables
**Variables** store data that flows between steps, making your automations dynamic and reusable.

## Next Steps

Now that you have Automaton running:

- **Explore the [User Guide](user-guide/flows.md)** for detailed feature documentation
- **Check out sample flows** in the Onboarding section
- **Read about [Custom Steps](user-guide/custom-steps.md)** to extend functionality
- **Learn about [Runner deployment](deployment/runners.md)** for production use

## Need Help?

- 📖 Browse this documentation for comprehensive guides
- 🐛 [Report issues](https://github.com/rzvdaniel/automaton/issues) on GitHub  
- 💡 [Request features](https://github.com/rzvdaniel/automaton/issues/new) to help shape Automaton's future

Welcome to the world of automated workflows with Automaton! 🚀