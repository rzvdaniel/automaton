# Automated Hetzner Deployment Plan

## Scenario Overview

**User Flow:**
1. User clicks "Create Runner" in Automaton Studio Runners page
2. User fills form: Runner Name
3. System automatically:
   - Generates API key for the runner
   - Deploys Docker container on Hetzner
   - Configures runner with name, API key, and server URL
   - Runner connects and appears in runners list

**Goal:** Zero-touch runner deployment for end users

## Feasibility Analysis

### ✅ YES - This is Technically Feasible

The scenario described is **absolutely possible** and follows common cloud infrastructure automation patterns. Here's how:

## Technical Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Automaton Studio                          │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  UI: Runners Page                                      │ │
│  │  - "Create Runner" button                             │ │
│  │  - Form: Runner Name                                   │ │
│  └────────────────┬───────────────────────────────────────┘ │
│                   │ POST /api/runners/auto-deploy          │
│  ┌────────────────▼───────────────────────────────────────┐ │
│  │  Backend API                                           │ │
│  │  - Generate API key (ak_runner_xxx)                    │ │
│  │  - Call Hetzner API to create server                   │ │
│  │  - Deploy Docker container via cloud-init             │ │
│  │  - Store runner metadata                               │ │
│  └────────────────┬───────────────────────────────────────┘ │
└───────────────────┼─────────────────────────────────────────┘
                    │ Hetzner Cloud API
                    │
┌───────────────────▼─────────────────────────────────────────┐
│                 Hetzner Cloud                                │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  Server Created (via API)                            │  │
│  │  - Ubuntu 22.04                                      │  │
│  │  - Type: CX11/CX21 (configurable)                    │  │
│  │  - cloud-init script executed on first boot         │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  cloud-init Script:                                  │  │
│  │  1. Install Docker                                   │  │
│  │  2. Pull automaton-runner image                      │  │
│  │  3. Create .env with API key & server URL           │  │
│  │  4. Start container with docker-compose             │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  Running Container                                   │  │
│  │  - Python 3.11 + requests                            │  │
│  │  - Automaton Runner                                  │  │
│  │  - Auto-configured and connected                     │  │
│  └──────────────────┬───────────────────────────────────┘  │
└────────────────────┼────────────────────────────────────────┘
                     │ SignalR/HTTPS
                     │ (connects to Studio)
┌────────────────────▼────────────────────────────────────────┐
│              Automaton Studio Server                         │
│  - Runner appears in runners list                           │
│  - Status: Connected                                         │
│  - Ready to execute workflows                                │
└──────────────────────────────────────────────────────────────┘
```

## Implementation Components

### 1. Backend API Endpoint

**New endpoint:** `POST /api/runners/auto-deploy`

```csharp
[HttpPost("auto-deploy")]
public async Task<IActionResult> AutoDeployRunner([FromBody] AutoDeployRequest request)
{
    // 1. Generate API key
    var apiKey = await _apiKeyService.CreateRunnerKeyAsync(request.RunnerName);
    
    // 2. Create Hetzner server
    var serverId = await _hetznerService.CreateServerAsync(
        name: $"runner-{request.RunnerName}",
        type: "cx11", // or configurable
        image: "ubuntu-22.04",
        cloudInit: GenerateCloudInitScript(request.RunnerName, apiKey)
    );
    
    // 3. Store runner metadata
    await _runnerRepository.CreateAsync(new Runner {
        Name = request.RunnerName,
        ApiKeyId = apiKey.Id,
        HetznerServerId = serverId,
        Status = "Provisioning"
    });
    
    return Ok(new { runnerId, serverId, status = "Provisioning" });
}
```

### 2. Hetzner API Integration

**NuGet Package:** `hcloud-dotnet` or direct REST API calls

```csharp
public class HetznerService
{
    private readonly HttpClient _client;
    private readonly string _apiToken; // From config/secrets
    
    public async Task<string> CreateServerAsync(
        string name, 
        string type, 
        string image, 
        string cloudInit)
    {
        var request = new {
            name = name,
            server_type = type,
            image = image,
            user_data = cloudInit, // cloud-init script
            location = "nbg1", // Nuremberg
            start_after_create = true
        };
        
        var response = await _client.PostAsJsonAsync(
            "https://api.hetzner.cloud/v1/servers",
            request
        );
        
        var result = await response.Content.ReadFromJsonAsync<HetznerServerResponse>();
        return result.Server.Id;
    }
    
    public async Task<ServerStatus> GetServerStatusAsync(string serverId)
    {
        // Check if server is running and cloud-init completed
    }
    
    public async Task DeleteServerAsync(string serverId)
    {
        // Delete server when runner is removed
    }
}
```

### 3. Cloud-Init Script

**Automatically executed on server first boot:**

```yaml
#cloud-config
package_update: true
package_upgrade: true

packages:
  - docker.io
  - docker-compose

runcmd:
  # Install Docker Compose v2
  - curl -L "https://github.com/docker/compose/releases/download/v2.20.0/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
  - chmod +x /usr/local/bin/docker-compose
  
  # Create deployment directory
  - mkdir -p /opt/automaton-runner
  - cd /opt/automaton-runner
  
  # Download Docker files (from your public repo or CDN)
  - curl -o Dockerfile.production https://raw.githubusercontent.com/rzvdaniel/automaton/main/src/Automaton.Runner/Dockerfile.production
  - curl -o docker-compose.production.yml https://raw.githubusercontent.com/rzvdaniel/automaton/main/src/Automaton.Runner/docker-compose.production.yml
  - curl -o docker-entrypoint.sh https://raw.githubusercontent.com/rzvdaniel/automaton/main/src/Automaton.Runner/docker-entrypoint.sh
  - chmod +x docker-entrypoint.sh
  
  # Create .env configuration
  - |
    cat > .env << 'EOF'
    AUTOMATON_API_KEY={{API_KEY}}
    AUTOMATON_SERVER_URL={{SERVER_URL}}
    AUTOMATON_RUNNER_NAME={{RUNNER_NAME}}
    DATA_DIR=/opt/automaton-runner/data
    LOGS_DIR=/opt/automaton-runner/logs
    CONFIG_DIR=/opt/automaton-runner/config
    EOF
  
  # Create data directories
  - mkdir -p data logs config
  
  # Build and start container
  - cd ..
  - docker build -f Automaton.Runner/Dockerfile.production -t automaton-runner:latest Automaton.Runner
  - cd Automaton.Runner
  - docker-compose -f docker-compose.production.yml up -d
  
  # Set up auto-restart on reboot
  - systemctl enable docker
  - docker update --restart=unless-stopped automaton-runner

write_files:
  - path: /opt/automaton-runner/.env
    content: |
      AUTOMATON_API_KEY={{API_KEY}}
      AUTOMATON_SERVER_URL={{SERVER_URL}}
      AUTOMATON_RUNNER_NAME={{RUNNER_NAME}}
    owner: root:root
    permissions: '0600'
```

**Note:** Variables `{{API_KEY}}`, `{{SERVER_URL}}`, `{{RUNNER_NAME}}` are replaced by backend before sending to Hetzner.

### 4. UI Changes in Studio

**Runners Page - Add "Create Runner" Button:**

```razor
<!-- Pages/Runners/Index.razor -->
<MudButton Variant="Variant.Filled" 
           Color="Color.Primary" 
           OnClick="OpenAutoDeployDialog">
    <MudIcon Icon="@Icons.Material.Filled.Add" Class="mr-2" />
    Auto-Deploy Runner
</MudButton>

<MudDialog @bind-IsVisible="showAutoDeployDialog">
    <TitleContent>
        <MudText Typo="Typo.h6">Auto-Deploy Runner on Hetzner</MudText>
    </TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="runnerName" 
                      Label="Runner Name" 
                      Required="true" />
        <MudText Typo="Typo.body2" Class="mt-4">
            A new Hetzner server will be created with:
        </MudText>
        <MudList Dense="true">
            <MudListItem Icon="@Icons.Material.Filled.Check">
                Docker container with Python 3.11
            </MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Check">
                Automaton Runner pre-configured
            </MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Check">
                Auto-connected to this Studio instance
            </MudListItem>
        </MudList>
        <MudText Typo="Typo.caption" Color="Color.Secondary">
            Estimated cost: €4.15/month (CX11 server)
        </MudText>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="CloseDialog">Cancel</MudButton>
        <MudButton Color="Color.Primary" 
                   OnClick="DeployRunner" 
                   Disabled="isDeploying">
            @(isDeploying ? "Deploying..." : "Deploy")
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    private string runnerName;
    private bool showAutoDeployDialog;
    private bool isDeploying;
    
    private async Task DeployRunner()
    {
        isDeploying = true;
        try
        {
            var result = await Http.PostAsJsonAsync("/api/runners/auto-deploy", 
                new { RunnerName = runnerName });
            
            if (result.IsSuccessStatusCode)
            {
                Snackbar.Add("Runner deployment started! It will appear in 2-3 minutes.", 
                            Severity.Success);
                showAutoDeployDialog = false;
                await RefreshRunners();
            }
        }
        finally
        {
            isDeploying = false;
        }
    }
}
```

### 5. Configuration Requirements

**appsettings.json additions:**

```json
{
  "Hetzner": {
    "ApiToken": "your-hetzner-api-token-here",
    "DefaultServerType": "cx11",
    "DefaultLocation": "nbg1",
    "ImageName": "ubuntu-22.04"
  },
  "AutoDeploy": {
    "ServerUrlForRunners": "https://studio.yourdomain.com",
    "DockerImageRepository": "https://raw.githubusercontent.com/rzvdaniel/automaton/main"
  }
}
```

## Security Considerations

### 1. Hetzner API Token Storage
- Store in **Azure Key Vault**, **AWS Secrets Manager**, or **HashiCorp Vault**
- Never commit to source control
- Use managed identities when possible

### 2. API Key Generation
- Generate cryptographically secure keys
- Store hashed version in database
- Associate with specific runner/server

### 3. Network Security
- Configure Hetzner firewall rules (allow only HTTPS outbound)
- No inbound ports needed (runner connects outbound)
- Consider VPC/private networking for multi-runner setups

### 4. Cost Controls
- Set per-user server limits
- Monitor Hetzner API usage
- Implement server auto-shutdown for inactive runners
- Add billing alerts

## Cost Breakdown

### Per Runner (Hetzner CX11)
- **Server**: €4.15/month
- **Traffic**: Usually included (20TB/month)
- **Storage**: Included (20GB)

### For 100 Users
- **Monthly**: €415
- **Yearly**: €4,980

### Optimization Options
1. **Shared servers** - Multiple runners per server (3-5 per CX21)
2. **Auto-scaling** - Shutdown inactive runners
3. **Reserved instances** - Prepay for discount
4. **Alternative providers** - DigitalOcean, AWS Spot, etc.

## Implementation Steps

### Phase 1: Core Infrastructure (Week 1-2)
- [ ] Set up Hetzner API integration
- [ ] Create HetznerService with server creation
- [ ] Implement cloud-init script generation
- [ ] Add configuration management

### Phase 2: Backend API (Week 2-3)
- [ ] Create auto-deploy endpoint
- [ ] Add API key generation for runners
- [ ] Implement runner metadata storage
- [ ] Add server status monitoring

### Phase 3: UI Integration (Week 3-4)
- [ ] Add "Auto-Deploy Runner" button
- [ ] Create deployment dialog
- [ ] Add progress indicator
- [ ] Show deployment status

### Phase 4: Monitoring & Management (Week 4-5)
- [ ] Server health monitoring
- [ ] Auto-cleanup for failed deployments
- [ ] Cost tracking dashboard
- [ ] User notifications (email/in-app)

### Phase 5: Testing & Documentation (Week 5-6)
- [ ] Integration testing
- [ ] Load testing (multiple deployments)
- [ ] User documentation
- [ ] Admin guide

## Alternative Approaches

### Option 1: Docker Image Registry
Instead of building on server, pull pre-built image:

**Pros:**
- Faster deployment (no build time)
- Consistent images
- Less server resources needed

**Cons:**
- Need to maintain Docker registry
- Additional cost (~$5/month for private registry)

```yaml
# cloud-init
runcmd:
  - docker pull your-registry.azurecr.io/automaton-runner:latest
  - docker run -d --restart=unless-stopped \
      -e AUTOMATON_API_KEY={{API_KEY}} \
      -e AUTOMATON_SERVER_URL={{SERVER_URL}} \
      -e AUTOMATON_RUNNER_NAME={{RUNNER_NAME}} \
      -v /opt/automaton/data:/app/data \
      -v /opt/automaton/logs:/app/logs \
      your-registry.azurecr.io/automaton-runner:latest
```

### Option 2: Kubernetes/Docker Swarm
For larger deployments:

**Pros:**
- Better resource utilization
- Easier scaling
- Built-in health monitoring

**Cons:**
- More complex setup
- Higher base cost
- Steeper learning curve

### Option 3: Serverless (Cloud Run, Azure Container Instances)
**Pros:**
- Pay per execution
- Auto-scaling
- No server management

**Cons:**
- Cold start issues
- Not suitable for SignalR persistent connections
- More expensive at scale

## Recommended Approach

**Start with Option 1 (Docker Registry + Hetzner API)**

1. Build and push runner image to Azure Container Registry
2. Use Hetzner API for server provisioning
3. Use cloud-init to pull and run pre-built image
4. Implement basic monitoring and cleanup

**Why:**
- Fastest time to market
- Predictable costs
- Easy to understand and maintain
- Can migrate to Kubernetes later if needed

## Sample API Flow

```
User clicks "Create Runner"
    │
    ▼
UI shows dialog: "Enter Runner Name"
    │
    ▼
User enters "prod-runner-01" and clicks Deploy
    │
    ▼
Frontend POST /api/runners/auto-deploy
    │
    ├─► Generate API key: ak_runner_abc123xyz
    │
    ├─► Call Hetzner API
    │   └─► Create server with cloud-init
    │       └─► cloud-init installs Docker
    │           └─► cloud-init pulls runner image
    │               └─► cloud-init starts container
    │                   └─► Container reads env vars
    │                       └─► Runner connects to Studio
    │
    ├─► Store in database:
    │   - Runner name: prod-runner-01
    │   - API key ID: (hashed)
    │   - Hetzner server ID: 12345
    │   - Status: Provisioning
    │
    └─► Return to UI: { status: "Provisioning", eta: "2-3 minutes" }
        │
        ▼
    UI polls /api/runners/{id}/status every 10 seconds
        │
        ▼
    After 2-3 minutes:
    - Server reports via SignalR: "Connected"
    - Database updated: Status = "Connected"
    - UI shows green indicator: "Ready"
        │
        ▼
    User sees runner in list, can assign workflows
```

## Testing Strategy

### 1. Unit Tests
- Hetzner API calls (mocked)
- Cloud-init script generation
- API key generation

### 2. Integration Tests
- Create test server on Hetzner
- Verify cloud-init execution
- Confirm runner connection
- Delete test server

### 3. Load Tests
- Deploy 10 runners simultaneously
- Measure deployment time
- Check for race conditions

### 4. Failure Scenarios
- Hetzner API timeout
- Invalid API key
- Network connectivity issues
- Server creation failures

## Monitoring & Observability

### Metrics to Track
1. **Deployment Success Rate** - % of successful deployments
2. **Deployment Time** - Average time to runner ready
3. **Server Health** - CPU, memory, disk usage
4. **Runner Connectivity** - Uptime percentage
5. **Cost per Runner** - Monthly cost breakdown

### Alerts
1. Deployment takes > 5 minutes
2. Server creation fails
3. Runner disconnects repeatedly
4. Cost threshold exceeded

## Documentation Needed

### For Users
- How to auto-deploy a runner
- Cost estimates
- How to monitor runner status
- How to delete/cleanup runners

### For Admins
- Hetzner setup guide
- API token configuration
- Cost monitoring dashboard
- Troubleshooting guide

### For Developers
- API documentation
- Hetzner integration architecture
- Testing guide
- Deployment scripts

## Next Steps

To proceed with implementation:

1. **Confirm approach** with team/stakeholders
2. **Set up Hetzner account** and get API token
3. **Create proof of concept** - deploy single runner manually via API
4. **Implement backend service** - HetznerService + API endpoint
5. **Build UI** - Add button and dialog
6. **Test thoroughly** - All scenarios and edge cases
7. **Deploy to staging** - Test with real Hetzner API
8. **Document** - User guide and admin guide
9. **Deploy to production** - Roll out feature flag

## Conclusion

**YES, this is absolutely possible and follows proven patterns.**

The implementation is straightforward using:
- **Hetzner Cloud API** for server provisioning
- **cloud-init** for automated configuration
- **Docker** for containerization
- **Your existing runner image** with minor modifications

The main development effort is in:
1. Backend API integration (2-3 weeks)
2. UI implementation (1 week)
3. Testing and polish (1-2 weeks)

**Total estimated time: 4-6 weeks for production-ready solution**

This approach is used by many SaaS platforms (GitHub Actions runners, GitLab runners, CI/CD services) and is well-proven.
