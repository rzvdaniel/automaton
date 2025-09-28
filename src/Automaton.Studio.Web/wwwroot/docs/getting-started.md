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
1. Go to **Downloads Hub** in the platform  
2. Navigate to **Downloads** section for runners
3. Download the appropriate version for your platform (Windows, macOS, Linux)
4. Follow the installation instructions provided on the download page

### 3. Setting Up Authentication for Runners

Runners need an API key to connect:
1. Go to **API Keys** in the platform
2. Click **Create New Key**
3. Copy the generated key (starts with `ak_`)
4. Use this key when configuring your runner
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