# Use the official .NET 8 runtime image
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

# Use the SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["Automaton.Runner.Console/Automaton.Runner.Console.csproj", "Automaton.Runner.Console/"]
COPY ["Automaton.Core/Automaton.Core.csproj", "Automaton.Core/"]
COPY ["Automaton.Steps/Automaton.Steps.csproj", "Automaton.Steps/"]
COPY ["Automaton.Core.Scripting/Automaton.Core.Scripting.csproj", "Automaton.Core.Scripting/"]

# Restore dependencies
RUN dotnet restore "Automaton.Runner.Console/Automaton.Runner.Console.csproj"

# Copy source code
COPY . .

# Build the application
WORKDIR "/src/Automaton.Runner.Console"
RUN dotnet build "Automaton.Runner.Console.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "Automaton.Runner.Console.csproj" -c Release -o /app/publish --no-restore

# Final stage
FROM base AS final
WORKDIR /app

# Copy the published application
COPY --from=publish /app/publish .

# Create logs directory
RUN mkdir -p /app/logs

# Create non-root user for security
RUN adduser --disabled-password --gecos '' automaton

# Change ownership of app directory
RUN chown -R automaton:automaton /app

# Switch to non-root user
USER automaton

# Set environment variables
ENV DOTNET_ENVIRONMENT=Production
ENV AppConfig__BaseUrl=https://localhost:7064

# Expose the application (if needed for health checks)
EXPOSE 8080

# Set the entry point
ENTRYPOINT ["dotnet", "automaton-runner.dll"]

# Default command shows help
CMD ["--help"]