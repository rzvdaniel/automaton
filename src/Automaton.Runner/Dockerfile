# Automaton Runner Dockerfile
# 
# Multi-stage build for both development and production deployments.
# Environment mode is controlled via DOTNET_ENVIRONMENT (Production or Development)
#
# Build: docker build -t automaton-runner .
# Run:   docker run -e AUTOMATON_SERVER_URL=... -e AUTOMATON_API_KEY=... automaton-runner

# =============================================================================
# Stage 1: Build the .NET application
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Copy solution and project files for restore
COPY *.sln ./
COPY Automaton.Core/*.csproj ./Automaton.Core/
COPY Automaton.Core.Scripting/*.csproj ./Automaton.Core.Scripting/
COPY Automaton.Steps/*.csproj ./Automaton.Steps/
COPY Automaton.Mcp/*.csproj ./Automaton.Mcp/
COPY Automaton.Runner/*.csproj ./Automaton.Runner/
COPY UnitTests/Automaton.Core.Tests/*.csproj ./UnitTests/Automaton.Core.Tests/
COPY UnitTests/Automaton.Runner.Tests/*.csproj ./UnitTests/Automaton.Runner.Tests/

# Restore dependencies
RUN dotnet restore Automaton.Runner/Automaton.Runner.csproj

# Copy all source code
COPY Automaton.Core/ ./Automaton.Core/
COPY Automaton.Core.Scripting/ ./Automaton.Core.Scripting/
COPY Automaton.Steps/ ./Automaton.Steps/
COPY Automaton.Mcp/ ./Automaton.Mcp/
COPY Automaton.Runner/ ./Automaton.Runner/

# Build and publish
RUN dotnet publish Automaton.Runner/Automaton.Runner.csproj \
    -c Release \
    -o /app/publish \
    -p:PublishSingleFile=false

# =============================================================================
# Stage 2: Runtime image with Python 3.13 + .NET 9
# =============================================================================
FROM python:3.13-slim AS final

# Install .NET 9 runtime dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    libicu76 \
    libssl3 \
    ca-certificates \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Install .NET 9 runtime
ENV DOTNET_ROOT=/usr/share/dotnet
RUN wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --runtime dotnet --version 9.0.6 --install-dir $DOTNET_ROOT \
    && rm dotnet-install.sh \
    && ln -s $DOTNET_ROOT/dotnet /usr/bin/dotnet

# Install common Python packages for custom steps
# These are the most frequently used packages in automation workflows
RUN pip install --no-cache-dir \
    # HTTP & API
    requests \
    httpx \
    aiohttp \
    # Data processing
    pandas \
    numpy \
    # JSON/YAML/CSV
    pyyaml \
    python-dateutil \
    # Database connectors
    pymysql \
    psycopg2-binary \
    # Cloud SDKs
    boto3 \
    # Utilities
    beautifulsoup4 \
    lxml \
    Pillow \
    python-dotenv \
    jinja2

# Create non-root user
RUN useradd -m -s /bin/bash automaton

WORKDIR /app

# Copy published app from build stage
COPY --from=build /app/publish .

# Set ownership for non-root user
RUN chown -R automaton:automaton /app

# Environment defaults (Production by default, override with DOTNET_ENVIRONMENT=Development)
ENV DOTNET_ENVIRONMENT=Production
ENV AUTOMATON_SERVER_URL=""
ENV AUTOMATON_API_KEY=""
ENV AUTOMATON_RUNNER_NAME=""

# PythonNet configuration - required for Python custom steps on Linux
ENV PYTHONNET_PYDLL=/usr/local/lib/libpython3.13.so
ENV PYTHONHOME=/usr/local
ENV PATH="/usr/local/bin:/usr/share/dotnet:$PATH"

USER automaton

# Default: run in service mode (non-interactive, for production/container use)
ENTRYPOINT ["dotnet", "automaton-runner.dll"]
CMD ["service"]
