# App Controller Execution Architecture

## Current Step Execution Model

The runner **does not load steps from the database directly**. Instead:

1. **Server builds payload**: `AppsStepExecutionService` or `CustomStepsService` fetches the step definition from the database.
2. **Server sends code via SignalR**: The complete step code + metadata is sent in the `CustomStepPayload` to the runner.
3. **Runner executes locally**: `CustomStepExecutor` runs the code using the script engine.
4. **Results returned**: Execution results are sent back via SignalR.

**Key files involved:**
- `src/Automaton.Studio.Server/Services/AppsStepExecutionService.cs` — resolves step, builds payload, sends to runner
- `src/Automaton.Runner/Services/CustomStepExecuteService.cs` — receives payload, executes code
- `src/Automaton.Core/Scripting/Step/CustomStepExecutor.cs` — unified executor for both Studio and Runner

## Design Decision: AppController Storage and Execution

We have **two architectural options** for storing and executing `AppController`s:

### Option A: Server-Fetches-and-Sends (Recommended)

**Flow:**
1. AppController code stored in `backend/` or `api/` folder within app's file system (via `AppFileStorageService`).
2. When frontend calls `executeController('Users', 'create', {...})`, it calls the backend API.
3. Backend API handler (e.g., `AppsControllerExecutionService`) loads the controller code from the file system.
4. Server builds a payload similar to `CustomStepPayload` and sends the code to the runner via SignalR.
5. Runner executes the action method and returns results.

**Advantages:**
- ✅ Aligns with current execution model (server controls what runs on runner).
- ✅ Security: server can audit/encrypt sensitive code before sending.
- ✅ No filesystem access required on runner.
- ✅ Code changes don't require runner restart.
- ✅ Supports multi-runner scenarios (code sent to any available runner).

**Disadvantages:**
- ⚠️ Slightly higher latency (file load + SignalR send).
- ⚠️ Larger payloads if controller code is large.


## Recommended Implementation: Option A

### 1. New Entity: `AppController`

```csharp
// Database entity
public class AppController
{
    public Guid Id { get; set; }
    public Guid WebAppId { get; set; }  // FK to WebApp
    public string Name { get; set; }     // e.g., "Users", "Products"
    public string Language { get; set; } // e.g., "Python", "PowerShell"
    public string Code { get; set; }     // Full controller code
    public List<ControllerAction> Actions { get; set; } // Metadata for available actions
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
}

public class ControllerAction
{
    public string Name { get; set; }                      // e.g., "create", "read", "update"
    public List<ControllerActionParameter> Parameters { get; set; }
    public List<ControllerActionOutput> Outputs { get; set; }
}
```

### 2. File System Storage

Controllers stored alongside app UI:
```
/AppData/apps/{userId}/{appId}/
├─ index.html
├─ styles/
│  └─ main.css
├─ js/
│  └─ app.js
└─ backend/
   ├─ users-controller.py
   ├─ products-controller.py
   └─ shared-utils.py
```

### 3. New Service: `AppsControllerExecutionService`

Similar to `AppsStepExecutionService`, but for controllers:

```csharp
public class AppsControllerExecutionService
{
    public async Task<AppControllerActionResponse> ExecuteActionAsync(
        PublishedAppInfo app,
        string controllerName,
        string actionName,
        Dictionary<string, string> inputs,
        List<string>? runnerNames)
    {
        // 1. Load controller from file system (or db for now)
        var controller = await LoadControllerAsync(app.Id, controllerName);
        
        // 2. Validate action exists and inputs match
        ValidateAction(controller, actionName, inputs);
        
        // 3. Build payload with full controller code
        var payload = new AppControllerActionPayload
        {
            ControllerId = controller.Id,
            ControllerName = controllerName,
            ActionName = actionName,
            Code = controller.Code,
            Language = controller.Language,
            Inputs = inputs,
            Action = controller.Actions.FirstOrDefault(a => a.Name == actionName)
        };
        
        // 4. Execute on runners
        return await ExecuteOnRunnersAsync(app, payload, runnerNames);
    }
}
```

### 4. Runner Handler

New SignalR method `RunAppControllerAction`:

```csharp
// In HubService
connection.On<AppControllerActionPayload, AppControllerActionResponse>(
    "RunAppControllerAction", 
    (payload) => ExecuteAppControllerActionHandler(payload));

// Handler in runner
private async Task<AppControllerActionResponse> ExecuteAppControllerActionHandler(
    AppControllerActionPayload payload)
{
    // Execute similar to CustomStepExecuteService but:
    // 1. Pass actionName to script engine
    // 2. Engine calls the specific action method
    // 3. Return action-specific outputs
    
    var executor = new AppControllerActionExecutor(scriptHost);
    return await executor.ExecuteAsync(payload);
}
```

### 5. Frontend Integration

```typescript
// From app UI
const result = await automatonApi.executeController({
    controllerName: 'Users',
    actionName: 'create',
    inputs: {
        name: 'John',
        email: 'john@example.com'
    }
});

// Alternative fluent API
const result = await automatonApi
    .controller('Users')
    .action('create')
    .execute({ name: 'John', email: 'john@example.com' });
```

## Security Considerations

1. **Code Ownership**: Only the app owner can execute their controllers.
2. **Input Validation**: Server validates inputs against action schema before sending to runner.
3. **Sensitive Variables**: Like CustomSteps, support encrypted/sensitive variables in controller code.
4. **Audit Logging**: Log all controller action executions with inputs/outputs.
5. **Execution Context**: Runner has no direct access to controller files; server controls what code runs.

## Migration Path

**Phase 1 (Current)**: Introduce `AppController` entity and file storage (backend folder).  
**Phase 2**: Implement `AppsControllerExecutionService` for SignalR invocation.  
**Phase 3**: Update runner to handle `RunAppControllerAction` method.  
**Phase 4**: AI prompts for controller-based app architecture (Phase 3 from main plan).

## Why Not Store as Multiple Steps?

A single `AppController` with multiple actions is better than N separate `CustomStep`s because:

1. **Encapsulation**: Related CRUD operations are grouped logically.
2. **Shared Context**: The controller can maintain state/context across actions (e.g., database connections, config).
3. **Flow Cleanliness**: The step library remains a collection of reusable, single-purpose building blocks.
4. **Large App Clarity**: Developers see "Users Controller" not "CreateUserStep", "UpdateUserStep", "DeleteUserStep", etc.
5. **Execution Efficiency**: One payload, one execution path instead of multiple step lookups.
