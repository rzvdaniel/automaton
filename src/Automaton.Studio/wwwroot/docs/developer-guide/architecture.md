# Architecture Overview

This document provides a comprehensive overview of the Automaton platform architecture, designed to help developers understand how the system works and how to extend it.

## High-Level Architecture

Automaton follows a distributed architecture pattern with clear separation between the management interface, execution engine, and storage layer.

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Studio Web    │    │  Studio Server  │    │    Database     │
│   (Frontend)    │◄──►│   (Backend)     │◄──►│    (MySQL)      │
│                 │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                              │
                              ▼
                    ┌─────────────────┐
                    │    Runners      │
                    │  (Distributed)  │
                    │                 │
                    └─────────────────┘
```

## Core Components

### 1. Automaton Studio
The management interface for designing and monitoring automation flows.

- **Studio Web**: Web-based interface using ASP.NET Core with Blazor Server
- **Studio Desktop**: Native Windows application with WPF and Blazor
- **Studio Server**: Backend API services using ASP.NET Core Web API

### 2. Automaton Core
Shared library containing fundamental abstractions and models for flows, steps, and execution context.

### 3. Automaton Steps  
Built-in step library providing common automation actions like HTTP requests, file operations, and logic steps.

### 4. Automaton Runners
Distributed execution agents that run automation flows across different platforms.

## Technology Stack

- **Frontend**: Blazor Server, ASP.NET Core, AntDesign UI components
- **Backend**: ASP.NET Core Web API, Entity Framework Core
- **Database**: MySQL with connection pooling and migrations
- **Communication**: SignalR for real-time updates between components
- **Runtime**: .NET 8 for modern, performant execution

## Security & Scalability

- **Authentication**: JWT tokens and API key-based authentication
- **Communication**: HTTPS/TLS encryption for all data transfer
- **Scaling**: Horizontal scaling through multiple distributed runners
- **Monitoring**: Comprehensive logging and health checks

## Next Steps

- Learn about [Development Setup](development-setup.md) to start contributing
- Explore [Project Structure](project-structure.md) for detailed code organization  
- Check [Building from Source](building.md) for development workflow