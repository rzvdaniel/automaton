# Phase 6: Flow Generation for Complex Scenarios

## Overview

This phase extends the unified App generation to support **automatic Flow creation** for complex multi-step scenarios. While Phases 1-5 enable direct step execution for simple cases, Phase 6 allows the AI to orchestrate multiple steps into Flows when the user's requirements demand it.

**Status:** Future Enhancement (after Phases 1-5)  
**Estimated Effort:** 5-7 days  
**Prerequisites:** Phases 1-5 complete

---

## Architecture Progression

```
???????????????????????????????????????????????????????????????????????????
?                     APP BACKEND COMPLEXITY SPECTRUM                     ?
???????????????????????????????????????????????????????????????????????????
?                                                                         ?
?  SIMPLE (Phase 1-5)              ?  COMPLEX (Phase 6)                   ?
?  ????????????????????            ?  ??????????????????                  ?
?                                  ?                                      ?
?  App ? Step ? Runner             ?  App ? Flow ? Steps ? Runner         ?
?                                  ?                                      ?
?  • Single API call               ?  • Multiple sequential operations    ?
?  • One data operation            ?  • Conditional branching             ?
?  • Direct response               ?  • Error handling & rollbacks        ?
?  • No dependencies               ?  • Data passing between steps        ?
?                                  ?                                      ?
?  Example:                        ?  Example:                            ?
?  "Send an email"                 ?  "Register user, send welcome email, ?
?  ? SendEmailStep                 ?   and create audit log"              ?
?                                  ?  ? UserRegistrationFlow              ?
?                                  ?     ??? CreateUserStep               ?
?                                  ?     ??? SendWelcomeEmailStep         ?
?                                  ?     ??? CreateAuditLogStep           ?
?                                                                         ?
???????????????????????????????????????????????????????????????????????????
```

---

## When to Use Steps vs Flows

### Use Direct Steps When:
- Single operation (one API call, one DB operation)
- No dependencies between operations
- Simple input ? output transformation
- No need for error recovery/rollback

### Use Flows When:
- Multiple operations must succeed together
- Operations depend on each other's output
- Need conditional logic or branching
- Require transactional behavior (all-or-nothing)
- Complex error handling with partial rollbacks

---

## User Experience Goal

```
User: "Create a user registration form that saves the user to database, 
       sends a welcome email, and logs the activity"

AI Analysis:
  ? Multiple operations detected: 3
  ? Operations have dependencies (email needs user ID)
  ? Should be transactional (user shouldn't be created if email fails)
  ? Recommendation: Generate Flow with 3 Steps

AI Actions:
  1. Create Step: CreateUserStep (Python)
  2. Create Step: SendWelcomeEmailStep (Python)  
  3. Create Step: CreateAuditLogStep (Python)
  4. Create Flow: UserRegistrationFlow
  5. Wire steps in flow with variable passing
  6. Generate frontend with automatonApi.executeFlow()
  7. Link flow to app

Result:
  - 3 Custom Steps created and linked
  - 1 Flow created with proper orchestration
  - Frontend form with loading states and error handling
  - User sees everything in Backend Actions tab
```

---

## Components to Create/Modify

### 1. Enhanced Decision Logic in System Prompt

The AI needs to decide: **Step or Flow?**

```
## COMPLEXITY ANALYSIS RULES:

When analyzing the user's request, determine the backend approach:

**COUNT DISTINCT OPERATIONS:**
- Each API call to external service = 1 operation
- Each database write = 1 operation  
- Each notification (email, SMS, webhook) = 1 operation
- Each file operation = 1 operation

**DECISION MATRIX:**

| Operations | Dependencies | Transactional | ? Use     |
|------------|--------------|---------------|-----------|
| 1          | N/A          | N/A           | Step      |
| 2+         | None         | No            | Steps     |
| 2+         | Yes          | No            | Steps     |
| 2+         | Yes          | Yes           | Flow      |
| 2+         | None         | Yes           | Flow      |

**DEPENDENCY CHECK:**
- Does Operation B need output from Operation A? ? Dependency exists
- Must all operations succeed or all fail? ? Transactional required

**EXAMPLES:**
- "Send email" ? 1 op ? Step
- "Save data AND send email (independent)" ? 2 ops, no deps ? 2 Steps
- "Create user, then send email with user ID" ? 2 ops, deps ? Flow
- "Process payment, update inventory, send receipt" ? 3 ops, deps, transactional ? Flow
```

---

### 2. New MCP Tools for Flow Creation

Add these tools to `UnifiedAppMcpClient`:

```csharp
// New tools for Flow generation
CreateTool("create_flow", "Create a new Flow for multi-step orchestration",
    new { 
        name = "string: Flow name in PascalCase",
        description = "string: Description of what the flow does"
    }),

CreateTool("add_step_to_flow", "Add a Custom Step to a Flow",
    new {
        flow_name = "string: Name of the flow",
        step_name = "string: Name of the step to add",
        position = "number: Position in the flow (0-based)",
        input_mappings = "object: Map flow inputs to step inputs",
        output_mappings = "object: Map step outputs to flow variables"
    }),

CreateTool("configure_flow_variables", "Define Flow input/output variables",
    new {
        flow_name = "string: Name of the flow",
        input_variables = "array: Flow-level input variables",
        output_variables = "array: Flow-level output variables"
    }),

CreateTool("link_flow_to_app", "Link a Flow to the current app",
    new { flow_name = "string: Name of the flow to link" }),

CreateTool("get_linked_flows", "Get Flows linked to this app")
```

---

### 3. Flow Creation Handler

```csharp
private async Task<ToolResponse> HandleCreateFlow(Dictionary<string, object> arguments)
{
    var name = GetStringArgument(arguments, "name");
    var description = GetStringArgument(arguments, "description", "");

    if (string.IsNullOrEmpty(name))
    {
        return CreateErrorResponse("Flow name is required");
    }

    try
    {
        // Create the flow via FlowsService
        var flow = new Flow
        {
            Name = name,
            Description = description,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            StartDefinition = new FlowDefinition()
        };

        var flowId = await flowsService.Create(flow);

        // Track the created flow for this session
        createdFlows[name] = new FlowContext
        {
            FlowId = flowId,
            Name = name,
            Steps = new List<string>()
        };

        logger.LogInformation("Created flow: {FlowName} (ID: {FlowId})", name, flowId);

        return CreateSuccessResponse($"Created flow '{name}' (ID: {flowId})");
    }
    catch (Exception ex)
    {
        return CreateErrorResponse($"Failed to create flow: {ex.Message}");
    }
}

private async Task<ToolResponse> HandleAddStepToFlow(Dictionary<string, object> arguments)
{
    var flowName = GetStringArgument(arguments, "flow_name");
    var stepName = GetStringArgument(arguments, "step_name");
    var position = GetIntArgument(arguments, "position", -1);

    var flow = await ResolveFlowAsync(flowName);
    if (flow == null)
    {
        return CreateErrorResponse($"Flow '{flowName}' not found");
    }

    var step = await ResolveStepAsync(stepName);
    if (step == null)
    {
        return CreateErrorResponse($"Step '{stepName}' not found");
    }

    // Add step to flow definition
    // This involves:
    // 1. Creating a StudioStep instance from the CustomStep
    // 2. Adding it to the flow's StartDefinition
    // 3. Wiring up NextStepId for sequencing
    // 4. Setting up input/output variable mappings

    // ... implementation details ...

    return CreateSuccessResponse($"Added step '{stepName}' to flow '{flowName}' at position {position}");
}
```

---

### 4. Extended System Prompt for Flow Generation

```csharp
public string BuildSystemPromptWithFlowSupport()
{
    return """
        // ... existing prompt content ...

        ## FLOW GENERATION (For Complex Scenarios)

        When multiple dependent operations are needed, create a Flow:

        ### Step 1: Analyze Complexity
        - Count distinct operations
        - Identify dependencies between operations
        - Determine if transactional behavior is needed

        ### Step 2: Create Individual Steps First
        For each operation:
        1. create_backend_step with appropriate name
        2. suggest_step_variables for inputs/outputs
        3. save_step_code with implementation

        ### Step 3: Create the Flow
        1. create_flow with descriptive name
        2. configure_flow_variables for flow-level I/O
        3. For each step in order:
           - add_step_to_flow with position and mappings

        ### Step 4: Wire Variable Mappings
        ```
        Flow Input (user_email) 
            ? Step 1 Input (email)
        
        Step 1 Output (user_id) 
            ? Step 2 Input (user_id)
        
        Step 2 Output (email_sent) 
            ? Flow Output (success)
        ```

        ### Step 5: Generate Frontend
        Use automatonApi.executeFlow() instead of executeStep():
        ```javascript
        const result = await automatonApi.executeFlow('UserRegistrationFlow', {
            inputs: { user_email: email, user_name: name }
        });
        ```

        ### Step 6: Link Flow to App
        - link_flow_to_app to connect
        - Flow appears in Backend Actions tab

        ## FLOW VARIABLE MAPPING PATTERNS

        ### Sequential Data Passing
        ```
        CreateUserStep.output.user_id ? SendEmailStep.input.recipient_id
        ```

        ### Flow Input Distribution
        ```
        Flow.input.api_key ? Step1.input.api_key
        Flow.input.api_key ? Step2.input.api_key
        ```

        ### Output Aggregation
        ```
        Step1.output.user_id   ? Flow.output.user_id
        Step2.output.email_id  ? Flow.output.email_id
        Step3.output.log_id    ? Flow.output.log_id
        ```

        ## EXAMPLE: User Registration Flow

        User: "Create registration that saves user, sends welcome email, logs activity"

        Your Actions:

        1. **Analyze**: 3 operations, dependencies exist (email needs user_id)
           ? Decision: Create Flow

        2. **Create Steps**:
           ```
           create_backend_step: CreateUserStep
           suggest_step_variables:
             inputs: [name, email, password_hash]
             outputs: [success, user_id, error_message]
           save_step_code: <Python to insert user in DB>

           create_backend_step: SendWelcomeEmailStep  
           suggest_step_variables:
             inputs: [user_id, email, api_key]
             outputs: [success, email_id, error_message]
           save_step_code: <Python to send email via SendGrid>

           create_backend_step: CreateAuditLogStep
           suggest_step_variables:
             inputs: [user_id, action, timestamp]
             outputs: [success, log_id]
           save_step_code: <Python to create audit entry>
           ```

        3. **Create Flow**:
           ```
           create_flow: UserRegistrationFlow
           configure_flow_variables:
             inputs: [name, email, password, api_key]
             outputs: [success, user_id, email_id, log_id, error_message]
           ```

        4. **Wire Steps**:
           ```
           add_step_to_flow: CreateUserStep at position 0
             input_mappings: {name: flow.name, email: flow.email, password_hash: flow.password}
             output_mappings: {user_id: flow.user_id}

           add_step_to_flow: SendWelcomeEmailStep at position 1
             input_mappings: {user_id: flow.user_id, email: flow.email, api_key: flow.api_key}
             output_mappings: {email_id: flow.email_id}

           add_step_to_flow: CreateAuditLogStep at position 2
             input_mappings: {user_id: flow.user_id, action: "USER_REGISTERED"}
             output_mappings: {log_id: flow.log_id}
           ```

        5. **Link to App**:
           ```
           link_flow_to_app: UserRegistrationFlow
           ```

        6. **Generate Frontend** with executeFlow()
        """.Trim();
}
```

---

### 5. Backend Actions Tab Update

Extend to show both Steps and Flows:

```razor
<TabPane Tab="Backend Actions" Key="backend">
    <div class="backend-actions-container">
        
        @* Steps Section *@
        <Card Class="backend-card" Title="Linked Steps" Bordered="true">
            @* ... existing steps list ... *@
        </Card>

        @* Flows Section (NEW) *@
        <Card Class="backend-card" Title="Linked Flows" Bordered="true" Style="margin-top: 24px;">
            <Extra>
                <Space>
                    <SpaceItem>
                        <Button Icon="plus" Type="@ButtonType.Primary" OnClick="ShowLinkFlowDialog">
                            Link Flow
                        </Button>
                    </SpaceItem>
                    <SpaceItem>
                        <Button Icon="apartment" OnClick="CreateNewFlow">
                            Create Flow
                        </Button>
                    </SpaceItem>
                </Space>
            </Extra>
            <ChildContent>
                @if (!linkedFlows.Any())
                {
                    <Empty Description="No flows linked. Use flows for complex multi-step operations.">
                        <ChildContent>
                            <Text Type="TextElementType.Secondary">
                                Flows orchestrate multiple steps with data passing and error handling.
                            </Text>
                        </ChildContent>
                    </Empty>
                }
                else
                {
                    @foreach (var flow in linkedFlows)
                    {
                        <Card Class="flow-card" Size="small">
                            <ChildContent>
                                <div class="flow-card-content">
                                    <div class="flow-info">
                                        <Icon Type="apartment" Style="font-size: 24px; color: #722ed1;" />
                                        <div class="flow-details">
                                            <Text Strong>@flow.Name</Text>
                                            <div class="flow-meta">
                                                <Tag Color="purple">@flow.StepCount steps</Tag>
                                            </div>
                                            @if (!string.IsNullOrEmpty(flow.Description))
                                            {
                                                <Text Type="TextElementType.Secondary">@flow.Description</Text>
                                            }
                                        </div>
                                    </div>
                                    <div class="flow-actions">
                                        <Button Icon="edit" Size="@ButtonSize.Small" 
                                                OnClick="() => NavigateToFlow(flow.Id)">
                                            Edit
                                        </Button>
                                        <Button Icon="copy" Size="@ButtonSize.Small"
                                                OnClick="() => CopyFlowCode(flow)">
                                        </Button>
                                    </div>
                                </div>
                            </ChildContent>
                        </Card>
                    }
                }
                
                <Alert Type="@AlertType.Info" ShowIcon="true" Style="margin-top: 16px;">
                    <MessageTemplate>
                        <div>
                            <Text Strong>Execute flows from your app:</Text>
                            <pre class="code-example">
const result = await automatonApi.executeFlow('FlowName', {
    inputs: { key: 'value' }
});</pre>
                        </div>
                    </MessageTemplate>
                </Alert>
            </ChildContent>
        </Card>
        
    </div>
</TabPane>
```

---

## Data Model Extensions

### AppFlow Entity

**Location:** `src/Automaton.Studio.Server/Entities/AppFlow.cs`

```csharp
namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Links a Flow to a Web App
/// </summary>
public class AppFlow
{
    public Guid AppId { get; set; }
    public Guid FlowId { get; set; }
    public DateTime LinkedAt { get; set; }
    public Guid LinkedByUserId { get; set; }
    public int DisplayOrder { get; set; }
    
    // Navigation properties
    public virtual App App { get; set; }
    public virtual Flow Flow { get; set; }
}
```

### Migration

```csharp
migrationBuilder.CreateTable(
    name: "AppFlows",
    columns: table => new
    {
        AppId = table.Column<Guid>(nullable: false),
        FlowId = table.Column<Guid>(nullable: false),
        LinkedAt = table.Column<DateTime>(nullable: false),
        LinkedByUserId = table.Column<Guid>(nullable: false),
        DisplayOrder = table.Column<int>(nullable: false, defaultValue: 0)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_AppFlows", x => new { x.AppId, x.FlowId });
        // Foreign keys...
    });
```

---

## API Extensions

### Link Flow to App

```http
POST /api/apps/{appId}/flows/{flowId}/link
```

### Get Linked Flows

```http
GET /api/apps/{appId}/flows
```

### Execute Flow from App

```http
POST /api/web-apps/{appSlug}/flows/{flowIdentifier}/execute
```

(This already exists from the original implementation)

---

## Testing Scenarios

### Scenario 1: Simple Multi-Step (No Dependencies)

```
Input: "Create a page that saves feedback AND sends notification email"
Expected: 2 separate Steps (can run independently)
- SaveFeedbackStep
- SendNotificationStep
Frontend calls both via executeStep()
```

### Scenario 2: Dependent Multi-Step

```
Input: "Register user then send welcome email with their user ID"
Expected: 1 Flow with 2 Steps
- CreateUserStep (outputs user_id)
- SendWelcomeEmailStep (receives user_id)
Frontend calls executeFlow()
```

### Scenario 3: Complex Transactional

```
Input: "Process order: validate inventory, charge payment, update stock, send confirmation"
Expected: 1 Flow with 4 Steps
- ValidateInventoryStep
- ProcessPaymentStep  
- UpdateStockStep
- SendConfirmationStep
With proper error handling at each step
```

---

## Future Enhancements (Post-Phase 6)

### Phase 7: Conditional Flows
- Add branching based on step outputs
- If/else logic in flows
- Error handling branches

### Phase 8: Parallel Execution
- Run independent steps in parallel
- Join results before continuing

### Phase 9: Flow Templates
- Pre-built flow templates (User Registration, Order Processing, etc.)
- One-click flow creation from templates

---

## Success Criteria

1. AI correctly identifies when Flow is needed vs Steps
2. Multi-step flows execute correctly with data passing
3. Errors in one step properly handled
4. Frontend integration works with executeFlow()
5. Backend Actions tab shows both Steps and Flows
6. Users can manually create flows for edge cases
