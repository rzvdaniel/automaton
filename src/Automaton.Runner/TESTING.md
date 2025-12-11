# Testing Automaton Runner Docker Setup

This guide covers testing the Docker setup for Automaton Runner.

## Quick Validation

Run the built-in validation script:

```bash
./test-docker-build.sh
```

This checks:
- Docker availability
- File existence and permissions
- docker-compose syntax
- Basic configuration

## Local Build Test

Test the complete build process locally:

```bash
# Build the image (from src/ directory)
cd /path/to/automaton/src
docker build -f Automaton.Runner/Dockerfile.production -t automaton-runner:test .

# This will take several minutes on first build
# Layers are cached for subsequent builds
```

## Testing with Test Configuration

### 1. Create Test Environment File

```bash
cd Automaton.Runner
cp .env.example .env.test
nano .env.test
```

Add test values:
```bash
AUTOMATON_API_KEY=ak_runner_test_key_replace_with_real
AUTOMATON_SERVER_URL=https://your-test-studio.com
AUTOMATON_RUNNER_NAME=test-docker-runner
DATA_DIR=./test-data
LOGS_DIR=./test-logs
CONFIG_DIR=./test-config
```

### 2. Start Test Container

```bash
# Create test directories
mkdir -p test-data test-logs test-config

# Start with test environment
docker-compose -f docker-compose.production.yml --env-file .env.test up
```

### 3. Verify Python Environment

Open a new terminal and run:

```bash
# Check Python version
docker exec automaton-runner python3 --version

# Expected output: Python 3.11.x

# Check requests library
docker exec automaton-runner python3 -c "import requests; print(requests.__version__)"

# Expected output: 2.x.x (version number)

# Test requests functionality
docker exec automaton-runner python3 -c "import requests; r = requests.get('https://httpbin.org/get'); print(r.status_code)"

# Expected output: 200
```

### 4. Check Runner Status

```bash
# View logs in real-time
docker logs -f automaton-runner

# Expected output should show:
# - Python installation verification
# - API key configuration
# - Runner registration
# - Service mode startup
# - Connection to Studio server
```

### 5. Test Workflow Execution

1. Open Automaton Studio
2. Create a test workflow with Python step:
```python
import requests
print("Testing requests library")
response = requests.get("https://httpbin.org/get")
print(f"Status: {response.status_code}")
```
3. Assign to your Docker runner
4. Execute and verify output

### 6. Clean Up

```bash
# Stop container
docker-compose -f docker-compose.production.yml down

# Remove test data
rm -rf test-data test-logs test-config .env.test
```

## Testing Different Scenarios

### Test 1: Environment Variable Configuration

Verify that all environment variables work:

```bash
docker run --rm \
  -e AUTOMATON_API_KEY=ak_runner_test \
  -e AUTOMATON_SERVER_URL=https://test.com \
  -e AUTOMATON_RUNNER_NAME=env-test \
  automaton-runner:test \
  bash -c "env | grep AUTOMATON"
```

Expected output:
```
AUTOMATON_API_KEY=ak_runner_test
AUTOMATON_SERVER_URL=https://test.com
AUTOMATON_RUNNER_NAME=env-test
```

### Test 2: Volume Persistence

```bash
# Start container
docker-compose -f docker-compose.production.yml up -d

# Create test file
docker exec automaton-runner bash -c "echo 'test data' > /app/data/test.txt"

# Stop container
docker-compose -f docker-compose.production.yml down

# Start again
docker-compose -f docker-compose.production.yml up -d

# Verify file persists
docker exec automaton-runner cat /app/data/test.txt

# Expected output: test data
```

### Test 3: Resource Limits

```bash
# Check configured limits
docker inspect automaton-runner | jq '.[0].HostConfig.Memory'
docker inspect automaton-runner | jq '.[0].HostConfig.NanoCpus'

# Monitor actual usage
docker stats automaton-runner --no-stream
```

### Test 4: Health Check

```bash
# Wait for container to be healthy
docker-compose -f docker-compose.production.yml up -d
sleep 40

# Check health status
docker inspect automaton-runner --format='{{.State.Health.Status}}'

# Expected output: healthy
```

### Test 5: Automatic Restart

```bash
# Start container
docker-compose -f docker-compose.production.yml up -d

# Kill process inside container
docker exec automaton-runner pkill -9 dotnet

# Wait a moment
sleep 5

# Check if restarted (should be running again)
docker ps | grep automaton-runner
```

## Integration Testing

### Test Complete Workflow

1. **Setup**: Start runner with real API key
2. **Register**: Verify runner appears in Studio
3. **Execute**: Run a test workflow
4. **Verify**: Check logs for successful execution
5. **Cleanup**: Remove test workflow

### Test Multi-User Setup

```bash
# Start multi-user compose
docker-compose -f examples/docker-compose.multi-user.yml up -d

# Verify all containers running
docker-compose -f examples/docker-compose.multi-user.yml ps

# Check each runner
docker logs automaton-runner-user1 | tail -20
docker logs automaton-runner-user2 | tail -20
docker logs automaton-runner-user3 | tail -20

# Verify in Studio (should see 3 runners)

# Cleanup
docker-compose -f examples/docker-compose.multi-user.yml down
```

## Performance Testing

### Resource Usage

Monitor resource consumption under load:

```bash
# Start monitoring
docker stats automaton-runner

# In Studio, trigger multiple workflows simultaneously
# Observe CPU and memory usage
```

### Stress Test

```bash
# Create workflow that uses significant resources
# Example: Large data processing with Python/pandas
# Monitor container behavior under load
docker stats automaton-runner --no-stream

# Check if container stays within limits
# CPU should not exceed configured limit
# Memory should not cause OOM kills
```

## Common Issues and Solutions

### Issue: Python Not Found

**Test**:
```bash
docker exec automaton-runner which python3
docker exec automaton-runner python3 --version
```

**Solution**: Rebuild image if Python is missing

### Issue: Requests Library Not Installed

**Test**:
```bash
docker exec automaton-runner pip3 list | grep requests
```

**Solution**: Check Dockerfile RUN command for pip install

### Issue: Container Exits Immediately

**Check logs**:
```bash
docker logs automaton-runner
```

**Common causes**:
- Missing API key
- Invalid server URL
- Network connectivity issues

### Issue: Runner Not Connecting to Studio

**Test connectivity**:
```bash
docker exec automaton-runner curl -v https://your-studio.com
```

**Check DNS**:
```bash
docker exec automaton-runner nslookup your-studio.com
```

### Issue: Permission Denied

**Check user**:
```bash
docker exec automaton-runner whoami
# Should output: automaton

docker exec automaton-runner ls -la /app
# Files should be owned by automaton:automaton
```

## Automated Testing Script

Create `run-tests.sh`:

```bash
#!/bin/bash
set -e

echo "Running Automaton Runner Docker Tests..."

# Test 1: File validation
./test-docker-build.sh

# Test 2: Build image
echo "Building Docker image..."
cd ..
docker build -f Automaton.Runner/Dockerfile.production -t automaton-runner:test .
cd Automaton.Runner

# Test 3: Python verification
echo "Testing Python environment..."
docker run --rm automaton-runner:test python3 --version
docker run --rm automaton-runner:test python3 -c "import requests; print('requests:', requests.__version__)"

# Test 4: Entrypoint script
echo "Testing entrypoint script..."
docker run --rm \
  -e AUTOMATON_API_KEY=ak_test \
  -e AUTOMATON_SERVER_URL=https://test.com \
  automaton-runner:test \
  bash -c "echo 'Entrypoint test passed'"

echo "All tests passed! ✓"
```

## CI/CD Testing

For automated testing in CI/CD pipelines:

```yaml
# .github/workflows/test-docker.yml
name: Test Docker Build

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      
      - name: Build Docker image
        run: |
          cd src
          docker build -f Automaton.Runner/Dockerfile.production -t automaton-runner:test .
      
      - name: Test Python
        run: |
          docker run --rm automaton-runner:test python3 --version
          docker run --rm automaton-runner:test python3 -c "import requests"
      
      - name: Test entrypoint
        run: |
          docker run --rm \
            -e AUTOMATON_API_KEY=ak_test \
            -e AUTOMATON_SERVER_URL=https://test.com \
            automaton-runner:test \
            bash -c "echo 'Test passed'"
```

## Support

- **Report Issues**: GitHub Issues
- **Documentation**: [DOCKER.md](DOCKER.md)
- **Quick Start**: [QUICKSTART-DOCKER.md](QUICKSTART-DOCKER.md)
