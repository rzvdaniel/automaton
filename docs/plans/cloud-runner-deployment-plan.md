# Cloud Runner Deployment Plan v3 - Provider-Agnostic Architecture

## Executive Summary

This document outlines the v3 plan for implementing Cloud Runners with a **provider-agnostic architecture** that decouples the system from any specific hosting provider (Hetzner, AWS, Azure, on-premise, bare metal, etc.). The approach maintains the core concept of **Management Runners** executing flows to deploy/manage user Cloud Runner containers, but introduces flexibility to support multiple infrastructure providers and deployment scenarios.

### Key Design Principles

1. **Provider Agnostic** - No hardcoded dependencies on specific cloud providers
2. **Extensible Architecture** - Easy to add new provider implementations
3. **Flexible Metadata** - JSON-based storage for provider-specific and future attributes
4. **Docker-Based Deployment** - Consistent Docker container deployment across all providers
5. **Flows as Infrastructure** - Docker operations via Python/Bash flows (provider-independent)
6. **Management Runners** - One privileged Runner per server manages all containers
7. **Admin-Only Execution** - Only Admins can trigger infrastructure flows
8. **User Choice** - Users select server based on location/specs/price/provider

### Key Features

- **Generic Server Entity** with JSON metadata for provider-specific attributes
- **Provider Abstraction Layer** for different infrastructure types
- **Flexible Capability Model** for CPU/memory/storage specifications
- **Support for On-Premise/Bare Metal** alongside cloud providers
- **Multi-Provider UI** showing servers from different sources
- **Provider-Specific Management Flows** with common interfaces

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  Your Hosting                                                       │
│  ┌───────────────────────────────────────────────────────────────┐ │
│  │  Automaton.Studio.Server                                      │ │
│  │                                                               │ │
│  │  CloudServerService        - Manage registered servers        │ │
│  │  CloudRunnerService        - Provision/deprovision runners    │ │
│  │  FlowExecutionService      - Execute management flows         │ │
│  │  IServerProviderFactory    - Provider abstraction layer       │ │
│  │    ├─ HetznerProvider                                         │ │
│  │    ├─ AwsProvider                                             │ │
│  │    ├─ AzureProvider                                           │ │
│  │    ├─ BareMetalProvider                                       │ │
│  │    └─ OnPremiseProvider                                       │ │
│  └───────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ SignalR (existing infrastructure)
                              │
┌─────────────────────────────────────────────────────────────────────┐
│  Hetzner Cloud (Provider: Hetzner)                                  │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  Management Runner (systemd service)                        │   │
│  │  - Executes management flows                                │   │
│  │  - Manages Docker containers                                │   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌───┐ ┌───┐ ┌───┐ ┌───┐                                          │
│  │ A │ │ B │ │ C │ │ D │  ... (Cloud Runner Containers)           │
│  └───┘ └───┘ └───┘ └───┘                                          │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  AWS EC2 (Provider: AWS)                                            │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  Management Runner (systemd service)                        │   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌───┐ ┌───┐                                                       │
│  │ X │ │ Y │  ... (Cloud Runner Containers)                        │
│  └───┘ └───┘                                                       │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  On-Premise / Bare Metal (Provider: OnPremise)                      │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  Management Runner (systemd service)                        │   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌───┐ ┌───┐ ┌───┐                                                │
│  │ P │ │ Q │ │ R │  ... (Cloud Runner Containers)                  │
│  └───┘ └───┘ └───┘                                                │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Database Schema

### CloudServer Entity (Generic, Provider-Agnostic)

Represents any server (cloud, on-premise, bare metal) registered in the system.

```csharp
public class CloudServer
{
    public Guid Id { get; set; }
    
    // Display information
    public string Name { get; set; }                    // "EU-Nuremberg-1", "AWS-Virginia-1", "OnPrem-DataCenter1"
    public string DisplayName { get; set; }             // "Nuremberg Production Server"
    public string? Description { get; set; }
    
    // Provider information
    public ServerProvider Provider { get; set; }        // Hetzner, AWS, Azure, OnPremise, BareМetal, Other
    public string? ProviderRegion { get; set; }         // "eu-central", "us-east-1", "westeurope", null for on-premise
    public string Location { get; set; }                // "Nuremberg, Germany", "Virginia, USA", "Office DataCenter"
    public string? CountryCode { get; set; }            // "DE", "US", null for on-premise
    
    // Server specifications (standardized)
    public ServerCapabilities Capabilities { get; set; } // JSON-serialized
    public string IpAddress { get; set; }               // "95.216.xxx.xxx", "10.0.1.50" (for on-premise)
    public string? PublicIpAddress { get; set; }        // Optional for servers with both public/private IPs
    
    // Capacity management
    public int MaxCapacity { get; set; }                // Maximum number of Cloud Runners
    public int CurrentUsage { get; set; }               // Current number of Cloud Runners
    
    // Management Runner reference
    public Guid? ManagementRunnerId { get; set; }       // FK to Runner
    public Runner? ManagementRunner { get; set; }
    
    // Pricing (for display to users) - can be 0 for owned servers
    public decimal PricePerRunner { get; set; }         // 0.70 (€/month) or 0 for owned infrastructure
    public string Currency { get; set; }                // "EUR", "USD", or empty for owned
    
    // Status
    public CloudServerStatus Status { get; set; }       // Active, Maintenance, Offline, Provisioning
    
    // Provider-specific metadata (JSON)
    /// <summary>
    /// JSON-serialized provider-specific data that may change or be extended
    /// Examples:
    /// - Hetzner: { "ServerId": "12345", "ServerType": "cx41", "DatacenterId": "nbg1-dc3" }
    /// - AWS: { "InstanceId": "i-1234567890abcdef0", "InstanceType": "t3.medium", "AvailabilityZone": "us-east-1a" }
    /// - OnPremise: { "Hostname": "srv-prod-01", "RackLocation": "Rack-A-12", "ManagementUrl": "https://ipmi.local" }
    /// </summary>
    public string? ProviderMetadata { get; set; }
    
    // Connection details (JSON)
    /// <summary>
    /// JSON-serialized connection settings for Management Runner deployment
    /// Examples:
    /// - SSH: { "SshUser": "root", "SshPort": 22, "SshKeyId": "..." }
    /// - API: { "ApiEndpoint": "https://api.provider.com", "ApiKeyId": "..." }
    /// </summary>
    public string? ConnectionSettings { get; set; }
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastHealthCheck { get; set; }
    
    // Tags for organization (JSON array)
    /// <summary>
    /// JSON-serialized array of tags for filtering/organization
    /// Example: ["production", "europe", "high-memory"]
    /// </summary>
    public string? Tags { get; set; }
    
    // Navigation
    public ICollection<CloudRunner> CloudRunners { get; set; }
}

/// <summary>
/// Standardized server capabilities - stored as JSON for flexibility
/// </summary>
public class ServerCapabilities
{
    // Core resources
    public int CpuCores { get; set; }                   // 4
    public int CpuThreads { get; set; }                 // 8 (for hyperthreading)
    public string CpuModel { get; set; }                // "Intel Xeon E5-2650 v4"
    public int MemoryMB { get; set; }                   // 16384 (16GB)
    public int StorageGB { get; set; }                  // 160
    public string StorageType { get; set; }             // "SSD", "NVMe", "HDD"
    
    // Network
    public int NetworkSpeedMbps { get; set; }           // 1000 (1 Gbps)
    public bool PublicNetwork { get; set; }             // true
    public bool PrivateNetwork { get; set; }            // false
    
    // Additional features (optional)
    public Dictionary<string, object>? AdditionalFeatures { get; set; }
    // Example: { "gpu": "NVIDIA T4", "backup": true, "monitoring": true }
    
    // Display helper
    public string ToDisplayString()
    {
        return $"{CpuCores} vCPU, {MemoryMB / 1024}GB RAM, {StorageGB}GB {StorageType}";
    }
}

public enum ServerProvider
{
    Hetzner,
    AWS,
    Azure,
    GCP,
    DigitalOcean,
    Linode,
    OnPremise,
    BareMetal,
    Other
}

public enum CloudServerStatus
{
    Provisioning,   // Being set up
    Active,         // Ready and accepting runners
    Maintenance,    // Temporarily unavailable
    Offline,        // Disconnected
    Decommissioned  // Permanently removed
}
```

### CloudRunner Entity (Provider-Agnostic)

Represents a user's Cloud Runner container on any server.

```csharp
public class CloudRunner
{
    public Guid Id { get; set; }
    
    // Ownership
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; }
    
    // Server assignment
    public Guid ServerId { get; set; }
    public CloudServer Server { get; set; }
    
    // Runner reference (once connected)
    public Guid? RunnerId { get; set; }
    public Runner? Runner { get; set; }
    
    // API Key for this runner
    public Guid? ApiKeyId { get; set; }
    public ApiKey? ApiKey { get; set; }
    
    // Container details
    public string ContainerName { get; set; }           // "runner-{userId}-{shortId}"
    public string DisplayName { get; set; }             // User-friendly name
    
    // Status
    public CloudRunnerStatus Status { get; set; }       // Creating, Running, Stopped, Failed, Deleted
    public string? StatusMessage { get; set; }          // Error message if failed
    
    // Resource tier
    public CloudRunnerTier Tier { get; set; }           // Free, Pro, Enterprise
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public enum CloudRunnerStatus
{
    Creating,
    Running,
    Stopped,
    Failed,
    Deleted
}

public enum CloudRunnerTier
{
    Free,       // 0.25 vCPU, 256MB RAM
    Pro,        // 0.5 vCPU, 512MB RAM
    Enterprise  // 1.0 vCPU, 1GB RAM
}
```

### Runner Entity Updates

Add flag to identify Management Runners:

```csharp
public class Runner
{
    // ... existing properties ...
    
    /// <summary>
    /// Indicates if this runner is a Management Runner for a CloudServer.
    /// Management Runners can execute infrastructure management flows.
    /// </summary>
    public bool IsManagementRunner { get; set; } = false;
    
    /// <summary>
    /// If this is a cloud runner, reference to the CloudRunner record
    /// </summary>
    public Guid? CloudRunnerId { get; set; }
    public CloudRunner? CloudRunner { get; set; }
}
```

---

## Provider Abstraction Layer

### IServerProvider Interface

Defines the contract for provider-specific operations.

```csharp
/// <summary>
/// Provider abstraction for different infrastructure types
/// </summary>
public interface IServerProvider
{
    ServerProvider ProviderType { get; }
    
    /// <summary>
    /// Validate provider-specific metadata
    /// </summary>
    Task<ValidationResult> ValidateMetadataAsync(string providerMetadata);
    
    /// <summary>
    /// Validate connection settings
    /// </summary>
    Task<ValidationResult> ValidateConnectionAsync(string connectionSettings);
    
    /// <summary>
    /// Perform health check on the server
    /// </summary>
    Task<ServerHealthStatus> CheckHealthAsync(CloudServer server);
    
    /// <summary>
    /// Get provider-specific server information for display
    /// </summary>
    Task<ProviderServerInfo> GetServerInfoAsync(CloudServer server);
    
    /// <summary>
    /// Get estimated cost per runner (if applicable)
    /// </summary>
    decimal? GetEstimatedCostPerRunner(CloudServer server);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string>? FieldErrors { get; set; }
}

public class ServerHealthStatus
{
    public bool IsHealthy { get; set; }
    public string? StatusMessage { get; set; }
    public DateTime CheckedAt { get; set; }
    public Dictionary<string, object>? Metrics { get; set; }
}

public class ProviderServerInfo
{
    public string DisplayInfo { get; set; }
    public Dictionary<string, string> Properties { get; set; }
    public string? IconUrl { get; set; }
}
```

### Provider Implementations

#### HetznerProvider

```csharp
public class HetznerProvider : IServerProvider
{
    public ServerProvider ProviderType => ServerProvider.Hetzner;
    
    public async Task<ValidationResult> ValidateMetadataAsync(string providerMetadata)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<HetznerServerMetadata>(providerMetadata);
            // Validate required fields: ServerId, ServerType, DatacenterId
            if (string.IsNullOrEmpty(metadata?.ServerId))
                return new ValidationResult 
                { 
                    IsValid = false, 
                    ErrorMessage = "ServerId is required for Hetzner servers" 
                };
            
            return new ValidationResult { IsValid = true };
        }
        catch
        {
            return new ValidationResult { IsValid = false, ErrorMessage = "Invalid JSON format" };
        }
    }
    
    // Implementation of other methods...
}

public class HetznerServerMetadata
{
    public string ServerId { get; set; }        // Hetzner server ID
    public string ServerType { get; set; }      // "cx41", "cx51", etc.
    public string DatacenterId { get; set; }    // "nbg1-dc3"
    public string? ImageId { get; set; }
}
```

#### OnPremiseProvider

```csharp
public class OnPremiseProvider : IServerProvider
{
    public ServerProvider ProviderType => ServerProvider.OnPremise;
    
    public async Task<ValidationResult> ValidateMetadataAsync(string providerMetadata)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<OnPremiseServerMetadata>(providerMetadata);
            // Less strict validation - just ensure JSON is valid
            return new ValidationResult { IsValid = true };
        }
        catch
        {
            return new ValidationResult { IsValid = false, ErrorMessage = "Invalid JSON format" };
        }
    }
    
    public decimal? GetEstimatedCostPerRunner(CloudServer server)
    {
        // On-premise servers typically have no per-runner cost
        return null;
    }
    
    // Implementation of other methods...
}

public class OnPremiseServerMetadata
{
    public string Hostname { get; set; }
    public string? RackLocation { get; set; }
    public string? ManagementUrl { get; set; }  // IPMI, iDRAC, iLO, etc.
    public string? HardwareModel { get; set; }
    public string? SerialNumber { get; set; }
    public Dictionary<string, string>? CustomProperties { get; set; }
}
```

#### AwsProvider

```csharp
public class AwsProvider : IServerProvider
{
    public ServerProvider ProviderType => ServerProvider.AWS;
    
    // Implementation...
}

public class AwsServerMetadata
{
    public string InstanceId { get; set; }      // "i-1234567890abcdef0"
    public string InstanceType { get; set; }    // "t3.medium"
    public string AvailabilityZone { get; set; } // "us-east-1a"
    public string? AmiId { get; set; }
    public string? VpcId { get; set; }
    public string? SubnetId { get; set; }
}
```

---

## Management Flows (Provider-Agnostic)

These flows work consistently across all providers since they operate at the Docker level.

### Flow 1: Deploy Cloud Runner

**Input Variables**:
| Variable | Type | Description |
|----------|------|-------------|
| `userId` | string | User's GUID |
| `apiKey` | string | Generated API key for the runner |
| `runnerName` | string | Display name |
| `containerName` | string | Docker container name |
| `tier` | string | "free", "pro", or "enterprise" |
| `apiUrl` | string | Studio.Server URL |
| `imageTag` | string | Runner image tag (default: "latest") |

**Bash Script** (provider-independent):
```bash
#!/bin/bash
set -e

# Input variables (injected by flow engine)
USER_ID="{{userId}}"
API_KEY="{{apiKey}}"
RUNNER_NAME="{{runnerName}}"
CONTAINER_NAME="{{containerName}}"
TIER="{{tier}}"
API_URL="{{apiUrl}}"
IMAGE_TAG="{{imageTag}}"

# Registry (GitHub Container Registry)
IMAGE="ghcr.io/rzvdaniel/automaton-runner:${IMAGE_TAG}"

# Resource limits based on tier
case $TIER in
  "free")
    CPU_LIMIT="0.25"
    MEMORY_LIMIT="256m"
    ;;
  "pro")
    CPU_LIMIT="0.5"
    MEMORY_LIMIT="512m"
    ;;
  "enterprise")
    CPU_LIMIT="1.0"
    MEMORY_LIMIT="1g"
    ;;
  *)
    CPU_LIMIT="0.25"
    MEMORY_LIMIT="256m"
    ;;
esac

echo "Pulling image: ${IMAGE}"
docker pull "${IMAGE}"

echo "Creating container: ${CONTAINER_NAME}"
docker run -d \
  --name "${CONTAINER_NAME}" \
  --restart unless-stopped \
  --cpus="${CPU_LIMIT}" \
  --memory="${MEMORY_LIMIT}" \
  --memory-swap="${MEMORY_LIMIT}" \
  --pids-limit=100 \
  --security-opt=no-new-privileges:true \
  -e AUTOMATON_SERVER_URL="${API_URL}" \
  -e AUTOMATON_API_KEY="${API_KEY}" \
  -e AUTOMATON_RUNNER_NAME="${RUNNER_NAME}" \
  -e AUTOMATON_USER_ID="${USER_ID}" \
  "${IMAGE}"

echo "Container ${CONTAINER_NAME} started successfully"

# Output container ID for reference
docker inspect --format='{{.Id}}' "${CONTAINER_NAME}"
```

### Flows 2-6: Additional Management Flows

All other management flows (Remove, Restart, Get Status, Get Stats, Install Docker) are Docker-focused and provider-agnostic.

---

## Services

### CloudServerService (Enhanced for Multi-Provider)

```csharp
public class CloudServerService
{
    private readonly IServerProviderFactory _providerFactory;
    
    // List all servers (with capacity info, grouped by provider)
    Task<List<CloudServerDto>> GetServersAsync();
    
    // List servers available for new Cloud Runners
    Task<List<CloudServerDto>> GetAvailableServersAsync(ServerProvider? filterProvider = null);
    
    // List servers by provider
    Task<List<CloudServerDto>> GetServersByProviderAsync(ServerProvider provider);
    
    // Get single server details
    Task<CloudServerDto?> GetServerAsync(Guid id);
    
    // Register a new server (Admin only)
    Task<CloudServerDto> RegisterServerAsync(RegisterCloudServerRequest request);
    
    // Update server (Admin only)
    Task UpdateServerAsync(Guid id, UpdateCloudServerRequest request);
    
    // Perform health check
    Task<ServerHealthStatus> HealthCheckAsync(Guid id);
    
    // Set server status (Admin only)
    Task SetServerStatusAsync(Guid id, CloudServerStatus status);
    
    // Delete server (Admin only, must have no runners)
    Task DeleteServerAsync(Guid id);
    
    // Update capacity count (internal use)
    Task IncrementUsageAsync(Guid serverId);
    Task DecrementUsageAsync(Guid serverId);
    
    // Get provider-specific information
    Task<ProviderServerInfo> GetProviderInfoAsync(Guid id);
}
```

### IServerProviderFactory

```csharp
public interface IServerProviderFactory
{
    IServerProvider GetProvider(ServerProvider providerType);
    IEnumerable<ServerProvider> GetSupportedProviders();
}

public class ServerProviderFactory : IServerProviderFactory
{
    private readonly Dictionary<ServerProvider, IServerProvider> _providers;
    
    public ServerProviderFactory(IServiceProvider serviceProvider)
    {
        _providers = new Dictionary<ServerProvider, IServerProvider>
        {
            [ServerProvider.Hetzner] = new HetznerProvider(),
            [ServerProvider.AWS] = new AwsProvider(),
            [ServerProvider.Azure] = new AzureProvider(),
            [ServerProvider.OnPremise] = new OnPremiseProvider(),
            [ServerProvider.BareMetal] = new BareMetalProvider(),
            // Add more providers as needed
        };
    }
    
    public IServerProvider GetProvider(ServerProvider providerType)
    {
        if (!_providers.TryGetValue(providerType, out var provider))
            throw new NotSupportedException($"Provider {providerType} is not supported");
        
        return provider;
    }
    
    public IEnumerable<ServerProvider> GetSupportedProviders()
    {
        return _providers.Keys;
    }
}
```

### CloudRunnerService

```csharp
public class CloudRunnerService
{
    // Core service methods
    Task<List<CloudRunnerDto>> GetUserCloudRunnersAsync(Guid userId);
    Task<CloudRunnerDto?> GetCloudRunnerAsync(Guid id, Guid userId);
    Task<CloudRunnerResult> CreateCloudRunnerAsync(Guid userId, CreateCloudRunnerRequest request);
    Task<CloudRunnerResult> DeleteCloudRunnerAsync(Guid id, Guid userId);
    Task<CloudRunnerResult> RestartCloudRunnerAsync(Guid id, Guid userId);
    Task UpdateRunnerConnectionAsync(Guid runnerId, bool isConnected);
    Task LinkRunnerAsync(Guid cloudRunnerId, Guid runnerId);
}
```

---

## API Endpoints

### CloudServers Controller (Admin)

```
GET    /api/cloud-servers                    - List all servers
GET    /api/cloud-servers/providers          - List supported providers
GET    /api/cloud-servers/by-provider/{type} - List servers by provider
GET    /api/cloud-servers/{id}               - Get server details
GET    /api/cloud-servers/{id}/provider-info - Get provider-specific info
POST   /api/cloud-servers                    - Register new server
PUT    /api/cloud-servers/{id}               - Update server
DELETE /api/cloud-servers/{id}               - Delete server
POST   /api/cloud-servers/{id}/status        - Set server status
POST   /api/cloud-servers/{id}/health-check  - Perform health check
GET    /api/cloud-servers/{id}/stats         - Get server stats (triggers flow)
```

### CloudRunners Controller (User)

```
GET    /api/cloud-runners              - List user's Cloud Runners
GET    /api/cloud-runners/{id}         - Get Cloud Runner details
POST   /api/cloud-runners              - Create Cloud Runner
DELETE /api/cloud-runners/{id}         - Delete Cloud Runner
POST   /api/cloud-runners/{id}/restart - Restart Cloud Runner
```

### Available Servers (Public/User)

```
GET    /api/cloud-runners/servers                - List all available servers
GET    /api/cloud-runners/servers?provider=aws   - Filter by provider
```

---

## User Interface

### Runners Page (Updated with Provider Grouping)

```
┌─────────────────────────────────────────────────────────────┐
│  Runners                                     [+ Create]      │
└─────────────────────────────────────────────────────────────┘
│                                                               │
│  🌐 Cloud Runners ────────────────────────────────────────┐  │
│  │                                                         │  │
│  │  ┌────────────────────────────────────────────────┐   │  │
│  │  │ 🟢 My Production Runner    Hetzner/EU   Pro    │   │  │
│  │  │    Created: 2 days ago      Active: 5 min ago  │   │  │
│  │  │    [Restart] [Logs] [Delete]                   │   │  │
│  │  └────────────────────────────────────────────────┘   │  │
│  │                                                         │  │
│  │  ┌────────────────────────────────────────────────┐   │  │
│  │  │ 🟢 AWS Test Runner         AWS/US       Free   │   │  │
│  │  │    Created: 1 week ago      Active: 10 min ago │   │  │
│  │  │    [Restart] [Logs] [Delete]                   │   │  │
│  │  └────────────────────────────────────────────────┘   │  │
│  │                                                         │  │
│  │  [+ Create Cloud Runner]                               │  │
│  │                                                         │  │
│  └─────────────────────────────────────────────────────────┘  │
│                                                               │
│  💻 Self-Hosted Runners ──────────────────────────────────┐  │
│  │                                                         │  │
│  │  ┌────────────────────────────────────────────────┐   │  │
│  │  │ 🟢 Office Runner            Windows            │   │  │
│  │  │    [Edit] [Delete]                             │   │  │
│  │  └────────────────────────────────────────────────┘   │  │
│  │                                                         │  │
│  │  [Download Runner]                                      │  │
│  │                                                         │  │
│  └─────────────────────────────────────────────────────────┘  │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

### Create Cloud Runner Modal (Multi-Provider)

```
┌─────────────────────────────────────────────────────────────┐
│  Create Cloud Runner                                    [X] │
└─────────────────────────────────────────────────────────────┘
│                                                               │
│  Runner Name: [My Cloud Runner________________]              │
│                                                               │
│  Filter by Provider: [All ▼] [Location: All ▼]               │
│                                                               │
│  Select Server:                                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ ⚪ 🇩🇪 Nuremberg, Germany (Hetzner)                   │    │
│  │   CX41 (4 vCPU, 16GB RAM) • 7 slots • €0.70/mo      │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ ⚪ 🇺🇸 Virginia, USA (AWS)                            │    │
│  │   t3.medium (2 vCPU, 4GB RAM) • 12 slots • $0.83/mo │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ ⚪ 🏢 Office DataCenter (On-Premise)                  │    │
│  │   Custom (8 vCPU, 32GB RAM) • 3 slots • Free (owned)│    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ ⚪ 🇸🇬 Singapore (Azure)                              │    │
│  │   D2s_v3 (2 vCPU, 8GB RAM) • 5 slots • $0.96/mo     │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                               │
│  Resource Tier:                                               │
│  ⚪ Free  (0.25 vCPU, 256MB RAM)                              │
│  ⚪ Pro   (0.5 vCPU, 512MB RAM)                               │
│  ⚪ Enterprise (1 vCPU, 1GB RAM)                              │
│                                                               │
│                          [Cancel]  [Create Cloud Runner]     │
└───────────────────────────────────────────────────────────────┘
```

### Admin: Cloud Servers Page (Multi-Provider View)

```
┌─────────────────────────────────────────────────────────────┐
│  Cloud Servers (Admin)                  [+ Register Server] │
└─────────────────────────────────────────────────────────────┘
│                                                               │
│  Filter: [All Providers ▼] [All Statuses ▼]                  │
│                                                               │
│  📊 Hetzner Servers ──────────────────────────────────────┐  │
│  │                                                         │  │
│  │  ┌────────────────────────────────────────────────┐   │  │
│  │  │ 🇩🇪 EU-Nuremberg-1       Status: 🟢 Active      │   │  │
│  │  │ CX41 • 95.216.xxx.xxx                          │   │  │
│  │  │ Capacity: ████████████░░░ 18/25                │   │  │
│  │  │ Management Runner: 🟢 Connected                │   │  │
│  │  │                                                 │   │  │
│  │  │ [Stats] [Maintenance] [Edit] [Delete]          │   │  │
│  │  └────────────────────────────────────────────────┘   │  │
│  └─────────────────────────────────────────────────────────┘  │
│                                                               │
│  ☁️ AWS Servers ───────────────────────────────────────────┐  │
│  │                                                         │  │
│  │  ┌────────────────────────────────────────────────┐   │  │
│  │  │ 🇺🇸 US-Virginia-1        Status: 🟢 Active      │   │  │
│  │  │ t3.medium • 54.123.xxx.xxx                     │   │  │
│  │  │ Capacity: ████░░░░░░░░░░░ 5/15                 │   │  │
│  │  │ Management Runner: 🟢 Connected                │   │  │
│  │  │                                                 │   │  │
│  │  │ [Stats] [Maintenance] [Edit] [Delete]          │   │  │
│  │  └────────────────────────────────────────────────┘   │  │
│  └─────────────────────────────────────────────────────────┘  │
│                                                               │
│  🏢 On-Premise Servers ───────────────────────────────────┐  │
│  │                                                         │  │
│  │  ┌────────────────────────────────────────────────┐   │  │
│  │  │ 🏢 Office-DataCenter-1   Status: 🟢 Active      │   │  │
│  │  │ Custom • 10.0.1.50 (Private)                   │   │  │
│  │  │ Capacity: █████░░░░░░░░░░ 6/20                 │   │  │
│  │  │ Management Runner: 🟢 Connected                │   │  │
│  │  │                                                 │   │  │
│  │  │ [Stats] [Maintenance] [Edit] [Delete]          │   │  │
│  │  └────────────────────────────────────────────────┘   │  │
│  └─────────────────────────────────────────────────────────┘  │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

### Register Server Form (Multi-Provider)

```
┌─────────────────────────────────────────────────────────────┐
│  Register Cloud Server                                  [X] │
└─────────────────────────────────────────────────────────────┘
│                                                               │
│  Server Provider: [Hetzner ▼]                                │
│                                                               │
│  Basic Information                                            │
│  ├─ Name: [EU-Nuremberg-1_________________]                  │
│  ├─ Display Name: [Nuremberg Production___]                  │
│  ├─ Description: [Production server in EU]                   │
│                                                               │
│  Location                                                     │
│  ├─ Region: [eu-central_______] (Provider-specific)          │
│  ├─ Location: [Nuremberg, Germany________]                   │
│  └─ Country: [DE___]                                          │
│                                                               │
│  Server Specifications                                        │
│  ├─ CPU Cores: [4___]    CPU Threads: [8___]                 │
│  ├─ Memory (MB): [16384_]                                     │
│  ├─ Storage (GB): [160__]  Type: [SSD ▼]                     │
│  └─ Network (Mbps): [1000_]                                   │
│                                                               │
│  Network Configuration                                        │
│  ├─ IP Address: [95.216.xxx.xxx__________]                   │
│  └─ Public IP: [Same as IP Address______] (optional)         │
│                                                               │
│  Capacity & Pricing                                           │
│  ├─ Max Runners: [25___]                                      │
│  ├─ Price per Runner: [0.70_] Currency: [EUR ▼]              │
│  └─ [✓] This is owned infrastructure (no cost)               │
│                                                               │
│  Provider-Specific Settings (JSON)                            │
│  ┌───────────────────────────────────────────┐              │
│  │ {                                          │              │
│  │   "ServerId": "12345678",                  │              │
│  │   "ServerType": "cx41",                    │              │
│  │   "DatacenterId": "nbg1-dc3"               │              │
│  │ }                                          │              │
│  └───────────────────────────────────────────┘              │
│  [Validate JSON]                                              │
│                                                               │
│  Connection Settings (JSON) - Optional                        │
│  ┌───────────────────────────────────────────┐              │
│  │ {                                          │              │
│  │   "SshUser": "root",                       │              │
│  │   "SshPort": 22                            │              │
│  │ }                                          │              │
│  └───────────────────────────────────────────┘              │
│                                                               │
│  Tags (comma-separated)                                       │
│  [production, europe, high-memory____________]                │
│                                                               │
│                               [Cancel]  [Register Server]    │
└───────────────────────────────────────────────────────────────┘
```

---

## Setup Procedure

### Generic Server Setup (All Providers)

The setup process is now more flexible to support different infrastructure types:

1. **Provision Server** (varies by provider)
   - **Cloud Provider**: Use provider's console/API (Hetzner, AWS, Azure, etc.)
   - **On-Premise**: Use existing server hardware
   - **Bare Metal**: Physical server setup

2. **Install Docker** (same for all providers)
   ```bash
   curl -fsSL https://get.docker.com -o get-docker.sh
   sh get-docker.sh
   ```
   Or use the "Install Docker" management flow.

3. **Install Management Runner** (same for all providers)
   ```bash
   # Download runner
   mkdir -p /opt/automaton-runner
   cd /opt/automaton-runner
   
   # Download latest release
   wget https://studio.automaton.monster/downloads/runner/latest/automaton-runner-linux-x64
   chmod +x automaton-runner-linux-x64
   
   # Create config
   cat > appsettings.json << EOF
   {
     "ApiUrl": "https://studio.automaton.monster",
     "ApiKey": "<ADMIN_GENERATED_API_KEY>"
   }
   EOF
   
   # Create systemd service
   cat > /etc/systemd/system/automaton-runner.service << EOF
   [Unit]
   Description=Automaton Management Runner
   After=network.target docker.service
   Requires=docker.service

   [Service]
   Type=simple
   User=root
   WorkingDirectory=/opt/automaton-runner
   ExecStart=/opt/automaton-runner/automaton-runner-linux-x64
   Restart=always
   RestartSec=10

   [Install]
   WantedBy=multi-user.target
   EOF
   
   # Enable and start
   systemctl daemon-reload
   systemctl enable automaton-runner
   systemctl start automaton-runner
   ```

4. **Register in Automaton Studio**
   - Go to Admin > Cloud Servers
   - Click "Register Server"
   - Select provider type
   - Fill in server details, capabilities, and provider-specific metadata
   - Select the Management Runner

### Provider-Specific Examples

#### Hetzner Cloud Setup

```json
// Provider Metadata
{
  "ServerId": "12345678",
  "ServerType": "cx41",
  "DatacenterId": "nbg1-dc3"
}

// Connection Settings
{
  "SshUser": "root",
  "SshPort": 22
}
```

#### AWS EC2 Setup

```json
// Provider Metadata
{
  "InstanceId": "i-1234567890abcdef0",
  "InstanceType": "t3.medium",
  "AvailabilityZone": "us-east-1a",
  "VpcId": "vpc-12345678",
  "SubnetId": "subnet-12345678"
}

// Connection Settings
{
  "SshUser": "ubuntu",
  "SshPort": 22,
  "KeyPairName": "my-keypair"
}
```

#### On-Premise Setup

```json
// Provider Metadata
{
  "Hostname": "srv-prod-01.company.local",
  "RackLocation": "Rack-A-12",
  "ManagementUrl": "https://ipmi.company.local",
  "HardwareModel": "Dell PowerEdge R640",
  "SerialNumber": "ABC123XYZ789"
}

// Connection Settings
{
  "SshUser": "automaton",
  "SshPort": 22,
  "ManagementInterface": "iDRAC"
}
```

---

## Implementation Phases

### Phase 1: Database & Entities (Enhanced)

- [ ] Create `CloudServer` entity with JSON metadata fields
- [ ] Create `ServerCapabilities` class
- [ ] Create `ServerProvider` enum
- [ ] Create `CloudRunner` entity
- [ ] Add `IsManagementRunner` and `CloudRunnerId` to `Runner` entity
- [ ] Update `ApplicationDbContext` with new DbSets
- [ ] Create database migration
- [ ] Create DTOs and mapping

**Estimated: 2 days**

### Phase 2: Provider Abstraction Layer

- [ ] Create `IServerProvider` interface
- [ ] Create `IServerProviderFactory` and implementation
- [ ] Implement `HetznerProvider`
- [ ] Implement `AwsProvider`
- [ ] Implement `AzureProvider`
- [ ] Implement `OnPremiseProvider`
- [ ] Implement `BareMetalProvider`
- [ ] Create provider metadata classes
- [ ] Create validation logic

**Estimated: 3 days**

### Phase 3: Core Services (Enhanced)

- [ ] Create `CloudServerService` with multi-provider support
- [ ] Create `CloudRunnerService`
- [ ] Add provider factory integration
- [ ] Add health check functionality
- [ ] Create API controllers with provider endpoints

**Estimated: 2 days**

### Phase 4: Management Flows

- [ ] Create "Deploy Cloud Runner" flow
- [ ] Create "Remove Cloud Runner" flow
- [ ] Create "Restart Cloud Runner" flow
- [ ] Create "Get Container Status" flow
- [ ] Create "Get Server Stats" flow
- [ ] Create "Install Docker" flow
- [ ] Store as system flows (templates)

**Estimated: 1 day**

### Phase 5: User Interface (Enhanced)

- [ ] Update Runners page with Cloud Runners section
- [ ] Create "Create Cloud Runner" modal with provider filter
- [ ] Create Cloud Runner status display component
- [ ] Add delete/restart actions
- [ ] Add provider icons and badges

**Estimated: 2 days**

### Phase 6: Admin Interface (Enhanced)

- [ ] Create "Cloud Servers" admin page with provider grouping
- [ ] Create "Register Server" form with provider selection
- [ ] Add provider metadata JSON editor with validation
- [ ] Create server capacity dashboard
- [ ] Create server stats view
- [ ] Add provider-specific info display

**Estimated: 3 days**

### Phase 7: Runner Image & Deployment

- [ ] Set up GitHub Container Registry
- [ ] Create GitHub Action to build and push runner image
- [ ] Update runner Dockerfile if needed
- [ ] Test deployment flow end-to-end on multiple providers

**Estimated: 2 days**

### Phase 8: Testing & Documentation

- [ ] End-to-end testing on Hetzner
- [ ] End-to-end testing on AWS/Azure (if possible)
- [ ] Test on-premise registration
- [ ] Error handling improvements
- [ ] UI polish
- [ ] Documentation updates
- [ ] Provider setup guides

**Estimated: 2 days**

---

## Total Estimated Time: 17-18 days

---

## Security Considerations

- **Management Runner Security**
  - Runs as root with Docker socket access
  - API key is admin-level, never exposed to users
  - Only accessible by Admin users for flow execution
  - Marked as `IsManagementRunner = true` in database

- **Cloud Runner Isolation**
  - Each container has resource limits (CPU, memory, PIDs)
  - Runs with `--security-opt=no-new-privileges:true`
  - Cannot access Docker socket
  - Cannot access other containers
  - Unique API key per Cloud Runner

- **API Security**
  - Cloud Runner endpoints require authentication
  - Users can only manage their own Cloud Runners
  - Server registration/management requires Admin role
  - Management flow execution requires Admin role

- **Provider Credential Storage**: If providers require API keys, store them encrypted
- **Multi-Tenancy**: Ensure users can only see servers available to them
- **Provider Validation**: Validate provider metadata to prevent injection attacks
- **Network Isolation**: On-premise servers might need special network rules

---

## Cost Comparison (Multi-Provider Example)

### Monthly Cost per Runner by Provider

| Provider | Server Type | Monthly Cost | Max Runners | Cost/Runner |
|----------|-------------|--------------|-------------|-------------|
| **Hetzner** | CX41 | €17.49 | 25 | €0.70 |
| **AWS** | t3.medium | $41.61 | 15 | $2.77 |
| **Azure** | D2s_v3 | $70.08 | 15 | $4.67 |
| **On-Premise** | Custom | $0 (owned) | 50 | $0 |
| **DigitalOcean** | 4GB | $24.00 | 10 | $2.40 |

This flexibility allows administrators to:
- Use cost-effective Hetzner servers for European users
- Leverage existing on-premise infrastructure
- Provide premium options on AWS/Azure for compliance requirements
- Mix and match based on location and cost needs

---

## Key Benefits

1. **No Vendor Lock-in**: Use any infrastructure provider or mix multiple providers simultaneously
2. **Cost Optimization**: Choose the most cost-effective provider per region
3. **Leverage Existing Infrastructure**: Use on-premise servers without additional cost
4. **Compliance Flexibility**: Meet data residency requirements with provider choice
5. **Scalability**: Add new providers without code changes (just implement `IServerProvider`)
6. **Future-Proof**: JSON metadata allows extending attributes without schema changes
7. **User Choice**: Users can select based on location, price, or provider preference

---

## Appendix: Example Provider Implementations

### Example: DigitalOcean Provider

```csharp
public class DigitalOceanProvider : IServerProvider
{
    public ServerProvider ProviderType => ServerProvider.DigitalOcean;
    
    public async Task<ValidationResult> ValidateMetadataAsync(string providerMetadata)
    {
        var metadata = JsonSerializer.Deserialize<DigitalOceanServerMetadata>(providerMetadata);
        if (metadata?.DropletId <= 0)
            return new ValidationResult { IsValid = false, ErrorMessage = "Invalid DropletId" };
        
        return new ValidationResult { IsValid = true };
    }
    
    // ... other methods
}

public class DigitalOceanServerMetadata
{
    public long DropletId { get; set; }
    public string Size { get; set; }      // "s-2vcpu-4gb"
    public string Region { get; set; }    // "nyc1"
    public string? ImageId { get; set; }
}
```

### Example: Google Cloud Provider

```csharp
public class GcpProvider : IServerProvider
{
    public ServerProvider ProviderType => ServerProvider.GCP;
    
    // ... implementation
}

public class GcpServerMetadata
{
    public string InstanceName { get; set; }
    public string MachineType { get; set; }   // "n1-standard-2"
    public string Zone { get; set; }          // "us-central1-a"
    public string ProjectId { get; set; }
}
```

---

## Open Questions

1. **Auto-Discovery of On-Premise Servers?**
   - Should there be a tool to scan local networks and register servers?
   - Or is manual registration sufficient?

2. **Provider API Integration?**
   - Should the system integrate with provider APIs (Hetzner, AWS) for automated provisioning?
   - Or keep it manual for initial implementation?

3. **Multi-Region Support?**
   - Should users be able to deploy to multiple servers simultaneously?
   - What's the use case for this?

4. **Cost Tracking?**
   - Should the system track actual costs from cloud providers?
   - Or rely on estimated pricing shown in UI?

5. **Server Auto-Scaling?**
   - Should servers be automatically provisioned when capacity is reached?
   - Out of scope for v3?

---

## Files to Create

```
src/Automaton.Studio.Server/
├── Entities/
│   ├── CloudServer.cs (with JSON fields)
│   ├── CloudRunner.cs
│   └── ServerCapabilities.cs
├── Enums/
│   └── ServerProvider.cs
├── Providers/
│   ├── IServerProvider.cs
│   ├── IServerProviderFactory.cs
│   ├── ServerProviderFactory.cs
│   ├── HetznerProvider.cs
│   ├── AwsProvider.cs
│   ├── AzureProvider.cs
│   ├── OnPremiseProvider.cs
│   └── BareMetalProvider.cs
├── Services/
│   ├── CloudServerService.cs
│   └── CloudRunnerService.cs
├── Controllers/
│   ├── CloudServersController.cs
│   └── CloudRunnersController.cs
├── Models/
│   ├── CloudServerDto.cs
│   ├── CloudRunnerDto.cs
│   ├── ServerCapabilitiesDto.cs
│   ├── CreateCloudRunnerRequest.cs
│   ├── RegisterCloudServerRequest.cs
│   ├── HetznerServerMetadata.cs
│   ├── AwsServerMetadata.cs
│   ├── OnPremiseServerMetadata.cs
│   └── ProviderServerInfo.cs
└── Migrations/
    └── YYYYMMDD_AddCloudRunnerEntitiesV3.cs

src/Automaton.Studio/
├── Pages/
│   ├── Runners/
│   │   ├── RunnersPage.razor (update)
│   │   └── Components/
│   │       ├── CloudRunnersSection.razor
│   │       └── CreateCloudRunnerModal.razor (with provider filter)
│   └── Admin/
│       └── CloudServers/
│           ├── CloudServersPage.razor (with provider grouping)
│           ├── RegisterServerModal.razor (with provider selection)
│           └── Components/
│               ├── ProviderBadge.razor
│               └── ServerCapabilitiesDisplay.razor
└── Services/
    ├── CloudServerService.cs (HTTP client)
    └── CloudRunnerService.cs (HTTP client)
```

---

## Conclusion

This plan outlines a **provider-agnostic architecture** for Cloud Runner deployment that can scale across multiple cloud providers, on-premise infrastructure, and bare metal servers. The use of JSON for flexible metadata ensures the system can evolve without constant schema changes, while the provider abstraction layer makes it easy to add support for new infrastructure types.

The Docker-based deployment model ensures consistency and reliability across all provider types while maintaining strong security and isolation for Cloud Runner containers.
