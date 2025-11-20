# Automaton

Automaton is a lightweight, cross-platform automation platform designed to streamline and orchestrate software tasks. It provides a visual interface for creating and managing task flows, while supporting extensibility through Python scripting and modular logic components.

## 🚧 Project Status
As of July 11, 2025
Automaton is currently in early development, approaching its first alpha release. A demo website will soon be available to showcase the core features and interface.

## 🔧 Key Features (Planned & In Progress)

🧩 Flow Builder – Design automation pipelines using a clean, intuitive interface

🐍 Python Integration – Create and embed custom steps using Python code

🌐 Remote Execution – Run flows on any connected system with a compatible runner

🔁 Logic Blocks – Use conditionals, loops, and HTTP request steps (with more being added)

📦 Self-Hosted – Deploy on any web server supporting .NET 7 and Blazor

💾 Persistent Storage – Built-in support for MySQL-backed state and flow management

## 🚀 Getting Started

### Development Environment Setup

For new developers joining the team, we provide a Docker-based MySQL setup for easy onboarding:

1. **Clone the repository**
   ```bash
   git clone https://github.com/rzvdaniel/automaton.git
   cd automaton
   ```

2. **Set up the database**
   ```bash
   # Copy environment variables
   cp .env.example .env
   
   # Start MySQL container
   docker compose -f docker-compose.dev.yml up -d mysql
   ```

3. **Configure connection string**
   ```bash
   cd src/Automaton.Studio.Server
   dotnet user-secrets set "ConnectionStrings:MySqlConnection" "Server=localhost;Port=3306;Database=automaton;User=automaton_user;Password=AutomatonPass123!;"
   ```

4. **Start the server** (migrations apply automatically on first run)
   ```bash
   dotnet run
   ```

For detailed instructions, see the [Docker Setup Guide](docker/README.md).

## 📸 Screenshots

![Screenshot 01](/screenshots/Screenshot-2024-01-22-231011.png?raw=true "Studio Flow")

## 📜 License

Licensed under the Apache License 2.0.
