# Local Testing Guide for Automaton Runner Docker

This guide helps testers quickly set up and run the Automaton Runner Docker container locally to test with Automaton Studio.

## Quick Start for Testers

### Prerequisites

1. **Docker Desktop** installed and running
   - Windows/Mac: [Download Docker Desktop](https://www.docker.com/products/docker-desktop)
   - Linux: [Install Docker Engine](https://docs.docker.com/engine/install/)
2. **Automaton Studio** running locally or accessible (e.g., https://localhost:7091 or https://studio.yourdomain.com)
3. **API Key** from Automaton Studio (we'll create this in Step 1)

### Step-by-Step Setup (5 minutes)

#### Step 1: Get API Key from Automaton Studio

1. Open Automaton Studio in your browser (e.g., `https://localhost:7091`)
2. Navigate to **Settings → API Keys**
3. Click **Create API Key**
4. Fill in:
   - **Name**: `Local Test Runner`
   - **Scope**: Select **Runner**
   - **Description**: `For local Docker testing`
   - **Expiration**: Leave empty or set future date
5. Click **Create**
6. **Important**: Copy the API key immediately (it starts with `ak_runner_...`)

#### Step 2: Run the Setup Script

We've created a convenient script that does everything for you:

```bash
# Navigate to the Runner directory
cd /path/to/automaton/src/Automaton.Runner

# Run the setup script (it will prompt you for the API key)
./setup-local-test.sh
```

The script will:
- ✅ Check Docker is running
- ✅ Prompt for your API key and Studio URL
- ✅ Create `.env` configuration file
- ✅ Build the Docker image
- ✅ Start the container
- ✅ Show the logs

**That's it!** Your runner should now be connected to Automaton Studio.

#### Step 3: Verify in Studio

1. Go back to Automaton Studio
2. Navigate to **Runners** page
3. You should see your runner listed as **Connected** with name `local-test-runner`

#### Step 4: Test with a Workflow

1. In Studio, create a simple test workflow:
   - Add a Python step with this code:
     ```python
     import requests
     print("Testing requests library")
     response = requests.get("https://httpbin.org/get")
     print(f"Status: {response.status_code}")
     print("✅ Runner is working!")
     ```
2. Assign the workflow to your Docker runner
3. Execute the workflow
4. Check the output - you should see the success message

### Manual Setup (Alternative)

If you prefer to set up manually without the script:

#### 1. Clone/Navigate to Repository

```bash
git clone https://github.com/rzvdaniel/automaton.git
cd automaton/src/Automaton.Runner
```

#### 2. Create Configuration File

```bash
cp .env.example .env
nano .env  # or use your preferred editor
```

Set these values:
```bash
# REQUIRED: Your API key from Studio
AUTOMATON_API_KEY=ak_runner_your_key_here

# REQUIRED: Your Studio URL (use localhost for local testing)
AUTOMATON_SERVER_URL=https://localhost:7091

# OPTIONAL: Custom runner name
AUTOMATON_RUNNER_NAME=local-test-runner

# Data directories (will be created automatically)
DATA_DIR=./data
LOGS_DIR=./logs
CONFIG_DIR=./config
```

#### 3. Build Docker Image

```bash
# From the src/ directory (parent of Automaton.Runner)
cd ..
docker build -f Automaton.Runner/Dockerfile.production -t automaton-runner:local .
```

This will take 5-10 minutes on first build (downloads .NET, Python, etc.)

#### 4. Start Container

```bash
# Go back to Runner directory
cd Automaton.Runner

# Create data directories
mkdir -p data logs config

# Start the container
docker-compose -f docker-compose.production.yml up -d
```

#### 5. View Logs

```bash
docker-compose -f docker-compose.production.yml logs -f
```

You should see:
```
✓ Python and pip are installed
✓ Python 'requests' library is installed
📋 Configuration:
  • Server URL: https://localhost:7091
  • Runner Name: local-test-runner
✓ Authentication configured successfully
✓ Runner registered successfully
🚀 Starting Automaton Runner in service mode...
```

Press `Ctrl+C` to stop following logs (container keeps running).

### Troubleshooting

#### Issue: "Cannot connect to Docker daemon"

**Solution**: Make sure Docker Desktop is running
```bash
# Check Docker status
docker info
```

#### Issue: "Connection refused" when connecting to Studio

**Problem**: If Studio is running on localhost (https://localhost:7091), the container can't reach it with "localhost"

**Solution 1** - Use host.docker.internal (Mac/Windows):
```bash
# In .env file, change:
AUTOMATON_SERVER_URL=https://host.docker.internal:7091
```

**Solution 2** - Use your machine's IP address:
```bash
# Find your IP (Mac/Linux)
ifconfig | grep "inet " | grep -v 127.0.0.1

# Find your IP (Windows)
ipconfig

# In .env file, use your IP:
AUTOMATON_SERVER_URL=https://192.168.1.100:7091
```

**Solution 3** - Use Docker host network (Linux only):
```bash
# Add to docker-compose.production.yml
services:
  automaton-runner:
    network_mode: "host"
```

#### Issue: "SSL certificate problem"

**Solution**: If using self-signed certificate for local Studio, you may need to disable SSL verification (development only):
```bash
# Add to docker-compose.production.yml under environment:
- DOTNET_ENVIRONMENT=Development
```

#### Issue: "Authentication failed"

**Solution**: 
1. Verify API key is correct (copy-paste from Studio)
2. Verify API key has "Runner" scope
3. Check API key hasn't expired
4. Recreate API key in Studio if needed

#### Issue: Runner appears offline in Studio

**Solution**:
1. Check container logs: `docker logs automaton-runner`
2. Verify network connectivity: `docker exec automaton-runner curl -k https://your-studio-url`
3. Restart container: `docker-compose -f docker-compose.production.yml restart`

### Common Commands

```bash
# Start runner
docker-compose -f docker-compose.production.yml up -d

# Stop runner
docker-compose -f docker-compose.production.yml down

# View live logs
docker-compose -f docker-compose.production.yml logs -f

# Restart runner
docker-compose -f docker-compose.production.yml restart

# Check runner status
docker-compose -f docker-compose.production.yml ps

# Execute command in running container
docker exec -it automaton-runner bash

# Test Python inside container
docker exec automaton-runner python3 --version
docker exec automaton-runner python3 -c "import requests; print(requests.__version__)"

# Rebuild after code changes
docker-compose -f docker-compose.production.yml build --no-cache
docker-compose -f docker-compose.production.yml up -d
```

### Cleaning Up

When you're done testing:

```bash
# Stop and remove container
docker-compose -f docker-compose.production.yml down

# Remove data (optional - removes all runner data)
rm -rf data logs config

# Remove Docker image (optional - frees up ~500MB)
docker rmi automaton-runner:local

# Remove configuration (optional)
rm .env
```

### Testing Different Scenarios

#### Test 1: Basic Python Script
Create workflow in Studio:
```python
print("Hello from Docker runner!")
import sys
print(f"Python version: {sys.version}")
```

#### Test 2: Using requests Library
```python
import requests
response = requests.get("https://api.github.com")
print(f"GitHub API status: {response.status_code}")
print(f"Rate limit: {response.headers.get('X-RateLimit-Limit')}")
```

#### Test 3: File Operations
```python
# Write file
with open("/app/data/test.txt", "w") as f:
    f.write("Test data from workflow")
print("File written successfully")

# Read file
with open("/app/data/test.txt", "r") as f:
    content = f.read()
print(f"File content: {content}")
```

#### Test 4: Environment Variables
```python
import os
runner_name = os.environ.get('AUTOMATON_RUNNER_NAME', 'unknown')
print(f"Running on: {runner_name}")
```

### Development Workflow

For developers testing changes:

```bash
# 1. Make code changes to Automaton.Runner

# 2. Rebuild image
cd ../  # Go to src/ directory
docker build -f Automaton.Runner/Dockerfile.production -t automaton-runner:dev .

# 3. Update docker-compose to use dev image
cd Automaton.Runner
# Edit docker-compose.production.yml: change image to automaton-runner:dev

# 4. Restart container
docker-compose -f docker-compose.production.yml down
docker-compose -f docker-compose.production.yml up -d

# 5. Test your changes
docker-compose -f docker-compose.production.yml logs -f
```

### Next Steps

- **Production deployment**: See [DOCKER.md](DOCKER.md)
- **Multi-user setup**: See [examples/docker-compose.multi-user.yml](examples/docker-compose.multi-user.yml)
- **Hetzner deployment**: See [examples/hetzner-deployment.md](examples/hetzner-deployment.md)
- **Architecture details**: See [ARCHITECTURE.md](ARCHITECTURE.md)

### Support

If you encounter issues:
1. Check the logs: `docker-compose -f docker-compose.production.yml logs`
2. Review [TESTING.md](TESTING.md) for detailed testing procedures
3. Check [DOCKER.md](DOCKER.md) troubleshooting section
4. Open an issue on GitHub with logs and error messages
