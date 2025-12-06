# Phase 3: Unified AI System Prompt

## Overview

Create a unified system prompt that enables the AI to generate both frontend (HTML/CSS/JS) and backend (Custom Steps) code in a single interaction. The AI will understand when backend logic is needed and automatically create the necessary Custom Steps.

**Estimated Effort:** 3-4 days

---

## Goal

Enable users to describe their complete app in natural language, and the AI will:
1. Generate frontend HTML/CSS/JavaScript
2. Identify when backend logic is needed
3. Create Custom Steps for backend functionality
4. Generate proper `executeStep()` calls in the frontend

**Example Interaction:**

```
User: "Create a form that sends emails using SendGrid API"

AI Actions:
1. Creates Custom Step: "SendEmailStep" (Python)
   - Input variables: to, subject, body, api_key
   - Code that calls SendGrid API
   
2. Creates Frontend HTML:
   - Email form with to, subject, body fields
   - Submit button with onclick handler
   - automatonApi.executeStep('SendEmailStep', {...}) call
   - Success/error feedback display

3. Links step to app automatically
```

---

## Components to Create/Modify

### 1. Create `UnifiedAppSystemPromptBuilder.cs`

**Location:** `src/Automaton.Studio/Services/Mcp/SystemPrompts/UnifiedAppSystemPromptBuilder.cs`

```csharp
namespace Automaton.Studio.Services.Mcp.SystemPrompts;

/// <summary>
/// Service for building unified AI system prompts that generate both frontend and backend code
/// </summary>
public class UnifiedAppSystemPromptBuilder : IUnifiedAppSystemPromptBuilder
{
    private readonly IStepSystemPromptBuilder stepPromptBuilder;

    public UnifiedAppSystemPromptBuilder(IStepSystemPromptBuilder stepPromptBuilder)
    {
        this.stepPromptBuilder = stepPromptBuilder;
    }

    /// <summary>
    /// Build a unified system prompt for combined frontend + backend generation
    /// </summary>
    public string BuildSystemPrompt()
    {
        return """
            You are an expert full-stack developer with access to execution tools. You can generate complete web applications with both frontend (HTML/CSS/JavaScript) and backend (Custom Steps in Python/Batch).

            ## YOUR CAPABILITIES:
            1. **Frontend Generation**: Create complete HTML/CSS/JavaScript web pages
            2. **Backend Generation**: Create Custom Steps (Python/Batch scripts) for server-side logic
            3. **Integration**: Wire frontend to backend using automatonApi.executeStep()

            ## CRITICAL WORKFLOW RULE:
            ? **ANALYZE THE REQUEST FOR BACKEND NEEDS** ?
            Before generating code, determine if the user's request requires backend logic:
            
            **Backend IS NEEDED when:**
            - API calls to external services (SendGrid, Stripe, databases, etc.)
            - Server-side data processing or validation
            - Sensitive operations (API keys, credentials)
            - Complex computations that should run on server
            - File operations or system commands
            
            **Backend IS NOT NEEDED when:**
            - Pure UI/display components
            - Client-side only interactions
            - Static content pages
            - Simple form validation (client-side)

            ## MANDATORY WORKFLOW FOR APPS WITH BACKEND:

            ### Step 1: Plan the Backend Steps
            When backend is needed, first determine:
            - What backend operations are required?
            - What inputs does each operation need?
            - What outputs should each operation return?
            
            ### Step 2: Create Backend Steps (BEFORE Frontend)
            For each backend operation:
            1. Call **create_backend_step** with step name and language
            2. Call **save_step_code** with the implementation
            3. Call **suggest_step_variables** to define inputs/outputs
            4. Call **link_step_to_app** to connect it to the current app

            ### Step 3: Generate Frontend
            After backend steps are created:
            1. Generate HTML/CSS/JavaScript that uses automatonApi.executeStep()
            2. Include proper loading states and error handling
            3. Display output variables meaningfully

            ## BACKEND STEP GUIDELINES:

            ### Step Naming Convention:
            - Use PascalCase: `SendEmailStep`, `ProcessPaymentStep`, `FetchDataStep`
            - Name should clearly indicate the action
            - Add "Step" suffix for clarity

            ### Step Variables:
            **Input Variables** (what the step receives):
            - Use snake_case: `api_key`, `email_to`, `message_body`
            - Mark sensitive inputs (passwords, API keys) as IsSensitive
            - Add descriptions for each variable

            **Output Variables** (what the step returns):
            - Use snake_case: `message_id`, `status`, `result_data`
            - Always include a `success` boolean output
            - Include error information outputs for failure cases

            ### Step Code Best Practices:
            ```python
            # Initialize outputs with defaults
            success = False
            error_message = ""
            result_data = ""

            try:
                # Access inputs directly (they are injected as globals)
                print(f"INFO: Processing request for {input_variable}")
                
                # Your logic here
                # ...
                
                # Set success outputs
                success = True
                result_data = "processed result"
                
            except Exception as e:
                print(f"ERROR: {str(e)}")
                error_message = str(e)
                # success remains False, result_data remains empty
            ```

            ## FRONTEND INTEGRATION PATTERN:

            When calling backend steps from frontend:

            ```javascript
            async function handleSubmit() {
                const submitBtn = document.getElementById('submitBtn');
                const statusDiv = document.getElementById('status');
                
                // Show loading state
                submitBtn.disabled = true;
                submitBtn.textContent = 'Processing...';
                statusDiv.innerHTML = '<span class="loading">? Working...</span>';
                
                try {
                    const result = await automatonApi.executeStep('YourStepName', {
                        inputs: {
                            input_var1: document.getElementById('field1').value,
                            input_var2: document.getElementById('field2').value
                        }
                    });
                    
                    if (result.success && result.outputVariables?.success) {
                        statusDiv.innerHTML = '<span class="success">? ' + 
                            (result.outputVariables.result_data || 'Success!') + '</span>';
                    } else {
                        const errorMsg = result.outputVariables?.error_message || result.error || 'Unknown error';
                        statusDiv.innerHTML = '<span class="error">? ' + errorMsg + '</span>';
                    }
                } catch (error) {
                    statusDiv.innerHTML = '<span class="error">? ' + error.message + '</span>';
                } finally {
                    submitBtn.disabled = false;
                    submitBtn.textContent = 'Submit';
                }
            }
            ```

            ## AVAILABLE TOOLS:

            ### Frontend Tools:
            - **get_current_code**: ?? Read existing HTML/CSS/JavaScript
            - **save_app_code**: ?? Save frontend code to the app

            ### Backend Tools:
            - **create_backend_step**: ?? Create a new Custom Step
            - **get_step_code**: ?? Read existing step code
            - **save_step_code**: ?? Save code to a step
            - **suggest_step_variables**: ? Define input/output variables
            - **execute_step_code**: ?? Test step execution
            - **link_step_to_app**: ?? Link step to current app

            ### Context Tools:
            - **get_linked_steps**: ?? List steps already linked to this app

            ## EXAMPLE GENERATION FLOW:

            User: "Create a contact form that sends emails via SendGrid"

            Your Actions:
            1. **create_backend_step**: name="SendEmailStep", language="Python"
            2. **suggest_step_variables**: 
               - Inputs: api_key (sensitive), to_email, subject, message
               - Outputs: success, message_id, error_message
            3. **save_step_code**: Python code that calls SendGrid API
            4. **link_step_to_app**: Connect SendEmailStep to this app
            5. **save_app_code**: HTML form + JavaScript that calls executeStep

            ## RESPONSE FORMAT:

            After generating, provide a brief summary:
            ```
            ? Created:
            - Backend Step: SendEmailStep (Python)
              - Inputs: api_key, to_email, subject, message
              - Outputs: success, message_id, error_message
            - Frontend: Contact form with email validation and send button
            
            ?? To use:
            1. Set the api_key input variable in the step's settings
            2. Preview the app to test the form
            ```

            ## IMPORTANT RULES:

            1. **ALWAYS create backend steps BEFORE generating frontend that uses them**
            2. **ALWAYS use snake_case for variable names**
            3. **ALWAYS include error handling in both frontend and backend**
            4. **ALWAYS mark sensitive inputs (API keys, passwords) as IsSensitive**
            5. **NEVER hardcode API keys or credentials in code**
            6. **NEVER leave loading states hanging - always reset in finally block**
            
            ## WHEN USER ASKS TO MODIFY:
            
            If user asks to "improve", "modify", or "fix" existing code:
            1. First call get_current_code AND get_linked_steps
            2. Understand what already exists
            3. Make incremental improvements
            4. Preserve existing backend steps unless explicitly asked to change them

            ## REMEMBER:
            ? Analyze request for backend needs FIRST
            ? Create backend steps BEFORE frontend
            ? Wire everything together with executeStep()
            ? Include comprehensive error handling
            ? Keep explanations brief - code is already saved
            """.Trim();
    }
}

/// <summary>
/// Interface for unified app system prompt builder
/// </summary>
public interface IUnifiedAppSystemPromptBuilder
{
    string BuildSystemPrompt();
}
```

---

### 2. Create `UnifiedAppAiChatContext.cs`

**Location:** `src/Automaton.Studio/Components/AiChat/Contexts/UnifiedAppAiChatContext.cs`

```csharp
namespace Automaton.Studio.Components.AiChat.Contexts;

/// <summary>
/// AI Chat context for unified App + Backend Step generation
/// </summary>
public class UnifiedAppAiChatContext : IAiChatContext
{
    private readonly IUnifiedAppSystemPromptBuilder systemPromptBuilder;
    private readonly string appId;
    private readonly Func<Task<string>> getCurrentCodeFunc;
    private readonly Func<Task<List<LinkedStepInfo>>> getLinkedStepsFunc;

    public UnifiedAppAiChatContext(
        IUnifiedAppSystemPromptBuilder systemPromptBuilder,
        string appId,
        Func<Task<string>> getCurrentCodeFunc,
        Func<Task<List<LinkedStepInfo>>> getLinkedStepsFunc)
    {
        this.systemPromptBuilder = systemPromptBuilder;
        this.appId = appId;
        this.getCurrentCodeFunc = getCurrentCodeFunc;
        this.getLinkedStepsFunc = getLinkedStepsFunc;
    }

    public string GetSystemPrompt()
    {
        return systemPromptBuilder.BuildSystemPrompt();
    }

    public async Task<string> GetContextInfo()
    {
        var linkedSteps = await getLinkedStepsFunc();
        
        if (!linkedSteps.Any())
        {
            return $"App ID: {appId}\nNo backend steps linked yet.";
        }

        var stepsInfo = string.Join("\n", linkedSteps.Select(s => 
            $"- {s.DisplayName} ({s.Language}): {s.Description ?? "No description"}"));

        return $"""
            App ID: {appId}
            
            Linked Backend Steps:
            {stepsInfo}
            """;
    }

    public string ContextType => "UnifiedApp";
    public string ContextId => appId;
}
```

---

### 3. Update `AppPage.razor.cs` to Use Unified Context

Modify the `CreateAiChatContext` method:

```csharp
// Add injection
[Inject] private IUnifiedAppSystemPromptBuilder UnifiedSystemPromptBuilder { get; set; }

// Update method
private IAiChatContext CreateAiChatContext()
{
    // Use unified context that supports both frontend and backend generation
    return new UnifiedAppAiChatContext(
        UnifiedSystemPromptBuilder,
        AppId,
        GetCurrentCodeForAI,
        GetLinkedStepsForAI
    );
}

private async Task<List<LinkedStepInfo>> GetLinkedStepsForAI()
{
    try
    {
        return await AppStepsService.GetLinkedStepsAsync(currentApp.Id);
    }
    catch
    {
        return new List<LinkedStepInfo>();
    }
}
```

---

### 4. Register Services in DI

**Location:** `src/Automaton.Studio/Extensions/ServiceCollectionExtensions.cs`

```csharp
// Add unified system prompt builder
services.AddScoped<IUnifiedAppSystemPromptBuilder, UnifiedAppSystemPromptBuilder>();
```

---

## Pattern Recognition Guide

The AI should recognize these patterns and create appropriate backend steps:

### Pattern 1: External API Integration
**Keywords:** "SendGrid", "Stripe", "API", "fetch from", "call", "integrate with"

```yaml
Detection: User mentions external service
Action: Create backend step for API call
Example: "send email via SendGrid" ? Create SendEmailStep
```

### Pattern 2: Data Processing
**Keywords:** "process", "transform", "calculate", "analyze", "parse"

```yaml
Detection: User needs server-side computation
Action: Create backend step for processing
Example: "analyze CSV data" ? Create ProcessDataStep
```

### Pattern 3: Database Operations
**Keywords:** "save", "store", "retrieve", "database", "record"

```yaml
Detection: User needs data persistence
Action: Create backend step for DB operations
Example: "store user preferences" ? Create SavePreferencesStep
```

### Pattern 4: File Operations
**Keywords:** "upload", "download", "file", "export", "generate PDF"

```yaml
Detection: User needs file handling
Action: Create backend step for file operations
Example: "generate PDF report" ? Create GeneratePdfStep
```

### Pattern 5: Authentication/Security
**Keywords:** "login", "authenticate", "verify", "secure", "token"

```yaml
Detection: User needs auth operations
Action: Create backend step with sensitive handling
Example: "verify user credentials" ? Create AuthenticateStep
```

---

## Testing Scenarios

### Scenario 1: Simple API Integration
```
Input: "Create a weather app that shows current weather for a city"
Expected Output:
- Backend: FetchWeatherStep (Python) with city input, weather_data output
- Frontend: City input + display weather + executeStep call
```

### Scenario 2: Form with Backend
```
Input: "Create a contact form that sends emails"
Expected Output:
- Backend: SendEmailStep (Python) with to, subject, body, api_key inputs
- Frontend: Contact form + success/error display + executeStep call
```

### Scenario 3: Data Processing
```
Input: "Create a page where users can upload CSV and get statistics"
Expected Output:
- Backend: ProcessCsvStep (Python) with csv_data input, stats output
- Frontend: File upload + display stats + executeStep call
```

### Scenario 4: No Backend Needed
```
Input: "Create a beautiful landing page for a coffee shop"
Expected Output:
- No backend steps created
- Frontend: Full landing page HTML/CSS/JS
```

---

## Error Handling in Prompts

The system prompt instructs the AI to handle errors gracefully:

### Backend Error Handling
```python
# Always initialize outputs
success = False
error_message = ""

try:
    # Main logic
    success = True
except Exception as e:
    error_message = str(e)
```

### Frontend Error Handling
```javascript
try {
    const result = await automatonApi.executeStep('StepName', {...});
    if (result.success && result.outputVariables?.success) {
        // Handle success
    } else {
        // Handle backend failure
    }
} catch (error) {
    // Handle network/system failure
} finally {
    // Reset UI state
}
```

---

## Next Steps

After Phase 3 is complete:
1. Proceed to [Phase 4: MCP Tools Enhancement](./04-phase4-mcp-tools.md)
2. This adds the actual tools the AI will use to create steps
