# Automaton Runner Dockerfile
# 
# Multi-stage build for both development and production deployments.
# Environment mode is controlled via DOTNET_ENVIRONMENT (Production or Development)
#
# Run:   docker run -e AUTOMATON_SERVER_URL=... -e AUTOMATON_API_KEY=... automaton-runner

# =============================================================================
# Stage 1: Build the .NET application
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Copy solution and project files for restore
COPY src/*.sln ./
COPY src/Automaton.Core/*.csproj ./Automaton.Core/
COPY src/Automaton.Core.Scripting/*.csproj ./Automaton.Core.Scripting/
COPY src/Automaton.Steps/*.csproj ./Automaton.Steps/
COPY src/Automaton.Ai/*.csproj ./Automaton.Ai/
COPY src/Automaton.AiGuidance/*.csproj ./Automaton.AiGuidance/
COPY src/Automaton.Mcp/*.csproj ./Automaton.Mcp/
COPY src/Automaton.Messaging/*.csproj ./Automaton.Messaging/
COPY src/Automaton.Runner/*.csproj ./Automaton.Runner/
COPY src/UnitTests/Automaton.Core.Tests/*.csproj ./UnitTests/Automaton.Core.Tests/
COPY src/UnitTests/Automaton.Runner.Tests/*.csproj ./UnitTests/Automaton.Runner.Tests/

# Restore dependencies
RUN dotnet restore Automaton.Runner/Automaton.Runner.csproj

# Copy all source code
COPY src/Automaton.Core/ ./Automaton.Core/
COPY src/Automaton.Core.Scripting/ ./Automaton.Core.Scripting/
COPY src/Automaton.Steps/ ./Automaton.Steps/
COPY src/Automaton.Ai/ ./Automaton.Ai/
COPY src/Automaton.AiGuidance/ ./Automaton.AiGuidance/
COPY src/Automaton.Mcp/ ./Automaton.Mcp/
COPY src/Automaton.Messaging/ ./Automaton.Messaging/
COPY src/Automaton.Runner/ ./Automaton.Runner/

# Build and publish
RUN dotnet publish Automaton.Runner/Automaton.Runner.csproj \
    -c Release \
    -o /app/publish \
    -p:PublishSingleFile=false

# =============================================================================
# Stage 2: Runtime image with Python 3.13 + .NET 9
# =============================================================================
FROM python:3.13-slim AS final

# Install .NET 9 runtime dependencies. Docker CLI only. The Runner controls the
# host Docker daemon through the mounted /var/run/docker.sock; it does not run
# Docker-in-Docker.
RUN apt-get update && apt-get install -y --no-install-recommends \
    libicu76 \
    libssl3 \
    ca-certificates \
    wget \
    curl \
    docker-cli \
    util-linux \
    sudo \
    ffmpeg \
    ghostscript \
    imagemagick \
    poppler-utils \
    pandoc \
    && rm -rf /var/lib/apt/lists/*

# Install the .NET 9 ASP.NET Core runtime. The runner hosts the HTTP runtime gateway.
ENV DOTNET_ROOT=/usr/share/dotnet
RUN wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --runtime aspnetcore --version 9.0.6 --install-dir $DOTNET_ROOT \
    && rm dotnet-install.sh \
    && ln -s $DOTNET_ROOT/dotnet /usr/bin/dotnet

# Install common Python packages for custom steps
# These are the most frequently used packages in automation workflows
RUN pip install --no-cache-dir \
    # HTTP & API
    requests \
    httpx \
    aiohttp \
    python-multipart \
    # Validation & configuration
    pydantic \
    pydantic-settings \
    # Data processing
    pandas \
    numpy \
    pytz \
    # JSON/YAML/CSV
    pyyaml \
    python-dateutil \
    openpyxl \
    xlsxwriter \
    python-docx \
    pypdf \
    # Database connectors
    sqlalchemy \
    pymysql \
    psycopg2-binary \
    # Cloud SDKs
    boto3 \
    # Utilities
    beautifulsoup4 \
    lxml \
    Pillow \
    python-dotenv \
    jinja2 \
    markdown

# Create non-root user
RUN useradd -m -s /bin/bash automaton

# Allow automaton to install system packages via apt-get (used by system-requirements.txt)
RUN echo "automaton ALL=(root) NOPASSWD: SETENV: /usr/bin/apt-get" > /etc/sudoers.d/automaton-apt \
    && chmod 0440 /etc/sudoers.d/automaton-apt

WORKDIR /app

# Copy published app from build stage
COPY --from=build /app/publish .

# Copy FastAPI server
COPY src/Automaton.Runner/fastapi_server /app/fastapi_server
COPY src/Automaton.Runner/automaton-host-exec /usr/local/bin/automaton-host-exec
RUN chmod +x /usr/local/bin/automaton-host-exec
RUN pip install --no-cache-dir -r /app/fastapi_server/requirements.txt

# Pre-create the persistent storage layout so named volumes inherit correct
# ownership and each user-attributable path has its own namespace.
RUN mkdir -p \
        /automaton/storage/workspace/apps \
        /automaton/storage/workspace/data \
        /automaton/storage/workspace/cache \
        /automaton/storage/workspace/runner \
        /automaton/storage/logs \
        /automaton/storage/home/.config \
        /automaton/system \
        /app/config \
    && cp -a /home/automaton/. /automaton/storage/home/ \
    && rm -rf /home/automaton /automaton/workspace /app/logs \
    && ln -s /automaton/storage/home /home/automaton \
    && ln -s /automaton/storage/workspace /automaton/workspace \
    && ln -s /automaton/storage/logs /app/logs \
    && ln -s /automaton/system /automaton/storage/home/.config/AutomatonRunner

# Set ownership for non-root user
RUN chown -R automaton:automaton /app /automaton

# Environment defaults
# - Production server URL is the default (override for development)
# - API key must be provided by the user
ENV DOTNET_ENVIRONMENT=Production
ENV AUTOMATON_SERVER_URL="https://api.automaton.monster"
ENV AUTOMATON_API_KEY=""
ENV AUTOMATON_RUNNER_NAME=""
ENV AUTOMATON_WORKSPACE_ROOT=/automaton/workspace
ENV RUNNER_SCRIPTS_DIR=/automaton/workspace/apps

# PythonNet configuration - required for Python custom steps on Linux
ENV PYTHONNET_PYDLL=/usr/local/lib/libpython3.13.so
ENV PYTHONHOME=/usr/local
ENV PATH="/usr/local/bin:/usr/share/dotnet:$PATH"

USER automaton

# Default: run in service mode via entrypoint that starts FastAPI + .NET runner
COPY --chown=automaton:automaton src/Automaton.Runner/docker-entrypoint.sh /app/docker-entrypoint.sh
RUN sed -i 's/\r//' /app/docker-entrypoint.sh && chmod +x /app/docker-entrypoint.sh

CMD ["/app/docker-entrypoint.sh"]
