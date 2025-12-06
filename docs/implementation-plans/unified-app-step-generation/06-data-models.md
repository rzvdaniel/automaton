# Data Models & Database Changes

## Overview

This document details the database schema changes and data models required to support the unified App + Backend Step generation feature.

---

## New Entity: AppCustomStep

This junction table tracks the relationship between Apps and Custom Steps.

### Entity Definition

**Location:** `src/Automaton.Studio.Server/Entities/AppCustomStep.cs`

```csharp
namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Links a Custom Step to a Web App
/// </summary>
public class AppCustomStep
{
    /// <summary>
    /// The App that this step is linked to
    /// </summary>
    public Guid AppId { get; set; }
    
    /// <summary>
    /// The Custom Step that is linked
    /// </summary>
    public Guid CustomStepId { get; set; }
    
    /// <summary>
    /// When the link was created
    /// </summary>
    public DateTime LinkedAt { get; set; }
    
    /// <summary>
    /// User who created the link
    /// </summary>
    public Guid LinkedByUserId { get; set; }
    
    /// <summary>
    /// Optional: Order of the step in the app (for UI display)
    /// </summary>
    public int DisplayOrder { get; set; }
    
    // Navigation properties
    public virtual App App { get; set; }
    public virtual CustomStep CustomStep { get; set; }
}
```

---

## Database Migration

### Migration File

**Location:** `src/Automaton.Studio.Server/Data/Migrations/YYYYMMDD_AddAppCustomSteps.cs`

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Data.Migrations;

public partial class AddAppCustomSteps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppCustomSteps",
            columns: table => new
            {
                AppId = table.Column<Guid>(type: "TEXT", nullable: false),
                CustomStepId = table.Column<Guid>(type: "TEXT", nullable: false),
                LinkedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                LinkedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppCustomSteps", x => new { x.AppId, x.CustomStepId });
                table.ForeignKey(
                    name: "FK_AppCustomSteps_Apps_AppId",
                    column: x => x.AppId,
                    principalTable: "Apps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AppCustomSteps_CustomSteps_CustomStepId",
                    column: x => x.CustomStepId,
                    principalTable: "CustomSteps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AppCustomSteps_CustomStepId",
            table: "AppCustomSteps",
            column: "CustomStepId");

        migrationBuilder.CreateIndex(
            name: "IX_AppCustomSteps_AppId_DisplayOrder",
            table: "AppCustomSteps",
            columns: new[] { "AppId", "DisplayOrder" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppCustomSteps");
    }
}
```

---

## Update ApplicationDbContext

**Location:** `src/Automaton.Studio.Server/Data/ApplicationDbContext.cs`

```csharp
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    // ... existing DbSets ...
    
    /// <summary>
    /// App-CustomStep relationships
    /// </summary>
    public DbSet<AppCustomStep> AppCustomSteps { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // ... existing configurations ...

        // AppCustomStep configuration
        modelBuilder.Entity<AppCustomStep>(entity =>
        {
            entity.HasKey(e => new { e.AppId, e.CustomStepId });
            
            entity.HasIndex(e => e.CustomStepId)
                .HasDatabaseName("IX_AppCustomSteps_CustomStepId");
            
            entity.HasIndex(e => new { e.AppId, e.DisplayOrder })
                .HasDatabaseName("IX_AppCustomSteps_AppId_DisplayOrder");

            entity.Property(e => e.LinkedAt)
                .IsRequired();
            
            entity.Property(e => e.LinkedByUserId)
                .IsRequired();
            
            entity.Property(e => e.DisplayOrder)
                .HasDefaultValue(0);

            entity.HasOne(e => e.App)
                .WithMany(a => a.AppCustomSteps)
                .HasForeignKey(e => e.AppId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CustomStep)
                .WithMany(cs => cs.AppCustomSteps)
                .HasForeignKey(e => e.CustomStepId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

---

## Update Existing Entities

### App Entity Update

**Location:** `src/Automaton.Studio.Server/Entities/App.cs`

```csharp
namespace Automaton.Studio.Server.Entities;

public class App
{
    // ... existing properties ...
    
    /// <summary>
    /// Custom Steps linked to this app
    /// </summary>
    public virtual ICollection<AppCustomStep> AppCustomSteps { get; set; } = new List<AppCustomStep>();
}
```

### CustomStep Entity Update

**Location:** `src/Automaton.Studio.Server/Entities/CustomStep.cs`

```csharp
namespace Automaton.Studio.Server.Entities;

public class CustomStep
{
    // ... existing properties ...
    
    /// <summary>
    /// Apps this step is linked to
    /// </summary>
    public virtual ICollection<AppCustomStep> AppCustomSteps { get; set; } = new List<AppCustomStep>();
}
```

---

## Updated Models

### AppModel (Server)

**Location:** `src/Automaton.Studio.Server/Models/App.cs`

```csharp
namespace Automaton.Studio.Server.Models;

public class AppModel
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; }
    public bool RequiresAuthentication { get; set; } = true;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public Guid? ExecutionRunnerId { get; set; }
    
    /// <summary>
    /// Owner user ID (for execution context)
    /// </summary>
    public Guid OwnerId { get; set; }
    
    // Appearance configuration
    public AppAppearance Appearance { get; set; } = new AppAppearance();
    
    public AppDefinition Definition { get; set; } = new AppDefinition();
    
    /// <summary>
    /// IDs of Custom Steps linked to this app
    /// </summary>
    public List<Guid> LinkedStepIds { get; set; } = new();
}
```

### LinkedStepInfo (Shared Model)

**Location:** `src/Automaton.Studio.Server/Models/LinkedStepInfo.cs`

```csharp
namespace Automaton.Studio.Server.Models;

/// <summary>
/// Information about a Custom Step linked to an App
/// </summary>
public class LinkedStepInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Language { get; set; } = "Python";
    public int Version { get; set; } = 1;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public DateTime? LinkedAt { get; set; }
    public int DisplayOrder { get; set; }
    
    // Variable counts
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    
    // Detailed variable info
    public List<StepVariableInfo> InputVariables { get; set; } = new();
    public List<StepVariableInfo> OutputVariables { get; set; } = new();
}

/// <summary>
/// Basic variable information
/// </summary>
public class StepVariableInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = "Text";
    public bool IsSensitive { get; set; }
}
```

### WebAppStepExecutionRequest

**Location:** `src/Automaton.Studio.Server/Models/WebAppStepExecution.cs`

```csharp
namespace Automaton.Studio.Server.Models;

/// <summary>
/// Request to execute a Custom Step from a Web App
/// </summary>
public class WebAppStepExecutionRequest
{
    /// <summary>
    /// Input variables as key-value pairs
    /// </summary>
    public Dictionary<string, string>? Inputs { get; set; }

    /// <summary>
    /// Optional list of runner names to execute on
    /// </summary>
    public List<string>? RunnerNames { get; set; }
}

/// <summary>
/// Response from Custom Step execution
/// </summary>
public class WebAppStepExecutionResponse
{
    public bool Success { get; set; }
    public Guid StepId { get; set; }
    public string? StepName { get; set; }
    public string Status { get; set; } = "Unknown";
    public int RunnerCount { get; set; }
    public DateTime ExecutedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    
    /// <summary>
    /// Output variables from step execution
    /// </summary>
    public Dictionary<string, object>? OutputVariables { get; set; }
    
    /// <summary>
    /// Execution logs
    /// </summary>
    public List<string>? Logs { get; set; }
}
```

---

## Frontend Models

### LinkedStepInfo (Frontend)

**Location:** `src/Automaton.Studio/Models/LinkedStepInfo.cs`

```csharp
namespace Automaton.Studio.Models;

/// <summary>
/// Information about a Custom Step linked to an App (frontend model)
/// </summary>
public class LinkedStepInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Language { get; set; } = "Python";
    public int Version { get; set; } = 1;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public DateTime? LinkedAt { get; set; }
    public int DisplayOrder { get; set; }
    
    // Computed properties for UI
    public bool HasInputVariables => InputCount > 0;
    public bool HasOutputVariables => OutputCount > 0;
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    
    public List<StepVariableInfo> InputVariables { get; set; } = new();
    public List<StepVariableInfo> OutputVariables { get; set; } = new();
}

public class StepVariableInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = "Text";
    public bool IsSensitive { get; set; }
}
```

---

## Database Schema Diagram

```
???????????????????????         ???????????????????????
?        Apps         ?         ?    CustomSteps      ?
???????????????????????         ???????????????????????
? Id (PK)             ?         ? Id (PK)             ?
? Name                ?         ? Name                ?
? DisplayName         ?         ? DisplayName         ?
? Description         ?         ? Description         ?
? Definition (JSON)   ?         ? Definition (JSON)   ?
? IsPublished         ?         ? Language            ?
? PublicUrl           ?         ? Category            ?
? ExecutionRunnerId   ?         ? Icon                ?
? ...                 ?         ? ...                 ?
???????????????????????         ???????????????????????
          ?                               ?
          ?                               ?
          ?    ???????????????????????    ?
          ?    ?   AppCustomSteps    ?    ?
          ?    ???????????????????????    ?
          ?????? AppId (PK, FK)      ??????
               ? CustomStepId (PK,FK)?
               ? LinkedAt            ?
               ? LinkedByUserId      ?
               ? DisplayOrder        ?
               ???????????????????????
```

---

## Migration Commands

```bash
# Generate migration
cd src/Automaton.Studio.Server
dotnet ef migrations add AddAppCustomSteps --output-dir Data/Migrations

# Apply migration
dotnet ef database update

# Rollback if needed
dotnet ef database update PreviousMigrationName
```

---

## Data Integrity Rules

1. **Cascade Delete (App)**: When an App is deleted, all AppCustomStep links are deleted
2. **Cascade Delete (Step)**: When a CustomStep is deleted, all AppCustomStep links are deleted
3. **No Orphan Links**: Links cannot exist without both App and CustomStep
4. **Unique Links**: Only one link between an App and CustomStep (composite PK)
5. **Owner Consistency**: Step and App should have the same owner for linking

---

## Query Examples

### Get Steps Linked to an App

```csharp
var linkedSteps = await dbContext.AppCustomSteps
    .Where(acs => acs.AppId == appId)
    .Include(acs => acs.CustomStep)
    .OrderBy(acs => acs.DisplayOrder)
    .Select(acs => new LinkedStepInfo
    {
        Id = acs.CustomStep.Id,
        Name = acs.CustomStep.Name,
        // ... map other properties
    })
    .ToListAsync();
```

### Get Apps Using a Step

```csharp
var appsUsingStep = await dbContext.AppCustomSteps
    .Where(acs => acs.CustomStepId == stepId)
    .Include(acs => acs.App)
    .Select(acs => acs.App)
    .ToListAsync();
```

### Check if Step is Linked to Any App

```csharp
var isLinked = await dbContext.AppCustomSteps
    .AnyAsync(acs => acs.CustomStepId == stepId);
```

---

## Backward Compatibility

- Existing Apps without linked steps continue to work
- Existing Steps without app links continue to work
- No changes to existing API contracts
- New fields are optional/nullable where appropriate
