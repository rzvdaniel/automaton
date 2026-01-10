# Automaton Runner - Resilience & Logging Guide

Preferred deployment: Docker Compose (`docker-compose.runner.yml`). Manual console execution is not recommended for production; use containerized runners managed via Compose. References to console commands in this guide describe internal behavior and can be executed inside a container using `docker compose exec runner ...` when debugging.

## ?? Table of Contents

1. [Overview](#overview)
2. [Auto-Connect & Retry Mechanism](#auto-connect--retry-mechanism)
3. [Circuit Breaker Pattern](#circuit-breaker-pattern)
4. [Continuous Retry Loop](#continuous-retry-loop)
5. [Logging Architecture](#logging-architecture)
6. [Log Rotation](#log-rotation)
7. [Monitoring & Troubleshooting](#monitoring--troubleshooting)
8. [Configuration Options](#configuration-options)

---

## Overview

The Automaton Runner is designed with **production-grade resilience** to ensure continuous operation even during extended server outages. This document explains how the runner automatically connects, retries on failures, logs operations, and manages log files.

### Key Features

- ? **Infinite Retry** - Never gives up connecting
- ? **Circuit Breaker** - Prevents server overload
- ? **Smart Backoff** - Respects cool-down periods
- ? **Automatic Log Rotation** - Prevents disk space issues
- ? **Comprehensive Logging** - File and database logs
- ? **Zero Manual Intervention** - Fully automatic recovery

---

## Auto-Connect & Retry Mechanism

### Connection Flow

```
???????????????????????????????????????????????????????????
? 1. Runner Starts                                        ?
?    ??? Load Configuration (API Key, Server URL, etc.)  ?
???????????????????????????????????????????????????????????
                          ?
???????????????????????????????????????????????????????????
? 2. Initial Connection Attempt                           ?
?    ??? Validate API Key                                 ?
?    ??? Check Runner Registration                        ?
?    ??? Connect to SignalR Hub                           ?
???????????????????????????????????????????????????????????
                          ?
???????????????????????????????????????????????????????????
? 3. Connection Success?                                   ?
?    ??? Yes: Start monitoring (check every 10s)          ?
?    ??? No: Enter Continuous Retry Loop                  ?
???????????????????????????????????????????????????????????
                          ?
???????????????????????????????????????????????????????????
? 4. Continuous Monitoring                                 ?
?    ??? Check connection state every 10 seconds          ?
?    ??? If disconnected, retry immediately               ?
?    ??? Never stop trying                                ?
???????????????????????????????????????????????????????????
```

### Implementation Details

**Location:** `src/Automaton.Runner.Console/Services/HubService.cs`

```csharp
public async Task<bool> ConnectAsync()
{
    // Validate prerequisites
    var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
    if (string.IsNullOrEmpty(apiKey))
    {
        WriteLine("? Not authenticated. Please set an API key first.");
        return false;
    }

    if (!configService.IsRunnerRegistered())
    {
        WriteLine("? Runner not registered. Please register first.");
        return false;
    }

    // Start continuous retry loop in background
    retryTokenSource = new CancellationTokenSource();
    _ = Task.Run(() => ContinuousRetryLoop(apiKey, hubUrl, retryTokenSource.Token));
    
    return true;
}
```

---

## Circuit Breaker Pattern

### Purpose

The circuit breaker prevents **overwhelming the server** with continuous connection attempts during outages. It implements a "fail-fast" mechanism with automatic recovery.

### Circuit States

```
???????????????
?   CLOSED    ?  ? Normal operation, requests pass through
?  (Working)  ?
???????????????
       ? 3 consecutive failures
       ?
???????????????
?    OPEN     ?  ? Requests fail immediately, no attempts
? (Broken)    ?     Duration: 2 minutes
???????????????
       ? After cool-down period
       ?
???????????????
? HALF-OPEN   ?  ? Test with 1 request
?  (Testing)  ?     - Success ? CLOSED
???????????????     - Failure ? OPEN (restart timer)
```

### Configuration

**Location:** `src/Automaton.Runner.Console/Connection/ResilientPolicy.cs`

```csharp
this.resilientConnectionPolicy = new ResilientPolicy(
    retryAfterSeconds: 5,                    // Retry every 5 seconds
    exceptionsAllowedBeforeBreaking: 3,      // Open after 3 failures
    durationOfBreak: TimeSpan.FromMinutes(2) // Cool-down: 2 minutes
);
```

### Timeline Example

For a server outage:

```
Time    Event
?????????????????????????????????????????????????????????????
00:00   Server goes down
00:00   Attempt 1 fails ?
00:05   Attempt 2 fails ? (wait 5s)
00:10   Attempt 3 fails ? (wait 5s)
00:10   ? Circuit breaker OPENS (2 minute cool-down)
00:10   Retry blocked - circuit open
00:30   Retry blocked - circuit open (wait 30s between cycles)
01:00   Retry blocked - circuit open
01:30   Retry blocked - circuit open
02:00   Retry blocked - circuit open
02:10   ? Circuit breaker CLOSES (cool-down complete)
02:10   Attempt 4 - tries to connect
        - If success: Resume normal operation ?
        - If failure: Circuit opens again for another 2 minutes ?
```

### Console Output

```
?? Connecting to https://localhost:7091/api/workflow/hub...
?? API Key: ak_fullaccess_H...
?? Runner ID: a715617d-6181-4cc7-816e-c89d80b19eb0
?? Runner Name: Local
?? Retry attempt 1 in 5 seconds...
?? Retry attempt 2 in 5 seconds...
?? Retry attempt 3 in 5 seconds...
??  Connection paused temporarily due to repeated failures
   The runner will automatically retry after the cool-down period
?? The runner will continue trying to connect automatically...
   Press 'q' to quit or wait for connection to be established.
```

---

## Continuous Retry Loop

### Infinite Duration

The runner implements **true infinite retry** - it will keep trying to connect **indefinitely** until:
- ? Connection succeeds (automatic!)
- ? User manually stops the service (`Ctrl+C`, service stop, etc.)
- ? Service is killed by the system

### Implementation

**Location:** `src/Automaton.Runner.Console/Services/HubService.cs`

```csharp
private async Task ContinuousRetryLoop(string apiKey, string hubUrl, CancellationToken cancellationToken)
{
    var wasDisconnected = false;
    
    while (shouldKeepRetrying && !cancellationToken.IsCancellationRequested)
    {
        try
        {
            // If already connected, monitor every 10 seconds
            if (IsConnected())
            {
                if (wasDisconnected)
                {
                    WriteLine("? Connection established successfully after retries!");
                    wasDisconnected = false;
                    isReconnecting = false;
                }
                
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                continue;
            }
            
            wasDisconnected = true;
            
            // Try to connect using the resilient policy (includes circuit breaker)
            await BuildAndConnectWithRetry(apiKey, hubUrl);
            
            logger.Information("Successfully connected via continuous retry loop");
            WriteLine("? Connection established successfully after retries!");
            
            // Wait before next check
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (Exception ex)
        {
            // Log and wait before next retry
            await HandleConnectionError(ex);
            
            // Wait 30 seconds between retry cycles
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }
    
    logger.Information("Continuous retry loop terminated");
}
```

### Long-Duration Outage Example

**Scenario:** Server down for 12 hours

```
Duration    Cycles    Total Attempts    Approx Log Entries    Service Log Size
????????????????????????????????????????????????????????????????????????????????
1 hour      30        90                90                    ~50KB
6 hours     180       540               540                   ~300KB
12 hours    360       1,080             1,080                 ~600KB
24 hours    720       2,160             2,160                 ~1.2MB
7 days      5,040     15,120            15,120                ~8MB

? Service logs rotate at 10MB (NSSM)
? Database logs cleaned after 31 days (Serilog)
? Memory usage: Stable (no leaks)
? CPU usage: Negligible (just sleeping)
```

**When server returns after 12 hours:**
```
12:00:00 - Server starts
12:00:30 - Next retry cycle (within 30 seconds)
12:00:30 - ? Connection established successfully!
```

---

## Logging Architecture

The Automaton Runner uses a **dual-logging strategy**:

1. **File Logs** - For service operation (stdout/stderr)
2. **Database Logs** - For application events (Serilog ? MariaDB/MySQL)

### Logging Flow

```
???????????????????????????????????????????????????????????
? Runner Application                                       ?
?                                                          ?
?  ??????????????????         ??????????????????         ?
?  ? Console Output ?         ? Serilog Logger ?         ?
?  ? (WriteLine)    ?         ? (Structured)   ?         ?
?  ??????????????????         ??????????????????         ?
?           ?                          ?                  ?
???????????????????????????????????????????????????????????
            ?                          ?
   ???????????????????       ???????????????????
   ? NSSM Service    ?       ? HTTP Sink       ?
   ? File Capture    ?       ? (POST to API)   ?
   ???????????????????       ???????????????????
            ?                          ?
   ???????????????????       ???????????????????
   ? service-output  ?       ? MariaDB/MySQL   ?
   ? .log (10MB max) ?       ? Logs Table      ?
   ? service-error   ?       ? (31 day TTL)    ?
   ? .log (10MB max) ?       ?                 ?
   ???????????????????       ???????????????????
        NSSM Rotation          Database Cleanup
```

### Log Types

| Log Type | Location | Purpose | Rotation |
|----------|----------|---------|----------|
| **Service Output** | `C:\Program Files\AutomatonRunner\logs\service-output.log` | Console stdout (connection status, workflow execution) | 10MB ? `.log.1`, `.log.2`, etc. |
| **Service Error** | `C:\Program Files\AutomatonRunner\logs\service-error.log` | Console stderr (unhandled exceptions, critical errors) | 10MB ? `.log.1`, `.log.2`, etc. |
| **Application Logs** | MySQL `Logs` table | Structured application events (Serilog) | Auto-delete after 31 days |
| **Execution Logs** | MySQL `FlowExecutionLog` table | Workflow execution details | Auto-delete after 31 days |

### Log Levels

```csharp
// File logs (via Console.WriteLine)
WriteLine("?? Connecting to {hubUrl}...");           // Informational
WriteLine("? Connected to server successfully");     // Success
WriteLine("?? Connection lost: {error}");             // Warning
WriteLine("? Connection failed: {error}");           // Error

// Database logs (via Serilog)
logger.Debug("Waiting 30 seconds before next attempt");
logger.Information("Connected to hub at {HubUrl}", hubUrl);
logger.Warning(error, "Connection closed with error");
logger.Error(ex, "Connection attempt failed");
```

---

## Log Rotation

### 1. Service Logs (NSSM) - File Rotation

**Configuration Location:** `scripts/setup-service.ps1` (lines 431-436)

```powershell
# Configure logging with rotation
& $nssmPath set $ServiceName AppStdout "$LogsPath\service-output.log"
& $nssmPath set $ServiceName AppStderr "$LogsPath\service-error.log"
& $nssmPath set $ServiceName AppRotateFiles 1        # Enable rotation
& $nssmPath set $ServiceName AppRotateOnline 1       # Rotate while running
& $nssmPath set $ServiceName AppRotateBytes 10485760 # Max 10MB per file
```

**Behavior:**

```
service-output.log           (current, active)
    ? reaches 10MB
service-output.log.1         (rotated)
    ? next rotation
service-output.log.2         (older)
    ? next rotation
service-output.log.3         (oldest)
```

**Example Timeline:**

```
Day 1:  service-output.log grows to 10MB
        ? Rotation triggered
        ? Renamed to service-output.log.1
        ? New service-output.log created

Day 3:  service-output.log grows to 10MB again
        ? service-output.log.1 ? service-output.log.2
        ? service-output.log ? service-output.log.1
        ? New service-output.log created

Result: Multiple rotated copies kept (configurable)
```

### 2. Application Logs (Serilog) - Time-Based Cleanup

**Configuration Location:** `src/Automaton.Studio.Server/Program.cs`

```csharp
.WriteTo.MariaDB(
    connectionString: configurationManager.GetConnectionString("MySqlConnection"),
    tableName: "Logs",
    autoCreateTable: false,
    useBulkInsert: true,
    restrictedToMinimumLevel: LogEventLevel.Information,
    batchPostingLimit: 1000,
    period: TimeSpan.FromSeconds(30),
    options: new Serilog.Sinks.MariaDB.MariaDBSinkOptions
    {
        PropertiesToColumnsMapping = new Dictionary<string, string>
        {
            { "Exception", "Exception" },
            { "Level", "Level" },
            { "Message", "Message" },
            { "MessageTemplate", "MessageTemplate" },
            { "Properties", "Properties" },
            { "Timestamp", "Timestamp" },
            { "EventType", "EventType" },
            { "UserName", "UserName" }
        },
        TimestampInUtc = true,
        ExcludePropertiesWithDedicatedColumn = false,
        EnumsAsInts = false,
        LogRecordsCleanupFrequency = TimeSpan.FromHours(2),  // Check every 2 hours
        LogRecordsExpiration = TimeSpan.FromDays(31)        // Delete after 31 days
    })
```

**Behavior:**

```
Every 2 hours:
    ??? Query: DELETE FROM Logs WHERE Timestamp < (NOW() - 31 days)
    ??? Automatically removes old entries

Database space reclaimed automatically
No manual cleanup required
```

### 3. Log Size Calculations

**12-Hour Server Outage:**

| Log Type | Entries | Entry Size | Total Size | Notes |
|----------|---------|------------|------------|-------|
| Service Output | ~1,080 | ~500 bytes | ~540KB | Console output |
| Service Error | ~100 | ~500 bytes | ~50KB | Exception details |
| Database Logs | ~1,080 | ~500 bytes | ~540KB | Serilog structured logs |
| **Total** | | | **~1.13MB** | Well under 10MB rotation threshold |

**31-Day Retention (Normal Operation):**

| Log Type | Daily Entries | 31 Days | Approx Size | Notes |
|----------|---------------|---------|-------------|-------|
| Database Logs | ~2,000 | 62,000 | ~31MB | Auto-deleted after 31 days |
| Service Logs | ~2,000 | 62,000 | Rotated at 10MB | Multiple `.log.X` files |

---

## Monitoring & Troubleshooting

### Viewing Logs

#### Windows Service Logs

```powershell
# View service output (real-time)
Get-Content "C:\Program Files\AutomatonRunner\logs\service-output.log" -Tail 50 -Wait

# View service errors
Get-Content "C:\Program Files\AutomatonRunner\logs\service-error.log" -Tail 50

# View rotated logs
Get-Content "C:\Program Files\AutomatonRunner\logs\service-output.log.1" -Tail 50

# Check service status
Get-Service AutomatonRunner
Get-Service AutomatonRunner | Format-List *
```

#### Database Logs

```sql
-- View recent logs
SELECT 
    Timestamp, 
    Level, 
    Message, 
    Exception
FROM Logs
WHERE Timestamp >= DATE_SUB(NOW(), INTERVAL 1 HOUR)
ORDER BY Timestamp DESC
LIMIT 100;

-- View connection-related logs
SELECT 
    Timestamp, 
    Level, 
    Message
FROM Logs
WHERE Message LIKE '%connection%' OR Message LIKE '%retry%'
ORDER BY Timestamp DESC
LIMIT 50;

-- Check log count and oldest entry
SELECT 
    COUNT(*) as TotalLogs,
    MIN(Timestamp) as OldestLog,
    MAX(Timestamp) as NewestLog
FROM Logs;
```

### Common Log Patterns

#### Successful Connection

```
?? Connecting to https://localhost:7091/api/workflow/hub...
?? API Key: ak_fullaccess_H...
?? Runner ID: a715617d-6181-4cc7-816e-c89d80b19eb0
?? Runner Name: Local
? Connected to server successfully
```

**Database Log:**
```
[Information] Connected to hub at https://localhost:7091/api/workflow/hub
```

#### Connection Failure with Retry

```
? Connection failed: Server connection refused
?? Connection refused - this usually means:
   � The Automaton server is not running
   � The server URL is incorrect
   � A firewall is blocking the connection

?? Troubleshooting steps:
   � Check if the server is running at: https://localhost:7091
   � Verify the server URL is correct
   � Check network connectivity and firewall settings

?? The runner will continue trying to connect automatically...
   Press 'q' to quit or wait for connection to be established.
```

**Database Log:**
```
[Error] Connection attempt failed. Server: https://localhost:7091, HubUrl: https://localhost:7091/api/workflow/hub, Error: HttpRequestException
```

#### Circuit Breaker Open

```
?? Retry attempt 1 in 5 seconds...
?? Retry attempt 2 in 5 seconds...
?? Retry attempt 3 in 5 seconds...
??  Connection paused temporarily due to repeated failures
   The runner will automatically retry after the cool-down period
```

**Database Log:**
```
[Warning] Connection circuit breaker opened for 00:02:00 due to repeated failures. Error: HttpRequestException
```

#### Successful Reconnection

```
? Connection established successfully after retries!
? Connected to server successfully
```

**Database Log:**
```
[Information] Successfully connected via continuous retry loop
[Information] Connected to hub at https://localhost:7091/api/workflow/hub
```

#### Connection Lost (SignalR Reconnect)

```
?? Connection lost: Server connection refused
?? Reconnecting to server: Server connection refused
? Reconnected to server successfully
```

**Database Log:**
```
[Warning] Connection closed with error during operation
[Information] Connection reconnecting due to error
[Information] Reconnected to server with connection ID: xxx
```

---

## Configuration Options

### Circuit Breaker Settings

**Location:** `src/Automaton.Runner.Console/Services/HubService.cs`

```csharp
this.resilientConnectionPolicy = new ResilientPolicy(
    retryAfterSeconds: 5,                    // Adjust retry interval
    exceptionsAllowedBeforeBreaking: 3,      // Failures before opening circuit
    durationOfBreak: TimeSpan.FromMinutes(2) // Cool-down period
);
```

**Recommendations:**

| Scenario | retryAfterSeconds | exceptionsAllowed | durationOfBreak |
|----------|-------------------|-------------------|-----------------|
| **Development** | 3 | 2 | 1 minute |
| **Production** | 5 | 3 | 2 minutes |
| **High-Latency Networks** | 10 | 5 | 5 minutes |
| **Strict Server Protection** | 5 | 2 | 5 minutes |

### Continuous Retry Settings

**Location:** `src/Automaton.Runner.Console/Services/HubService.cs`

```csharp
// Monitoring interval (when connected)
await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

// Retry cycle interval (when disconnected)
await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
```

**Customization:**

```csharp
// More frequent monitoring
await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

// Less aggressive retry (reduce server load)
await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
```

### Log Rotation Settings

**Windows Service (NSSM):**

```powershell
# Change max file size (e.g., 50MB)
& $nssmPath set $ServiceName AppRotateBytes 52428800

# Disable rotation (not recommended)
& $nssmPath set $ServiceName AppRotateFiles 0

# Configure number of rotated files to keep (not directly supported by NSSM)
# Manual cleanup script required
```

**Database Logs (Serilog):**

```csharp
// Change retention period (e.g., 90 days)
LogRecordsExpiration = TimeSpan.FromDays(90)

// Change cleanup frequency (e.g., daily)
LogRecordsCleanupFrequency = TimeSpan.FromDays(1)

// Increase batch size for high-volume scenarios
batchPostingLimit: 5000,
period: TimeSpan.FromMinutes(1)
```

---

## Best Practices

### Production Deployment

1. **? Use Default Circuit Breaker Settings**
   - Proven to work for most scenarios
   - Balances responsiveness and server protection

2. **? Monitor Service Logs Regularly**
   ```powershell
   # Set up monitoring script
   while ($true) {
       $lastLine = Get-Content "C:\Program Files\AutomatonRunner\logs\service-output.log" -Tail 1
       if ($lastLine -like "*?*" -or $lastLine -like "*??*") {
           Write-Host "WARNING: Connection issues detected" -ForegroundColor Red
           # Send alert/notification
       }
       Start-Sleep 60
   }
   ```

3. **? Configure Database Log Retention**
   - Development: 7 days
   - Production: 31-90 days
   - Compliance: Based on requirements

4. **? Set Up Log Archival**
   ```powershell
   # Archive rotated logs
   $archivePath = "\\server\logs\automaton-runner-archive"
   Get-ChildItem "C:\Program Files\AutomatonRunner\logs\*.log.*" | 
       Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-7) } |
       Move-Item -Destination $archivePath
   ```

5. **? Test Failure Scenarios**
   - Stop the Automaton Studio server
   - Verify runner keeps retrying
   - Start server and confirm automatic reconnection
   - Check logs for proper behavior

### Troubleshooting Checklist

**Runner won't connect:**

- [ ] Check service is running: `Get-Service AutomatonRunner`
- [ ] Verify API key is set: Check environment variable `AUTOMATON_API_KEY`
- [ ] Confirm server is accessible: `Test-NetConnection localhost -Port 7091`
- [ ] Review service output log for errors
- [ ] Check database logs for authentication failures

**Runner connects but disconnects frequently:**

- [ ] Check network stability
- [ ] Review server logs for errors
- [ ] Verify server has sufficient resources
- [ ] Check for firewall/proxy issues
- [ ] Review SignalR hub configuration

**Logs growing too fast:**

- [ ] Check for excessive error logging
- [ ] Review retry frequency (may be too aggressive)
- [ ] Increase log rotation threshold
- [ ] Reduce database log retention period
- [ ] Implement log archival

---

## Summary

The Automaton Runner implements **enterprise-grade resilience** with:

? **Infinite Retry** - Never stops trying to connect  
? **Circuit Breaker** - Prevents server overload (3 failures ? 2 min cool-down)  
? **Smart Backoff** - 30-second delays between retry cycles  
? **Automatic Log Rotation** - Service logs rotate at 10MB  
? **Database Log Cleanup** - Auto-delete after 31 days  
? **Zero Manual Intervention** - Fully automatic recovery  

**For a 12-hour server outage:**
- Runner makes ~1,080 connection attempts
- Service logs grow by ~600KB (well under 10MB limit)
- Database logs grow by ~540KB (auto-deleted after 31 days)
- **When server returns: Automatic reconnection within 30 seconds** ?

**The runner is production-ready and requires zero maintenance!** ??

---

## Additional Resources

- **Service Setup Guide:** `scripts/README.md`
- **Configuration Reference:** `src/Automaton.Runner.Console/appsettings.json`
- **Circuit Breaker Implementation:** `src/Automaton.Runner.Console/Connection/ResilientPolicy.cs`
- **Logging Configuration:** `src/Automaton.Studio.Server/Program.cs`
- **NSSM Documentation:** https://nssm.cc/usage
- **Serilog Documentation:** https://serilog.net/

---

**Document Version:** 1.0  
**Last Updated:** 2024  
**Maintained By:** Automaton Team
