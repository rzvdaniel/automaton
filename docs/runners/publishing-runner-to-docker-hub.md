# Publishing Automaton Runner to Docker Hub

This guide explains how to build and publish the Automaton Runner Docker image to Docker Hub from the command line.

## Prerequisites

1. **Docker Desktop** installed and running
2. **Docker Hub account** (e.g., `rzvdaniel` or `automatonhq`)
3. **Docker Hub Access Token** created at https://hub.docker.com/settings/security
4. **Repository** created on Docker Hub: `automaton-runner`

## Step 1: Login to Docker Hub

Before you can push images, authenticate with Docker Hub:

```bash
# Login with your Docker Hub username
docker login

# Enter your Docker Hub username when prompted
# Enter your Access Token (NOT your password) when prompted
```

**Security tip:** Use an access token instead of your password. Create one at:
https://hub.docker.com/settings/security

## Step 2: Build the Docker Image

Navigate to the repository root and build the image:

```bash
# Navigate to repo root
cd C:\Work\automaton

# Build the runner image
# Context is 'src' folder (contains all required projects)
docker build -t rzvdaniel/automaton-runner:latest -f src/Automaton.Runner/Dockerfile src

# Optional: Build with a specific version tag
docker build -t rzvdaniel/automaton-runner:v1.0.0 -f src/Automaton.Runner/Dockerfile src
```

### Build Arguments Explained

- `-t rzvdaniel/automaton-runner:latest` — Tag the image with your Docker Hub username/repo and version
- `-f src/Automaton.Runner/Dockerfile` — Path to the Dockerfile
- `src` — Build context (the Dockerfile needs access to all projects in `src/`)

### Multi-Tag Build

To publish both `latest` and a version tag:

```bash
docker build \
  -t rzvdaniel/automaton-runner:latest \
  -t rzvdaniel/automaton-runner:v1.0.0 \
  -f src/Automaton.Runner/Dockerfile \
  src
```

## Step 3: Test the Image Locally (Optional but Recommended)

Before publishing, verify the image works:

```bash
# Run a test container
docker run -d \
  --name automaton-runner-test \
  -e AUTOMATON_SERVER_URL=http://host.docker.internal:5091 \
  -e AUTOMATON_API_KEY=ak_your_test_key_here \
  -e AUTOMATON_RUNNER_NAME=Test-Runner \
  rzvdaniel/automaton-runner:latest

# Check logs
docker logs -f automaton-runner-test

# If successful, you should see:
# "Runner registered successfully"
# "Connected to Automaton Server"

# Clean up test container
docker stop automaton-runner-test
docker rm automaton-runner-test
```

## Step 4: Push to Docker Hub

Once the image is built and tested, push it to Docker Hub:

```bash
# Push the latest tag
docker push rzvdaniel/automaton-runner:latest

# Push a specific version tag (if you built one)
docker push rzvdaniel/automaton-runner:v1.0.0
```

### Push Multiple Tags

If you tagged with multiple versions:

```bash
# Push all tags for this repository
docker push rzvdaniel/automaton-runner --all-tags
```

## Step 5: Verify on Docker Hub

1. Go to https://hub.docker.com/r/rzvdaniel/automaton-runner
2. Check that your image appears under **Tags**
3. Verify the image size and push timestamp

## Complete Example Workflow

Here's a complete script to build, test, and publish:

```bash
# 1. Navigate to repo root
cd C:\Work\automaton

# 2. Build with both latest and version tags
docker build \
  -t rzvdaniel/automaton-runner:latest \
  -t rzvdaniel/automaton-runner:v1.2.0 \
  -f src/Automaton.Runner/Dockerfile \
  src

# 3. Test the image
docker run -d \
  --name runner-test \
  -e AUTOMATON_SERVER_URL=http://host.docker.internal:5091 \
  -e AUTOMATON_API_KEY=ak_test_key \
  -e AUTOMATON_RUNNER_NAME=Test \
  rzvdaniel/automaton-runner:latest

docker logs -f runner-test
# Press Ctrl+C when you see "Runner registered successfully"

# 4. Clean up test
docker stop runner-test
docker rm runner-test

# 5. Login to Docker Hub
docker login

# 6. Push both tags
docker push rzvdaniel/automaton-runner:latest
docker push rzvdaniel/automaton-runner:v1.2.0
```

## Automated Publishing with Azure Pipelines

For production releases, use the Azure Pipeline defined in `azure-pipelines-runner-docker.yml` at the repository root.

The pipeline automatically:
- Builds the Docker image on tag pushes (e.g., `v1.0.0`, `runner-v1.0.0`)
- Tags with both the version and `latest`
- Pushes to Docker Hub
- Runs on triggers for runner-related code changes

### Trigger a Pipeline Build

```bash
# Create and push a version tag
git tag -a v1.2.0 -m "Release version 1.2.0"
git push origin v1.2.0

# Or use a runner-specific tag
git tag -a runner-v1.2.0 -m "Runner version 1.2.0"
git push origin runner-v1.2.0
```

The pipeline will automatically build and publish the image.

## Best Practices

### Versioning

Use semantic versioning for tags:
- `v1.0.0` — Major release
- `v1.1.0` — Minor release (new features)
- `v1.1.1` — Patch release (bug fixes)

### Testing Before Publishing

**Always test the image locally** before pushing to Docker Hub to avoid publishing broken images.

### Docker Hub Rate Limits

Docker Hub has rate limits for pulls:
- **Anonymous**: 100 pulls per 6 hours
- **Authenticated**: 200 pulls per 6 hours
- **Pro/Team**: Higher limits

For production, consider:
- Using a Docker Hub Pro account
- Or hosting on a private registry (Azure Container Registry, AWS ECR, etc.)

### Image Size Optimization

The current Dockerfile uses multi-stage builds to minimize image size:
- Build stage: Uses .NET SDK (large)
- Runtime stage: Uses .NET Runtime + Python (smaller)

Current image size: ~500-600MB (includes Python + common packages)

## Troubleshooting

### Authentication Failed

```
Error: denied: incorrect username or password
```

**Solution:** Use an access token instead of your password:
1. Create token at https://hub.docker.com/settings/security
2. Use token when running `docker login`

### Image Build Failed

```
Error: failed to solve: failed to compute cache key
```

**Solution:** Ensure you're in the repo root and build context is `src`:
```bash
cd C:\Work\automaton
docker build -f src/Automaton.Runner/Dockerfile src
```

### Push Denied

```
Error: denied: requested access to the resource is denied
```

**Solution:** 
1. Ensure you're logged in: `docker login`
2. Ensure the repository name matches your Docker Hub username
3. Create the repository on Docker Hub first if it doesn't exist

## Related Documentation

- [Running Runner in Docker](./docker-runner.md) — How to run the published image
- [Azure Pipelines Configuration](../../azure-pipelines-runner-docker.yml) — Automated CI/CD setup
