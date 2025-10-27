# Web App Preview & Publishing - Implementation Plan

## Overview
This document outlines the implementation plan for adding preview functionality and a publishing mechanism for web apps in Automaton Studio. This feature will allow users to create, preview, publish, and share HTML/CSS/JavaScript web applications with backend integration to Automaton workflows.

---

## ?? Goals

1. **Preview**: Allow designers to preview HTML/CSS/JS in real-time while editing
2. **Publish**: Enable users to publish apps and make them accessible via public URLs
3. **Access**: Allow external users to access published apps without authentication
4. **Execution**: Safely execute user-generated HTML/CSS/JS content
5. **Backend Integration**: Connect web apps to Automaton workflows for backend functionality

---

## ?? Implementation Phases

### Phase 1: Preview Functionality (Immediate Priority)

#### 1.1 Preview Architecture Decision

**Selected Approach: Iframe with `srcdoc` attribute**

**Pros:**
- ? Isolated execution environment (security sandbox)
- ? Real browser rendering without navigation
- ? Can inject content dynamically without server round-trip
- ? Sandboxing capabilities for user-generated content
- ? Industry best practice (CodePen, JSFiddle, StackBlitz use this)

**Cons:**
- ?? Same-origin policy restrictions (solved with `allow-same-origin` sandbox)
- ?? Requires content injection strategy

**Alternative Approaches Considered:**
- ? **Popup Window**: Popup blockers, harder lifecycle management
- ? **Separate Page**: Requires server round-trip, state management complexity
- ? **Direct DOM Injection**: Security risks, DOM pollution

#### 1.2 Preview Implementation Details

**File Changes Required:**

1. **`src/Automaton.Studio/Pages/Apps/AppDesignerPage.razor`**
   - Add new "Preview" tab in the Tabs component
   - Implement preview toolbar (Refresh, Open in New Tab, View Published)
   - Add iframe with `srcdoc` for live preview
   - Style preview container for responsive display

2. **`src/Automaton.Studio/Pages/Apps/AppDesignerPage.razor.cs`**
   - Add `GetPreviewHtml()` method to retrieve current code
   - Add `InjectAutomatonBridge()` method to inject workflow API
   - Add `RefreshPreview()` method for manual refresh
   - Add `OpenPreviewInNewTab()` method to open in separate tab
   - Add `OpenPublishedApp()` method (if published)

**Preview Refresh Strategies:**
- **Manual Refresh**: Button in toolbar
- **Auto-refresh**: On save action (later: debounced auto-refresh on edit)
- **New Tab Preview**: For full-screen testing

**Code Structure:**

```razor
<!-- Preview Tab Structure -->
<TabPane Tab="Preview" Key="preview">
    <div class="preview-container">
   <div class="preview-toolbar">
   <Space>
        <SpaceItem>
      <Button Icon="reload" OnClick="RefreshPreview">Refresh</Button>
       </SpaceItem>
   <SpaceItem>
<Button Icon="fullscreen" OnClick="OpenPreviewInNewTab">
  Open in New Tab
  </Button>
                </SpaceItem>
@if (currentApp.IsPublished)
     {
            <SpaceItem>
   <Button Icon="eye" Type="@ButtonType.Primary" 
   OnClick="OpenPublishedApp">
    View Published
</Button>
          </SpaceItem>
           }
            </Space>
        </div>
        <iframe id="app-preview-frame" 
      srcdoc="@GetPreviewHtml()" 
       sandbox="allow-scripts allow-same-origin allow-modals allow-forms"
    class="preview-iframe">
  </iframe>
    </div>
</TabPane>
```

**Iframe Sandbox Permissions:**
- `allow-scripts`: Execute JavaScript code
- `allow-same-origin`: Access parent APIs (Automaton bridge)
- `allow-modals`: Display alerts, confirms, prompts
- `allow-forms`: Submit forms
- ? **NOT** `allow-top-navigation`: Prevent navigation hijacking
- ? **NOT** `allow-popups`: Prevent unwanted popups

**Automaton Bridge Injection:**

The preview will automatically inject an `Automaton` object into the web app's JavaScript context, allowing designers to test workflow calls:

```javascript
window.Automaton = {
    executeFlow: async function(flowId, inputs) {
        console.log('Preview: executeFlow called', flowId, inputs);
        // In preview mode, return mock response
 return { 
  success: true, 
      message: 'Preview mode - flow not executed',
  data: null 
     };
    }
};
```

---

### Phase 2: Publishing Mechanism

#### 2.1 Database Schema Updates

**File:** `src/Automaton.Studio.Server/Entities/WebApp.cs`

**Changes Required:**

```csharp
public class WebApp
{
    public Guid Id { get; set; }
  public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string Definition { get; set; } // JSON: AppDefinition serialized
    
  // Publishing fields (NEW)
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; } // URL slug (e.g., "my-app-name")
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; } = 1; // Track version history
    
    // Access control (FUTURE)
    public bool RequiresAuthentication { get; set; } = false;
    public string? AllowedRoles { get; set; } // JSON array for future

    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    
    public virtual ICollection<WebAppUser> WebAppUsers { get; set; }
}
```

**Migration Required:** Yes
- Add columns: `PublishedAt`, `Version`
- Update existing rows with default values

**Migration Script:**
```sql
ALTER TABLE WebApps ADD PublishedAt DATETIME NULL;
ALTER TABLE WebApps ADD Version INT NOT NULL DEFAULT 1;
```

#### 2.2 URL Structure

**Published Apps URL Pattern:**

```
Internal (Designer):
https://yourdomain.com/appdesigner/{guid}

Public (Published):
https://yourdomain.com/web/{slug}

Examples:
https://yourdomain.com/web/my-todo-app
https://yourdomain.com/web/customer-portal
https://yourdomain.com/web/contact-form
```

**Benefits:**
- Clean, memorable URLs
- SEO-friendly slugs
- Clear separation from internal/designer URLs
- Easy sharing and bookmarking

**Slug Generation Rules:**
- Convert display name to lowercase
- Replace spaces with hyphens
- Remove special characters (keep alphanumeric and hyphens)
- Ensure uniqueness (append number if conflict: `my-app-2`)
- Example: "My Todo App!" ? "my-todo-app"

#### 2.3 Publishing Flow

**User Journey:**

```
[Designer] ? [Publish Button] ? [Validation] ? [Generate Slug] ? [Update DB] ? [Published ?]
```

**Validation Steps:**

1. **Required Fields Check**
   - Name is not empty
   - HtmlContent is not empty
   - DisplayName is not empty

2. **HTML Validation** (Optional, Basic)
   - Check for basic HTML structure (optional)
 - Warn if no `<html>` or `<body>` tags (non-blocking)

3. **Slug Generation**
   - Generate slug from `DisplayName`
   - Check uniqueness in database
   - Append number if conflict detected

4. **Database Update**
   - Set `IsPublished = true`
   - Set `PublicUrl = generatedSlug`
   - Set `PublishedAt = DateTime.UtcNow`
   - Increment `Version++`

5. **Success Notification**
   - Show toast notification with public URL
   - Provide "Copy Link" button
   - Show "View Published" button

**Files to Modify:**

1. **`src/Automaton.Studio.Server/Controllers/AppsController.cs`**
   - Add `PublishAsync(Guid id)` endpoint
   - Add `UnpublishAsync(Guid id)` endpoint
   - Add slug uniqueness validation

2. **`src/Automaton.Studio/Services/AppsService.cs`**
   - Add `PublishAsync(Guid id)` method
   - Add `UnpublishAsync(Guid id)` method
   - Add `GenerateUniqueSlug(string displayName)` helper

3. **`src/Automaton.Studio/Pages/Apps/AppsPage.razor`**
   - Add "Publish" button in action column
   - Add "Published" badge/tag
   - Add "Open" button for published apps
   - Add "Unpublish" option

4. **`src/Automaton.Studio/Pages/Apps/AppDesignerPage.razor`**
   - Add publish status indicator in header
   - Add "Publish" / "Unpublish" button in toolbar
   - Show public URL when published

---

### Phase 3: Public Access Controller

#### 3.1 Create Public Web Apps API

**File:** `src/Automaton.Studio.Server/Controllers/PublicWebAppsController.cs` (NEW)

**Purpose:** Serve published web apps to external users without authentication

**Endpoints:**

```csharp
[ApiController]
[Route("api/web")]
[AllowAnonymous] // No authentication required
public class PublicWebAppsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PublicWebAppsController> _logger;

    /// <summary>
    /// Get published web app by slug
    /// </summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublishedApp(string slug)
  {
  // 1. Query published app by slug
      var app = await _context.WebApps
            .Where(w => w.IsPublished && w.PublicUrl == slug)
            .Select(w => new
            {
       w.Id,
            w.Name,
   w.DisplayName,
    w.Description,
   w.Definition,
      w.PublishedAt
   })
            .FirstOrDefaultAsync();

        if (app == null)
            return NotFound(new { message = "App not found or not published" });

   // 2. Deserialize definition
        var definition = JsonSerializer.Deserialize<AppDefinition>(app.Definition);

        // 3. Return app data
 return Ok(new
        {
          app.Id,
  app.Name,
            app.DisplayName,
    app.Description,
      HtmlContent = definition.HtmlContent,
Settings = definition.Settings,
          PublishedAt = app.PublishedAt
});
    }
}
```

**Response Format:**

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "my-todo-app",
  "displayName": "My Todo App",
  "description": "A simple todo list application",
  "htmlContent": "<!DOCTYPE html>...",
  "settings": {
    "enablePwa": false,
    "pwaName": null,
 "themeColor": "#1890ff"
  },
  "publishedAt": "2024-01-15T10:30:00Z"
}
```

#### 3.2 Create Web App Viewer Page

**File:** `src/Automaton.Studio/Pages/WebAppViewer.razor` (NEW)

**Purpose:** Blazor page that loads and renders published web apps in an isolated iframe

**Key Features:**
- No authentication required (uses `[AllowAnonymous]`)
- Empty layout (full-screen app display)
- Loads app content via API
- Injects Automaton workflow bridge
- Error handling for not found / unpublished apps

**Structure:**

```razor
@page "/web/{slug}"
@layout EmptyLayout
@attribute [AllowAnonymous]
@inject HttpClient Http
@inject NavigationManager Navigation

<div class="web-app-container">
@if (loading)
 {
        <div class="loading">
            <Spin Size="large" />
            <p>Loading app...</p>
        </div>
    }
    else if (error != null)
    {
        <Result Status="404"
                Title="App Not Found"
           SubTitle="@error">
       <Extra>
         <Button Type="@ButtonType.Primary" OnClick="GoHome">
         Go Home
  </Button>
            </Extra>
     </Result>
    }
    else
    {
        <iframe id="web-app-frame" 
       srcdoc="@ProcessedHtmlContent" 
          sandbox="allow-scripts allow-forms allow-same-origin allow-modals"
       class="web-app-iframe">
  </iframe>
    }
</div>
```

**Code Behind:**

```csharp
@code {
    [Parameter] public string Slug { get; set; }
    
    private bool loading = true;
    private string? error;
    private string ProcessedHtmlContent = string.Empty;
    
    protected override async Task OnInitializedAsync()
    {
        try
 {
   var response = await Http.GetFromJsonAsync<WebAppResponse>($"api/web/{Slug}");
            
            if (response != null)
          {
              ProcessedHtmlContent = InjectAutomatonBridge(response.HtmlContent);
       }
        }
     catch (HttpRequestException ex)
        {
  error = "This app does not exist or has not been published.";
     }
        finally
    {
            loading = false;
        }
    }
    
    private string InjectAutomatonBridge(string htmlContent)
    {
      var bridge = @"
        <script>
            window.Automaton = {
       executeFlow: async function(flowId, inputs) {
       try {
const response = await fetch('/api/flows/execute', {
       method: 'POST',
       headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ flowId, inputs })
     });
      
     if (!response.ok) {
           throw new Error('Flow execution failed');
   }
              
   return await response.json();
    } catch (error) {
            console.error('Automaton.executeFlow error:', error);
    throw error;
 }
      }
            };
        </script>";
  
        // Insert before </head> if exists, otherwise prepend to content
      if (htmlContent.Contains("</head>", StringComparison.OrdinalIgnoreCase))
 {
            return htmlContent.Replace("</head>", $"{bridge}</head>", StringComparison.OrdinalIgnoreCase);
        }
      
        return bridge + htmlContent;
    }
    
    private void GoHome()
    {
        Navigation.NavigateTo("/");
    }
}
```

**Required Layout:**

**File:** `src/Automaton.Studio/Shared/EmptyLayout.razor` (NEW)

```razor
@inherits LayoutComponentBase

<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Automaton App</title>
    <style>
        * {
            margin: 0;
       padding: 0;
    box-sizing: border-box;
        }
     
        html, body {
            height: 100%;
        width: 100%;
 overflow: hidden;
        }
  </style>
</head>
<body>
    @Body
</body>
</html>
```

---

### Phase 4: Backend Integration (Workflow Execution)

#### 4.1 Flow Execution API

**File:** `src/Automaton.Studio.Server/Controllers/FlowExecutionController.cs` (NEW)

**Purpose:** Allow published web apps to execute Automaton workflows

**Security Considerations:**
- Rate limiting per IP
- Optional API key authentication (future)
- Workflow must be marked as "Published" or "Public"
- Input validation and sanitization

**Endpoint:**

```csharp
[ApiController]
[Route("api/flows")]
[AllowAnonymous] // Public access for published apps
public class FlowExecutionController : ControllerBase
{
    private readonly IFlowService _flowService;
    private readonly ILogger<FlowExecutionController> _logger;

    [HttpPost("execute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
 [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ExecuteFlow(
  [FromBody] FlowExecutionRequest request)
    {
        // 1. Validate request
        if (string.IsNullOrEmpty(request.FlowId))
  return BadRequest(new { message = "FlowId is required" });

        // 2. Check if flow exists and is published
        var flow = await _flowService.GetPublishedFlowAsync(
            Guid.Parse(request.FlowId));
  
        if (flow == null)
            return NotFound(new { message = "Flow not found or not published" });

// 3. Rate limiting check (implement with MemoryCache or Redis)
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!await _rateLimiter.AllowRequest(clientIp))
         return StatusCode(429, new { message = "Too many requests" });

      try
        {
        // 4. Execute flow with inputs
   var result = await _flowService.ExecuteAsync(flow, request.Inputs);

            // 5. Return result
     return Ok(new
       {
         success = result.IsSuccess,
      data = result.OutputVariables,
       message = result.Message,
     executionTime = result.ExecutionTime
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing flow {FlowId}", request.FlowId);
            return StatusCode(500, new { message = "Flow execution failed" });
   }
    }
}

public record FlowExecutionRequest
{
    public string FlowId { get; init; }
    public Dictionary<string, object> Inputs { get; init; } = new();
}
```

#### 4.2 Client-Side API (Automaton Bridge)

**Injected into all published web apps automatically:**

```javascript
/**
 * Automaton JavaScript Bridge
 * Automatically injected into all published web apps
 * Provides API to execute Automaton workflows from client-side code
 */
window.Automaton = {
    /**
     * Execute an Automaton workflow
     * @param {string} flowId - The GUID of the published flow
     * @param {object} inputs - Input variables for the flow
     * @returns {Promise<object>} Flow execution result
     */
    executeFlow: async function(flowId, inputs = {}) {
      try {
       const response = await fetch('/api/flows/execute', {
      method: 'POST',
        headers: {
'Content-Type': 'application/json'
       },
    body: JSON.stringify({
 flowId: flowId,
            inputs: inputs
      })
            });
       
      if (!response.ok) {
 const error = await response.json();
       throw new Error(error.message || 'Flow execution failed');
   }
         
    const result = await response.json();
            return result;
        } catch (error) {
       console.error('Automaton.executeFlow error:', error);
            throw error;
    }
    },
    
    /**
     * Get version information
     */
    version: '1.0.0'
};
```

#### 4.3 Example Web App with Backend Integration

**Example: Contact Form with Email Workflow**

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Contact Form</title>
    <style>
        body {
            font-family: Arial, sans-serif;
         max-width: 600px;
       margin: 50px auto;
padding: 20px;
        }
        .form-group {
        margin-bottom: 15px;
        }
        label {
            display: block;
    margin-bottom: 5px;
       font-weight: bold;
        }
        input, textarea {
            width: 100%;
   padding: 8px;
    border: 1px solid #ddd;
            border-radius: 4px;
     }
     button {
    background: #1890ff;
            color: white;
   padding: 10px 20px;
            border: none;
            border-radius: 4px;
            cursor: pointer;
        }
        button:hover {
          background: #40a9ff;
        }
        .message {
       padding: 10px;
  margin-top: 15px;
    border-radius: 4px;
        }
        .success {
            background: #f6ffed;
      border: 1px solid #b7eb8f;
            color: #52c41a;
  }
        .error {
            background: #fff2f0;
            border: 1px solid #ffccc7;
      color: #ff4d4f;
  }
    </style>
</head>
<body>
    <h1>Contact Us</h1>
    <form id="contactForm">
    <div class="form-group">
       <label for="name">Name:</label>
  <input type="text" id="name" required>
   </div>
        <div class="form-group">
         <label for="email">Email:</label>
 <input type="email" id="email" required>
        </div>
        <div class="form-group">
     <label for="message">Message:</label>
      <textarea id="message" rows="5" required></textarea>
        </div>
      <button type="submit">Send Message</button>
    </form>
 
    <div id="result"></div>
    
    <script>
        document.getElementById('contactForm').addEventListener('submit', async function(e) {
            e.preventDefault();
   
      const resultDiv = document.getElementById('result');
    resultDiv.innerHTML = '<div class="message">Sending...</div>';
     
          try {
          // Execute Automaton workflow to send email
     const result = await Automaton.executeFlow('your-email-flow-id', {
  name: document.getElementById('name').value,
   email: document.getElementById('email').value,
  message: document.getElementById('message').value
    });
          
      if (result.success) {
     resultDiv.innerHTML = '<div class="message success">Message sent successfully!</div>';
    document.getElementById('contactForm').reset();
         } else {
   throw new Error(result.message || 'Failed to send message');
  }
      } catch (error) {
     resultDiv.innerHTML = `<div class="message error">Error: ${error.message}</div>`;
   }
 });
    </script>
</body>
</html>
```

---

### Phase 5: Security Implementation

#### 5.1 Content Security Policy (CSP)

**File:** `src/Automaton.Studio.Server/Program.cs` or Middleware

**Purpose:** Protect against XSS and other injection attacks in published web apps

```csharp
// Add CSP middleware for /web/* routes
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/web"))
    {
        // Strict CSP for published web apps
        context.Response.Headers.Add("Content-Security-Policy", 
     "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https:; " +
    "font-src 'self' data:; " +
     "connect-src 'self';");
   
        // Prevent clickjacking
  context.Response.Headers.Add("X-Frame-Options", "SAMEORIGIN");
  
        // XSS protection
        context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    }
    
    await next();
});
```

**CSP Directives Explained:**
- `default-src 'self'`: Only load resources from same origin by default
- `script-src 'unsafe-inline' 'unsafe-eval'`: Allow inline scripts (needed for user content)
- `style-src 'unsafe-inline'`: Allow inline styles
- `img-src data: https:`: Allow images from data URLs and HTTPS
- `connect-src 'self'`: Only allow API calls to same origin (Automaton APIs)

#### 5.2 Iframe Sandboxing

**Applied in both Preview and Published Views:**

```html
<iframe 
  srcdoc="..." 
    sandbox="allow-scripts allow-forms allow-same-origin allow-modals"
    class="app-iframe">
</iframe>
```

**Sandbox Permissions:**
- ? `allow-scripts`: Execute JavaScript (required for functionality)
- ? `allow-forms`: Submit forms (required for interactive apps)
- ? `allow-same-origin`: Access Automaton APIs (required for workflow integration)
- ? `allow-modals`: Show alert/confirm/prompt dialogs
- ? **NOT** `allow-top-navigation`: Prevent hijacking parent navigation
- ? **NOT** `allow-popups`: Prevent unwanted popup windows
- ? **NOT** `allow-pointer-lock`: Prevent pointer lock (gaming APIs)

#### 5.3 Rate Limiting

**File:** `src/Automaton.Studio.Server/Services/RateLimiterService.cs` (NEW)

**Purpose:** Prevent abuse of flow execution API

```csharp
public interface IRateLimiterService
{
    Task<bool> AllowRequest(string clientIdentifier);
}

public class RateLimiterService : IRateLimiterService
{
  private readonly IMemoryCache _cache;
    private const int MaxRequestsPerMinute = 60;
  private const int MaxRequestsPerHour = 1000;

    public async Task<bool> AllowRequest(string clientIdentifier)
    {
        var minuteKey = $"rate_limit_minute_{clientIdentifier}";
        var hourKey = $"rate_limit_hour_{clientIdentifier}";

        // Check minute limit
        var minuteCount = _cache.GetOrCreate(minuteKey, entry =>
     {
         entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return 0;
        });

   if (minuteCount >= MaxRequestsPerMinute)
   return false;

        // Check hour limit
      var hourCount = _cache.GetOrCreate(hourKey, entry =>
   {
   entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
  return 0;
   });

        if (hourCount >= MaxRequestsPerHour)
     return false;

        // Increment counters
        _cache.Set(minuteKey, minuteCount + 1);
        _cache.Set(hourKey, hourCount + 1);

     return true;
    }
}
```

**Rate Limits (Configurable):**
- 60 requests per minute per IP
- 1000 requests per hour per IP
- Future: Per-app or per-flow limits
- Future: API key-based authentication with higher limits

#### 5.4 Input Validation & Sanitization

**Flow Execution Request Validation:**

```csharp
public class FlowExecutionRequestValidator : AbstractValidator<FlowExecutionRequest>
{
    public FlowExecutionRequestValidator()
    {
        RuleFor(x => x.FlowId)
    .NotEmpty()
            .WithMessage("FlowId is required");

   RuleFor(x => x.FlowId)
    .Must(BeValidGuid)
 .WithMessage("FlowId must be a valid GUID");

        RuleFor(x => x.Inputs)
            .NotNull()
  .WithMessage("Inputs cannot be null");

        RuleFor(x => x.Inputs.Count)
    .LessThanOrEqualTo(50)
            .WithMessage("Maximum 50 input variables allowed");
    }

    private bool BeValidGuid(string flowId)
    {
        return Guid.TryParse(flowId, out _);
    }
}
```

---

### Phase 6: UI Updates

#### 6.1 Apps List Page Updates

**File:** `src/Automaton.Studio/Pages/Apps/AppsPage.razor`

**Changes:**
1. Add "Published" status column/badge
2. Add "Publish" / "Unpublish" button
3. Add "Open" button for published apps
4. Add copy link functionality

**Visual Changes:**

```razor
<Table DataSource="@apps" Loading="loading">
    <Column Title="Name" TData="string">
     <div style="display: flex; align-items: center; gap: 8px;">
  <span>@context.DisplayName</span>
      @if (context.IsPublished)
            {
         <Tag Color="green">Published</Tag>
       }
      </div>
  </Column>
    
    <Column Title="Description" TData="string">
        @context.Description
    </Column>
    
    <Column Title="Updated" TData="DateTime">
  @context.Updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
    </Column>
    
    <ActionColumn Title="Actions">
        <Space>
        <SpaceItem>
      <Tooltip Title="Edit app">
    <Button Icon="edit" 
Size="@ButtonSize.Small"
         OnClick="() => Edit(context.Id)">
       Edit
     </Button>
        </Tooltip>
        </SpaceItem>
  
       @if (context.IsPublished)
      {
        <SpaceItem>
          <Tooltip Title="Open published app">
     <Button Icon="eye" 
 Size="@ButtonSize.Small"
         Type="@ButtonType.Primary"
                  OnClick="() => OpenPublished(context.PublicUrl)">
 Open
             </Button>
      </Tooltip>
                </SpaceItem>
       
              <SpaceItem>
        <Tooltip Title="Copy public link">
    <Button Icon="link" 
    Size="@ButtonSize.Small"
          OnClick="() => CopyPublicLink(context.PublicUrl)">
            </Button>
         </Tooltip>
    </SpaceItem>
   }
            
  <SpaceItem>
        <Tooltip Title="@(context.IsPublished ? "Unpublish app" : "Publish app")">
    <Button Icon="@(context.IsPublished ? "stop" : "cloud-upload")" 
       Size="@ButtonSize.Small"
         OnClick="() => TogglePublish(context)">
         @(context.IsPublished ? "Unpublish" : "Publish")
           </Button>
                </Tooltip>
            </SpaceItem>
          
    <SpaceItem>
        <Popconfirm Title="Are you sure you want to delete this app?"
             OnConfirm="() => Delete(context.Id)"
    OkText="Yes"
            CancelText="No">
         <Button Icon="delete" 
            Size="@ButtonSize.Small"
         Danger>
     Delete
        </Button>
      </Popconfirm>
  </SpaceItem>
        </Space>
    </ActionColumn>
</Table>
```

#### 6.2 App Designer Page Updates

**File:** `src/Automaton.Studio/Pages/Apps/AppDesignerPage.razor`

**Changes:**
1. Add Preview tab (new)
2. Add publish status indicator in header
3. Add publish/unpublish button in toolbar
4. Show public URL when published

**Header Updates:**

```razor
<Header Class="header">
    <Row>
        <Col>
            <Space Direction="SpaceDirection.Horizontal">
          <SpaceItem Class="header-item">
                <Breadcrumb>
        <!-- Existing breadcrumb -->
         </Breadcrumb>
         </SpaceItem>
         
  @if (currentApp.IsPublished)
       {
          <SpaceItem>
          <Tag Color="green" Icon="check-circle">
            Published
         </Tag>
       </SpaceItem>
       <SpaceItem>
  <Text Type="@TextElementType.Secondary" Style="font-size: 12px;">
      <Icon Type="link" />
        /web/@currentApp.PublicUrl
       </Text>
            </SpaceItem>
  }
         </Space>
        </Col>
     
  <Col Class="right-actions">
            <Space Class="right-header-items">
             <SpaceItem>
    <Button Icon="save" OnClick="Save" 
    Type="@ButtonType.Primary" Loading="@saving">
   Save
 </Button>
     </SpaceItem>
     
        <SpaceItem>
        <Button Icon="@(currentApp.IsPublished ? "stop" : "cloud-upload")"
      OnClick="TogglePublish"
     Loading="@publishing">
     @(currentApp.IsPublished ? "Unpublish" : "Publish")
          </Button>
     </SpaceItem>
       
            @if (currentApp.IsPublished)
        {
        <SpaceItem>
              <Button Icon="eye" 
       OnClick="OpenPublishedApp"
     Type="@ButtonType.Default">
          View Published
    </Button>
      </SpaceItem>
     }
  </Space>
        </Col>
    </Row>
</Header>
```

---

## ?? Implementation Priority

### ? High Priority (MVP - Week 1-2) - **PHASES 1, 2 & 3 COMPLETE**

1. **? Preview Tab Implementation - COMPLETED (Phase 1)**
   - ? Added Preview tab to AppDesignerPage
   - ? Implemented iframe preview with srcdoc
   - ? Added refresh functionality
   - ? Added "Open in New Tab" feature
   - ? Injected Automaton bridge for preview mode (mock responses)
   - ? Injected Automaton bridge for production mode (real API calls)

2. **? Basic Publishing - COMPLETED (Phase 2)**
   - ? Update WebApp entity (add PublishedAt, Version)
   - ?? Create database migration (PENDING - needs manual run)
   - ? Add Publish/Unpublish endpoints
   - ? Implement slug generation
   - ? Add publish button in UI
   - ? Add publish dialog component
   - ? Add publish status indicators
   - ? Add version tracking

3. **? Public Access - COMPLETED (Phase 3)**
   - ? Create PublicWebAppsController
   - ? Create WebAppViewer page
   - ? Implement EmptyLayout
   - ? Add /web/{slug} routing
   - ? Fix authentication error

4. **Automaton Bridge**
   - ? Create JavaScript bridge injection - COMPLETED (Phase 1)
   - ? Implement preview mode mock responses - COMPLETED (Phase 1)
   - ? Inject bridge in public apps - COMPLETED (Phase 3)
   - Implement basic executeFlow API - Phase 4 (IN PROGRESS)
