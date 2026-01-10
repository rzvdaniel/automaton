# Development Setup

This guide will help you set up a development environment for contributing to the Automaton project.

## Prerequisites

### Required Software
- **.NET 8.0 SDK**: [Download from Microsoft](https://dotnet.microsoft.com/download/dotnet/8.0)
- **IDE**: Visual Studio 2022, JetBrains Rider, or VS Code
- **Git**: For version control
- **MySQL**: For database development (or use Docker)

### Optional Tools  
- **Docker**: For containerized development
- **Postman**: For API testing
- **MySQL Workbench**: Database management GUI

## Getting the Source Code

Clone the repository and its submodules:
```bash
git clone https://github.com/rzvdaniel/automaton.git
cd automaton
```

## Entity Framework Setup

Install the EF Core CLI tool globally:
```bash
dotnet tool install --global dotnet-ef
```

## Database Setup

### Option 1: Local MySQL
1. Install MySQL Server 8.0+
2. Create a database named `automaton`
3. Update connection string in `appsettings.Development.json`

### Option 2: Docker MySQL
```bash
docker run --name automaton-mysql -e MYSQL_ROOT_PASSWORD=password -e MYSQL_DATABASE=automaton -p 3306:3306 -d mysql:8.0
```

## Building the Project

### Build All Projects
```bash
# From repository root
dotnet build
```

### Build Specific Projects
```bash
# Studio Web
cd src/Automaton.Studio.Web
dotnet build

# Runner (built via Docker Compose; console is for debugging only)
# Preferred: docker compose -f docker-compose.runner.yml up -d
# Debug inside container:
# docker compose -f docker-compose.runner.yml exec runner automaton-runner
```

## Running the Application

### Studio Web (Development)
```bash
cd src/Automaton.Studio.Web
dotnet run
```
Access at: `https://localhost:7091`

### Console Runner
```bash
cd src/Automaton.Runner.Console
dotnet run
```

## Development Workflow

1. **Create Feature Branch**: `git checkout -b feature/your-feature-name`
2. **Make Changes**: Implement your changes with tests
3. **Build & Test**: Ensure everything builds and tests pass
4. **Commit Changes**: Use clear, descriptive commit messages
5. **Create Pull Request**: Submit for review

## IDE Configuration

### Visual Studio 2022
- Install latest version with ASP.NET and web development workload
- Recommended extensions: Web Live Preview, GitHub integration

### VS Code
Recommended extensions:
- C# extension pack
- .NET Core Tools
- GitLens
- Thunder Client (API testing)

## Testing

### Run Unit Tests
```bash
dotnet test
```

### Run Specific Test Project
```bash
cd UnitTests
dotnet test
```

## Database Migrations

### Create Migration
```bash
cd src/Automaton.Studio.Server
dotnet ef migrations add YourMigrationName
```

### Apply Migrations
```bash
dotnet ef database update
```

## Debugging Tips

- **Studio Web**: Use browser dev tools for frontend, VS debugger for backend
- **API Testing**: Use the built-in Swagger UI at `/swagger`
- **Logging**: Check console output and log files for diagnostic information
- **Database**: Use EF Core logging to see generated SQL queries

## Contributing Guidelines

- Follow existing code style and conventions
- Write unit tests for new functionality
- Update documentation for new features
- Keep pull requests focused and atomic

## Getting Help

- Check existing GitHub issues and discussions
- Review the architecture documentation
- Look at existing code for patterns and examples

Ready to start coding? Check out the [Project Structure](project-structure.md) guide next!