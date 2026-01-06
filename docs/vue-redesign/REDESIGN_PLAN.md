# Automaton Studio Vue.js Redesign & AI-First Flow Generation

**Status:** Planning Phase
**Start Date:** January 2026
**Estimated Completion:** April 2026 (3-4 months)
**Author:** Claude & User

---

## Executive Summary

This document outlines the complete redesign of Automaton Studio from Blazor WebAssembly to Vue.js, alongside a fundamental shift from traditional drag-and-drop workflow builders to an AI-first approach where users describe automations in natural language.

### Key Changes

1. **Frontend Framework:** Blazor WASM + Ant Design Blazor → Vue 3 + TypeScript + Vuetify
2. **Flow Creation:** Drag-and-drop designer → AI-powered natural language generation
3. **Step Library:** Pre-built steps → AI-generated custom steps with intelligent reuse
4. **Developer Experience:** Limited debugging → Full browser DevTools access

### Why This Change?

**Technical Pain Points:**
- Ant Design Blazor is immature and buggy (e.g., tabs not rendering without hot reload)
- SignalR debugging overhead limits browser DevTools usage
- Monaco Editor integration requires complex wrappers
- Blazor WASM rendering quirks require workarounds (StateHasChanged, render toggles)

**Market Opportunity:**
- Traditional automation tools (Zapier, n8n) are being disrupted by AI-first approaches
- Users want to describe workflows, not drag boxes around
- Custom logic should be generated on-demand, not limited by pre-built integrations

---

## Table of Contents

1. [Vision & Goals](#vision--goals)
2. [Architecture Overview](#architecture-overview)
3. [Technology Stack](#technology-stack)
4. [Feature Comparison](#feature-comparison)
5. [Backend API Design](#backend-api-design)
6. [Frontend Implementation](#frontend-implementation)
7. [UI/UX Redesign](#uiux-redesign)
8. [Implementation Strategy](#implementation-strategy)
9. [Timeline & Milestones](#timeline--milestones)
10. [Risk Assessment](#risk-assessment)
11. [Success Metrics](#success-metrics)

---

## Vision & Goals

### Product Vision

> "Automaton: Describe your workflow in plain English. AI builds it. No pre-built steps, no drag-and-drop complexity."

### Primary Goals

1. **Eliminate Blazor Pain Points**
   - Replace with mature, stable Vue.js ecosystem
   - Direct Monaco Editor integration without wrappers
   - Full browser DevTools access
   - Predictable component rendering

2. **AI-First Workflow Creation**
   - Natural language flow generation
   - Intelligent step reuse across workflows
   - On-demand custom step generation
   - Minimal visual designer (read-only timeline)

3. **Maintain Backend Stability**
   - Keep existing .NET API unchanged
   - Preserve Runner execution engine
   - No database schema breaking changes
   - Existing integrations continue working

### Non-Goals

- ❌ Rewriting the backend API
- ❌ Changing the Runner architecture
- ❌ Breaking existing flows/apps/steps

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    NEW: Vue.js Frontend                 │
│  ┌─────────────┬──────────────┬────────────────────┐   │
│  │   Flows     │    Apps      │   Steps/Settings   │   │
│  │ (AI-First)  │ (Visual IDE) │   (Management)     │   │
│  └─────────────┴──────────────┴────────────────────┘   │
│         Vue 3 + TypeScript + Vuetify                    │
└─────────────────────────────────────────────────────────┘
                          │
                    HTTPS/REST
                          │
┌─────────────────────────────────────────────────────────┐
│              EXISTING: .NET API (Unchanged)             │
│  ┌──────────────────────────────────────────────────┐  │
│  │  FlowsController  │  AppsController  │  etc...   │  │
│  └──────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────┐  │
│  │  NEW: FlowGenerationController                   │  │
│  │  - GenerateFlow (AI-powered)                     │  │
│  │  - RefineFlow                                    │  │
│  │  - SearchSteps (for reuse)                       │  │
│  └──────────────────────────────────────────────────┘  │
│            ASP.NET Core 9 + Entity Framework            │
└─────────────────────────────────────────────────────────┘
                          │
                   MySQL/MariaDB
                          │
┌─────────────────────────────────────────────────────────┐
│                    EXISTING: Runner                     │
│              (No changes - keeps working)               │
└─────────────────────────────────────────────────────────┘
```

---

## Technology Stack

### Frontend (NEW)

| Component | Technology | Version | Why? |
|-----------|-----------|---------|------|
| **Framework** | Vue.js | 3.4+ | Mature, reactive, excellent TypeScript support |
| **Language** | TypeScript | 5.0+ | Type safety, better DX than pure JavaScript |
| **Build Tool** | Vite | 5.0+ | Fast HMR, modern ESM-based build |
| **UI Library** | Vuetify | 3.4+ | Most popular Vue UI library, Material Design, 80+ components, excellent docs |
| **State Management** | Pinia | 2.1+ | Vue 3's official state management |
| **Router** | Vue Router | 4.0+ | Official routing solution |
| **HTTP Client** | Axios | 1.6+ | Industry standard, interceptors for auth |
| **Code Editor** | Monaco Editor | Latest | Direct integration (no wrappers!) |
| **Type Generation** | openapi-typescript | Latest | Auto-generate types from C# API |

### Backend (EXISTING + ADDITIONS)

| Component | Status | Notes |
|-----------|--------|-------|
| **ASP.NET Core** | ✅ Keep | 9.0, no changes |
| **Entity Framework** | ✅ Keep | Existing schema preserved |
| **AI Integration** | ➕ Enhance | Extend existing Mcp integration for flow generation |
| **SignalR** | ✅ Keep | Runner communication unchanged |
| **Authentication** | ✅ Keep | JWT/API keys work as-is |

### Development Tools

- **IDE:** VS Code (for Vue.js), Visual Studio (for .NET API)
- **API Testing:** Swagger UI (existing), Postman
- **Version Control:** Git
- **Package Managers:** npm/pnpm (frontend), NuGet (backend)

---

## Feature Comparison

### Current (Blazor) vs. New (Vue.js)

| Feature | Current (Blazor) | New (Vue.js + AI) | Status |
|---------|------------------|-------------------|--------|
| **Flow Creation** | Drag-and-drop visual designer | Natural language → AI generates flow | 🆕 Revolutionary |
| **Step Library** | Pre-built step catalog | AI generates on-demand + reuse existing | 🆕 Revolutionary |
| **Apps** | Visual app builder with Monaco | Same concept, better Monaco integration | ✨ Enhanced |
| **Step Designer** | Form-based step creation | Same with better code editor | ✨ Enhanced |
| **Dashboard** | Summary cards, charts | Same functionality, better UI | ✨ Enhanced |
| **Settings** | Configuration pages | Same functionality | ➡️ Ported |
| **Authentication** | Login/register pages | Same flow, improved UX | ✨ Enhanced |
| **Debugging** | Limited (SignalR overhead) | Full browser DevTools | ✅ Fixed |
| **Component Quality** | Buggy (tabs, tree issues) | Stable, mature library | ✅ Fixed |

---

## Backend API Design

### New Endpoints for Flow Generation

#### 1. Generate Flow from Description

```
POST /api/flows/generate
Authorization: Bearer {token}

Request:
{
  "description": "When a webhook receives a new customer order, validate payment with Stripe, update inventory in PostgreSQL, send confirmation email via SendGrid, and create shipping label with ShipStation.",
  "allowStepReuse": true,
  "suggestedStepIds": [], // Optional: force use of specific steps
  "context": {
    "webhookUrl": "https://...",
    "stripeApiKey": "sk_test_..."
  }
}

Response:
{
  "flowId": "guid",
  "flowName": "Customer Order Processing",
  "description": "Automated order processing workflow",
  "steps": [
    {
      "stepId": "guid-1",
      "name": "ValidateStripePayment",
      "displayName": "Validate Stripe Payment",
      "description": "Validates payment using Stripe API",
      "code": "import stripe\n...",
      "language": "python",
      "inputVariables": [
        {"name": "paymentIntentId", "type": "string"}
      ],
      "outputVariables": [
        {"name": "isValid", "type": "bool"},
        {"name": "errorMessage", "type": "string"}
      ],
      "isReused": false,
      "confidenceScore": 95
    },
    {
      "stepId": "guid-2",
      "name": "UpdateInventory",
      "displayName": "Update PostgreSQL Inventory",
      "description": "Updates product inventory in database",
      "code": "import psycopg2\n...",
      "language": "python",
      "inputVariables": [
        {"name": "productId", "type": "string"},
        {"name": "quantity", "type": "int"}
      ],
      "outputVariables": [
        {"name": "newStockLevel", "type": "int"}
      ],
      "isReused": false,
      "confidenceScore": 92
    },
    // ... more steps
  ],
  "executionOrder": [
    {"fromStepId": "guid-1", "toStepId": "guid-2", "condition": "if success"},
    {"fromStepId": "guid-2", "toStepId": "guid-3", "condition": "always"}
  ],
  "warnings": [
    "Step 'ValidateStripePayment' requires STRIPE_API_KEY environment variable"
  ]
}
```

#### 2. Refine Existing Flow

```
POST /api/flows/generate/refine/{flowId}
Authorization: Bearer {token}

Request:
{
  "instruction": "Change step 2 to send a Slack notification instead of email",
  "stepId": "guid-2" // Optional: target specific step
}

Response:
{
  // Updated flow definition
}
```

#### 3. Search Existing Steps

```
GET /api/flows/generate/search-steps?query=validate email
Authorization: Bearer {token}

Response:
{
  "results": [
    {
      "stepId": "guid",
      "name": "EmailValidatorStep",
      "description": "Validates email addresses using regex and DNS lookup",
      "usageCount": 12, // Used in 12 flows
      "lastUsed": "2026-01-03T10:00:00Z",
      "matchScore": 0.95
    }
  ]
}
```

#### 4. Test Generated Flow

```
POST /api/flows/generate/test/{flowId}
Authorization: Bearer {token}

Request:
{
  "testData": {
    "webhookPayload": "{\"orderId\": \"12345\"}"
  },
  "dryRun": true // Don't actually execute, just validate
}

Response:
{
  "success": true,
  "executionId": "guid",
  "steps": [
    {
      "stepName": "ValidateStripePayment",
      "status": "success",
      "output": {"isValid": true},
      "duration": 234 // ms
    }
  ]
}
```

### Backend Service Implementation

```csharp
// src/Automaton.Studio.Server/Services/FlowGenerationService.cs

public class FlowGenerationService
{
    private readonly IAiProvider _aiProvider;
    private readonly CustomStepsService _stepsService;
    private readonly FlowsService _flowsService;
    private readonly ILogger<FlowGenerationService> _logger;

    public async Task<GenerateFlowResponse> GenerateFlowAsync(
        GenerateFlowRequest request,
        string userId)
    {
        _logger.LogInformation(
            "Generating flow from description: {Description}",
            request.Description);

        // 1. Search for reusable steps
        var reusableSteps = request.AllowStepReuse
            ? await FindReusableSteps(request.Description, userId)
            : new List<CustomStepDto>();

        _logger.LogInformation(
            "Found {Count} potentially reusable steps",
            reusableSteps.Count);

        // 2. Build AI prompt
        var prompt = BuildFlowGenerationPrompt(
            request.Description,
            reusableSteps,
            request.Context);

        // 3. Call AI to generate flow structure
        var aiResponse = await _aiProvider.GenerateStructuredResponse<FlowGenerationAiResponse>(
            prompt,
            temperature: 0.3, // Lower for more deterministic output
            maxTokens: 4000);

        // 4. Validate and create custom steps
        var steps = new List<GeneratedStepDto>();
        foreach (var stepDef in aiResponse.Steps)
        {
            if (stepDef.Action == "reuse")
            {
                var existingStep = reusableSteps.FirstOrDefault(
                    s => s.Id == stepDef.ExistingStepId);

                if (existingStep != null)
                {
                    steps.Add(MapToGeneratedStep(existingStep, isReused: true));
                }
                else
                {
                    _logger.LogWarning(
                        "AI tried to reuse non-existent step {StepId}",
                        stepDef.ExistingStepId);

                    // Fall back to creating new step
                    steps.Add(await CreateNewStep(stepDef, userId));
                }
            }
            else // create new
            {
                steps.Add(await CreateNewStep(stepDef, userId));
            }
        }

        // 5. Create flow in database (draft state)
        var flow = new Flow
        {
            Id = Guid.NewGuid(),
            Name = aiResponse.FlowName,
            Description = aiResponse.Description,
            UserId = userId,
            Status = FlowStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            Definition = new FlowDefinition
            {
                Steps = steps.Select(s => new FlowStep
                {
                    StepId = s.StepId,
                    Name = s.Name,
                    InputVariables = s.InputVariables,
                    OutputVariables = s.OutputVariables
                }).ToList(),
                ExecutionOrder = aiResponse.ExecutionOrder
            }
        };

        await _flowsService.CreateAsync(flow);

        _logger.LogInformation(
            "Flow {FlowId} generated successfully with {StepCount} steps",
            flow.Id,
            steps.Count);

        return new GenerateFlowResponse
        {
            FlowId = flow.Id,
            FlowName = flow.Name,
            Description = flow.Description,
            Steps = steps,
            ExecutionOrder = aiResponse.ExecutionOrder,
            Warnings = ValidateFlowConfiguration(flow, steps)
        };
    }

    private string BuildFlowGenerationPrompt(
        string userDescription,
        List<CustomStepDto> reusableSteps,
        Dictionary<string, object> context)
    {
        return $@"
You are an expert automation engineer. Generate a workflow based on this description.

USER REQUEST:
{userDescription}

EXISTING STEPS AVAILABLE FOR REUSE:
{string.Join("\n", reusableSteps.Select(s =>
    $"- ID: {s.Id}, Name: {s.Name}, Description: {s.Description}, Language: {s.Language}"))}

CONTEXT PROVIDED BY USER:
{JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true })}

INSTRUCTIONS:
1. Analyze the user's request and break it into logical steps
2. Prefer reusing existing steps when they match the requirement
3. Generate new steps only when necessary
4. Use Python for new steps unless the task is Windows-specific (use PowerShell)
5. Ensure proper error handling in all steps
6. Map input/output variables between steps correctly

OUTPUT FORMAT (JSON):
{{
  ""flowName"": ""Short, descriptive name"",
  ""description"": ""Brief explanation of what this flow does"",
  ""steps"": [
    {{
      ""name"": ""StepName"",
      ""action"": ""reuse"" | ""create"",
      ""existingStepId"": ""guid"" (only if reusing),
      ""displayName"": ""Human-readable name"",
      ""description"": ""What this step does"",
      ""code"": ""Step code"" (only if creating new),
      ""language"": ""python"" | ""powershell"" | ""batch"",
      ""inputs"": [
        {{""name"": ""variableName"", ""type"": ""string"" | ""int"" | ""bool"" | ""object""}}
      ],
      ""outputs"": [
        {{""name"": ""variableName"", ""type"": ""string"" | ""int"" | ""bool"" | ""object""}}
      ]
    }}
  ],
  ""executionOrder"": [
    {{
      ""fromStepId"": 0, // Index in steps array
      ""toStepId"": 1,
      ""condition"": ""if success"" | ""if error"" | ""always""
    }}
  ]
}}

Generate the flow now:
";
    }

    private async Task<List<CustomStepDto>> FindReusableSteps(
        string description,
        string userId)
    {
        // TODO: Implement vector/semantic search
        // For now, simple keyword matching
        var allSteps = await _stepsService.GetUserStepsAsync(userId);

        // Simple keyword matching - replace with vector search later
        var keywords = description
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return allSteps
            .Where(step =>
                keywords.Any(k =>
                    step.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                    step.Description?.Contains(k, StringComparison.OrdinalIgnoreCase) == true))
            .Take(10)
            .ToList();
    }

    private async Task<GeneratedStepDto> CreateNewStep(
        StepDefinition stepDef,
        string userId)
    {
        var step = await _stepsService.CreateAsync(new CreateCustomStepRequest
        {
            Name = stepDef.Name,
            DisplayName = stepDef.DisplayName,
            Description = stepDef.Description,
            Code = stepDef.Code,
            Language = stepDef.Language,
            InputVariables = stepDef.Inputs,
            OutputVariables = stepDef.Outputs,
            UserId = userId,
            IsGeneratedByAi = true
        });

        return MapToGeneratedStep(step, isReused: false);
    }

    private List<string> ValidateFlowConfiguration(
        Flow flow,
        List<GeneratedStepDto> steps)
    {
        var warnings = new List<string>();

        // Check for missing environment variables
        foreach (var step in steps)
        {
            if (step.Code.Contains("os.environ[") ||
                step.Code.Contains("Environment.GetEnvironmentVariable"))
            {
                warnings.Add(
                    $"Step '{step.DisplayName}' may require environment variables to be configured");
            }

            // Check for API keys mentioned in code
            if (step.Code.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
                step.Code.Contains("apikey", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"Step '{step.DisplayName}' requires API key configuration");
            }
        }

        return warnings;
    }
}
```

---

## Frontend Implementation

### Project Structure (Feature-Based)

**Architecture:** Feature-based structure where each feature is self-contained with its components, composables, services, types, and tests in the same folder. This is the modern, recommended approach for Vue 3 applications.

```
src/automaton-studio-vue/
├── public/
│   └── favicon.ico
├── src/
│   ├── features/
│   │   ├── auth/
│   │   │   ├── components/
│   │   │   │   ├── LoginForm.vue
│   │   │   │   ├── RegisterForm.vue
│   │   │   │   └── EmailConfirmation.vue
│   │   │   ├── composables/
│   │   │   │   └── useAuth.ts
│   │   │   ├── services/
│   │   │   │   └── auth.service.ts
│   │   │   ├── stores/
│   │   │   │   └── auth.store.ts
│   │   │   ├── types/
│   │   │   │   └── auth.types.ts
│   │   │   ├── views/
│   │   │   │   ├── LoginView.vue
│   │   │   │   └── RegisterView.vue
│   │   │   └── routes.ts
│   │   │
│   │   ├── dashboard/
│   │   │   ├── components/
│   │   │   │   ├── StatsCard.vue
│   │   │   │   ├── RecentExecutions.vue
│   │   │   │   ├── QuickActions.vue
│   │   │   │   └── ExecutionTrendChart.vue
│   │   │   ├── composables/
│   │   │   │   └── useDashboardStats.ts
│   │   │   ├── services/
│   │   │   │   └── dashboard.service.ts
│   │   │   ├── stores/
│   │   │   │   └── dashboard.store.ts
│   │   │   ├── types/
│   │   │   │   └── dashboard.types.ts
│   │   │   ├── views/
│   │   │   │   └── DashboardView.vue
│   │   │   └── routes.ts
│   │   │
│   │   ├── flows/
│   │   │   ├── components/
│   │   │   │   ├── FlowCard.vue
│   │   │   │   ├── FlowDescriptionInput.vue
│   │   │   │   ├── FlowTimeline.vue
│   │   │   │   ├── StepCard.vue
│   │   │   │   ├── StepDetails.vue
│   │   │   │   ├── StepReuseSuggestions.vue
│   │   │   │   ├── FlowRefinementDialog.vue
│   │   │   │   └── AiAssistant.vue
│   │   │   ├── composables/
│   │   │   │   ├── useFlowGeneration.ts
│   │   │   │   ├── useFlowExecution.ts
│   │   │   │   └── useFlowTimeline.ts
│   │   │   ├── services/
│   │   │   │   ├── flow-generation.service.ts
│   │   │   │   ├── flows.service.ts
│   │   │   │   └── flow-execution.service.ts
│   │   │   ├── stores/
│   │   │   │   ├── flow-generation.store.ts
│   │   │   │   └── flows.store.ts
│   │   │   ├── types/
│   │   │   │   ├── flow.types.ts
│   │   │   │   └── flow-generation.types.ts
│   │   │   ├── views/
│   │   │   │   ├── FlowListView.vue
│   │   │   │   ├── FlowGenerationView.vue
│   │   │   │   ├── FlowEditorView.vue
│   │   │   │   ├── FlowExecutionView.vue
│   │   │   │   └── FlowHistoryView.vue
│   │   │   └── routes.ts
│   │   │
│   │   ├── apps/
│   │   │   ├── components/
│   │   │   │   ├── AppCard.vue
│   │   │   │   ├── AppFileTree.vue
│   │   │   │   ├── AppEditor.vue
│   │   │   │   ├── AppPreview.vue
│   │   │   │   ├── AppSettings.vue
│   │   │   │   ├── AppBackendSteps.vue
│   │   │   │   └── AppPublishDialog.vue
│   │   │   ├── composables/
│   │   │   │   ├── useAppEditor.ts
│   │   │   │   ├── useMonacoEditor.ts
│   │   │   │   └── useAppPreview.ts
│   │   │   ├── services/
│   │   │   │   └── apps.service.ts
│   │   │   ├── stores/
│   │   │   │   └── apps.store.ts
│   │   │   ├── types/
│   │   │   │   └── apps.types.ts
│   │   │   ├── views/
│   │   │   │   ├── AppListView.vue
│   │   │   │   ├── AppEditorView.vue
│   │   │   │   └── AppPublishedView.vue
│   │   │   └── routes.ts
│   │   │
│   │   ├── steps/
│   │   │   ├── components/
│   │   │   │   ├── StepCard.vue
│   │   │   │   ├── StepEditor.vue
│   │   │   │   ├── StepCodeEditor.vue
│   │   │   │   ├── StepConfiguration.vue
│   │   │   │   ├── StepTestDialog.vue
│   │   │   │   └── StepSearchBar.vue
│   │   │   ├── composables/
│   │   │   │   ├── useStepEditor.ts
│   │   │   │   └── useStepExecution.ts
│   │   │   ├── services/
│   │   │   │   └── steps.service.ts
│   │   │   ├── stores/
│   │   │   │   └── steps.store.ts
│   │   │   ├── types/
│   │   │   │   └── steps.types.ts
│   │   │   ├── views/
│   │   │   │   ├── StepListView.vue
│   │   │   │   └── StepEditorView.vue
│   │   │   └── routes.ts
│   │   │
│   │   ├── runners/
│   │   │   ├── components/
│   │   │   │   ├── RunnerCard.vue
│   │   │   │   ├── RunnerStatusBadge.vue
│   │   │   │   ├── RunnerLogs.vue
│   │   │   │   └── RunnerAddDialog.vue
│   │   │   ├── composables/
│   │   │   │   ├── useRunners.ts
│   │   │   │   └── useRunnerConnection.ts
│   │   │   ├── services/
│   │   │   │   └── runners.service.ts
│   │   │   ├── stores/
│   │   │   │   └── runners.store.ts
│   │   │   ├── types/
│   │   │   │   └── runners.types.ts
│   │   │   ├── views/
│   │   │   │   └── RunnersView.vue
│   │   │   └── routes.ts
│   │   │
│   │   ├── projects/
│   │   │   ├── components/
│   │   │   │   ├── ProjectCard.vue
│   │   │   │   ├── ProjectSwitcher.vue
│   │   │   │   ├── ProjectMemberCard.vue
│   │   │   │   ├── ProjectMemberInviteDialog.vue
│   │   │   │   ├── ProjectResourcesList.vue
│   │   │   │   ├── ProjectSettingsTabs.vue
│   │   │   │   └── ProjectDeleteDialog.vue
│   │   │   ├── composables/
│   │   │   │   ├── useProjects.ts
│   │   │   │   ├── useProjectMembers.ts
│   │   │   │   └── useProjectResources.ts
│   │   │   ├── services/
│   │   │   │   ├── projects.service.ts
│   │   │   │   ├── project-members.service.ts
│   │   │   │   └── project-resources.service.ts
│   │   │   ├── stores/
│   │   │   │   └── projects.store.ts
│   │   │   ├── types/
│   │   │   │   └── projects.types.ts
│   │   │   ├── views/
│   │   │   │   ├── ProjectsListView.vue
│   │   │   │   └── ProjectSettingsView.vue
│   │   │   └── routes.ts
│   │   │
│   │   └── settings/
│   │       ├── components/
│   │       │   ├── SettingsSidebar.vue
│   │       │   ├── AccountSettings.vue
│   │       │   ├── ApiKeysSettings.vue
│   │       │   ├── AiKeysSettings.vue
│   │       │   ├── AppearanceSettings.vue
│   │       │   └── NotificationSettings.vue
│   │       ├── composables/
│   │       │   └── useSettings.ts
│   │       ├── services/
│   │       │   └── settings.service.ts
│   │       ├── stores/
│   │       │   └── settings.store.ts
│   │       ├── types/
│   │       │   └── settings.types.ts
│   │       ├── views/
│   │       │   └── SettingsView.vue
│   │       └── routes.ts
│   │
│   ├── shared/
│   │   ├── components/
│   │   │   ├── layout/
│   │   │   │   ├── AppShell.vue
│   │   │   │   ├── AppBar.vue
│   │   │   │   ├── NavigationRail.vue
│   │   │   │   ├── Breadcrumbs.vue
│   │   │   │   └── StatusBar.vue
│   │   │   ├── ui/
│   │   │   │   ├── BaseButton.vue
│   │   │   │   ├── BaseCard.vue
│   │   │   │   ├── BaseDialog.vue
│   │   │   │   ├── BaseInput.vue
│   │   │   │   ├── LoadingSpinner.vue
│   │   │   │   ├── EmptyState.vue
│   │   │   │   └── ErrorBoundary.vue
│   │   │   └── editor/
│   │   │       ├── MonacoEditor.vue
│   │   │       └── CodeViewer.vue
│   │   ├── composables/
│   │   │   ├── useTheme.ts
│   │   │   ├── useNotification.ts
│   │   │   ├── useDialog.ts
│   │   │   ├── useLocalStorage.ts
│   │   │   └── useDebounce.ts
│   │   ├── services/
│   │   │   ├── api/
│   │   │   │   ├── axios-instance.ts
│   │   │   │   └── api-client.ts
│   │   │   └── signalr/
│   │   │       └── signalr-client.ts
│   │   ├── utils/
│   │   │   ├── error-handler.ts
│   │   │   ├── date-formatter.ts
│   │   │   ├── validation.ts
│   │   │   └── storage.ts
│   │   ├── types/
│   │   │   ├── api/            # Auto-generated from OpenAPI
│   │   │   │   └── index.d.ts
│   │   │   └── common.types.ts
│   │   └── constants/
│   │       ├── routes.ts
│   │       └── config.ts
│   │
│   ├── router/
│   │   └── index.ts            # Aggregates all feature routes
│   │
│   ├── assets/
│   │   ├── styles/
│   │   │   ├── main.css
│   │   │   ├── variables.css
│   │   │   ├── vuetify-overrides.css
│   │   │   └── themes/
│   │   │       ├── light.css
│   │   │       └── dark.css
│   │   └── images/
│   │       ├── logo.svg
│   │       └── icons/
│   │
│   ├── App.vue
│   └── main.ts
│
├── .env.development
├── .env.production
├── vite.config.ts
├── tsconfig.json
├── package.json
└── README.md
```

### Why Feature-Based Structure?

1. **Colocation:** Everything related to a feature is in one place
2. **Scalability:** Easy to add/remove features without touching unrelated code
3. **Team Collaboration:** Multiple developers can work on different features without conflicts
4. **Code Splitting:** Easier to lazy-load entire features
5. **Testability:** Feature tests live alongside feature code
6. **Maintainability:** Clear boundaries between features

### Feature Module Pattern

Each feature follows this structure:

```
feature-name/
├── components/          # Feature-specific components
│   └── __tests__/       # Component tests (Vitest + Vue Test Utils)
├── composables/         # Feature-specific composition API functions
│   └── __tests__/       # Composable tests
├── services/            # API calls and business logic for this feature
│   └── __tests__/       # Service tests (mocked API calls)
├── stores/              # Pinia stores for this feature
│   └── __tests__/       # Store tests
├── types/               # TypeScript types/interfaces
├── views/               # Top-level views (pages) for this feature
│   └── __tests__/       # View/integration tests
└── routes.ts            # Feature routes (imported in main router)
```

**Testing Strategy:**
- **Unit Tests:** Components, composables, services, stores (using Vitest)
- **Integration Tests:** Views with mocked dependencies
- **E2E Tests:** Critical user flows (using Playwright) - in separate `/e2e` folder
- **Test Naming:** `ComponentName.spec.ts` or `functionName.spec.ts`
- **Coverage Target:** 80%+ for business logic, 60%+ overall

### Key Components

#### FlowGenerationView.vue

See full implementation in previous response (the main flow creation page).

#### FlowTimeline.vue

See full implementation in previous response (visual timeline with SVG).

#### Flow Generation Store (Pinia)

```typescript
// src/stores/flow-generation.store.ts

import { defineStore } from 'pinia';
import { ref } from 'vue';
import { flowGenerationService } from '@/services/api/flow-generation.service';
import type { GeneratedFlow, GenerateFlowRequest } from '@/types/flow-generation.types';

export const useFlowGenerationStore = defineStore('flowGeneration', () => {
  const currentFlow = ref<GeneratedFlow | null>(null);
  const isGenerating = ref(false);
  const error = ref<string | null>(null);

  const generateFlow = async (request: GenerateFlowRequest): Promise<GeneratedFlow> => {
    isGenerating.value = true;
    error.value = null;

    try {
      const response = await flowGenerationService.generateFlow(request);
      currentFlow.value = response;
      return response;
    } catch (err: any) {
      error.value = err.message || 'Failed to generate flow';
      throw err;
    } finally {
      isGenerating.value = false;
    }
  };

  const refineFlow = async (flowId: string, instruction: string): Promise<GeneratedFlow> => {
    isGenerating.value = true;
    error.value = null;

    try {
      const response = await flowGenerationService.refineFlow(flowId, { instruction });
      currentFlow.value = response;
      return response;
    } catch (err: any) {
      error.value = err.message || 'Failed to refine flow';
      throw err;
    } finally {
      isGenerating.value = false;
    }
  };

  const searchSteps = async (query: string) => {
    return await flowGenerationService.searchSteps(query);
  };

  const saveFlow = async (flow: GeneratedFlow) => {
    // Save to backend
    await flowGenerationService.saveFlow(flow);
    currentFlow.value = flow;
  };

  const reset = () => {
    currentFlow.value = null;
    error.value = null;
    isGenerating.value = false;
  };

  return {
    currentFlow,
    isGenerating,
    error,
    generateFlow,
    refineFlow,
    searchSteps,
    saveFlow,
    reset,
  };
});
```

#### API Service

```typescript
// src/services/api/flow-generation.service.ts

import axios from './axios-instance';
import type {
  GenerateFlowRequest,
  GenerateFlowResponse,
  RefineFlowRequest,
  StepSearchResult,
} from '@/types/flow-generation.types';

class FlowGenerationService {
  async generateFlow(request: GenerateFlowRequest): Promise<GenerateFlowResponse> {
    const { data } = await axios.post<GenerateFlowResponse>(
      '/api/flows/generate',
      request
    );
    return data;
  }

  async refineFlow(flowId: string, request: RefineFlowRequest): Promise<GenerateFlowResponse> {
    const { data } = await axios.post<GenerateFlowResponse>(
      `/api/flows/generate/refine/${flowId}`,
      request
    );
    return data;
  }

  async searchSteps(query: string): Promise<StepSearchResult[]> {
    const { data } = await axios.get<{ results: StepSearchResult[] }>(
      '/api/flows/generate/search-steps',
      { params: { query } }
    );
    return data.results;
  }

  async testFlow(flowId: string, testData: any): Promise<any> {
    const { data } = await axios.post(`/api/flows/generate/test/${flowId}`, {
      testData,
      dryRun: true,
    });
    return data;
  }

  async saveFlow(flow: GenerateFlowResponse): Promise<void> {
    await axios.put(`/api/flows/${flow.flowId}`, flow);
  }
}

export const flowGenerationService = new FlowGenerationService();
```

---

## UI/UX Redesign

This section defines the visual design system and page-by-page redesign specifications. **Important:** Review and finalize each page design BEFORE implementing it.

### Design System

#### Material Design 3 Principles

Automaton Studio will follow [Material Design 3](https://m3.material.io/) guidelines with Vuetify's implementation:

1. **Dynamic Color** - Consistent color theming with light/dark mode support
2. **Elevated Surfaces** - Clear visual hierarchy through elevation
3. **Responsive Layout** - Adaptive layouts for desktop, tablet, and mobile
4. **Motion** - Meaningful animations for state transitions
5. **Accessibility** - WCAG 2.1 AA compliance

#### Color Palette

```
Primary Colors:
├── Primary:        #1976D2 (Blue 700)      - Main actions, navigation
├── Primary Light:  #42A5F5 (Blue 400)      - Hover states
├── Primary Dark:   #0D47A1 (Blue 900)      - Active states

Secondary Colors:
├── Secondary:      #7C4DFF (Deep Purple A200) - AI features, highlights
├── Secondary Light:#B47CFF                    - AI hover states

Semantic Colors:
├── Success:        #4CAF50 (Green 500)     - Completed, running
├── Warning:        #FF9800 (Orange 500)    - Pending, attention
├── Error:          #F44336 (Red 500)       - Failed, errors
├── Info:           #2196F3 (Blue 500)      - Information

Neutral Colors:
├── Background:     #FAFAFA (Grey 50)       - Page background
├── Surface:        #FFFFFF                  - Cards, dialogs
├── Surface Variant:#F5F5F5 (Grey 100)      - Secondary surfaces
├── On Surface:     #212121 (Grey 900)      - Text on surfaces
├── Outline:        #BDBDBD (Grey 400)      - Borders, dividers

Dark Mode:
├── Background:     #121212
├── Surface:        #1E1E1E
├── Surface Variant:#2D2D2D
├── On Surface:     #E0E0E0
```

#### Typography

```
Font Family: Roboto (Material Design default)

Headings:
├── H1: 96px / Light    - Hero sections only
├── H2: 60px / Light    - Page titles
├── H3: 48px / Regular  - Section headers
├── H4: 34px / Regular  - Card titles
├── H5: 24px / Regular  - Subsection headers
├── H6: 20px / Medium   - Component headers

Body:
├── Body 1: 16px / Regular - Primary content
├── Body 2: 14px / Regular - Secondary content
├── Caption: 12px / Regular - Labels, hints
├── Overline: 10px / Medium - Category labels

Code:
├── Mono: 14px / JetBrains Mono - Code editor, logs
```

#### Spacing & Layout

```
Spacing Scale (8px base):
├── xs: 4px   - Tight spacing
├── sm: 8px   - Default component spacing
├── md: 16px  - Section spacing
├── lg: 24px  - Card padding
├── xl: 32px  - Section margins
├── xxl: 48px - Page sections

Grid System:
├── 12-column grid
├── Gutter: 24px
├── Margin: 24px (desktop), 16px (tablet), 8px (mobile)

Breakpoints:
├── xs: 0-599px      - Mobile
├── sm: 600-959px    - Tablet portrait
├── md: 960-1279px   - Tablet landscape
├── lg: 1280-1919px  - Desktop
├── xl: 1920px+      - Large desktop
```

#### Component Styling

```
Cards:
├── Border Radius: 12px
├── Elevation: 1 (rest), 4 (hover), 8 (selected)
├── Padding: 16px (compact), 24px (default)

Buttons:
├── Border Radius: 8px
├── Height: 36px (default), 28px (small), 48px (large)
├── Padding: 16px horizontal

Inputs:
├── Variant: Outlined (default)
├── Border Radius: 4px
├── Height: 56px (default), 40px (dense)

Navigation:
├── Rail Width: 72px (collapsed)
├── Drawer Width: 256px (expanded)
├── App Bar Height: 64px
```

---

### Global Layout

#### Application Shell

```
┌────────────────────────────────────────────────────────────────┐
│  App Bar (64px)                                    [User] [⚙️] │
│  Logo    Breadcrumb / Page Title              Search  Profile  │
├──────┬─────────────────────────────────────────────────────────┤
│      │                                                         │
│  N   │                                                         │
│  a   │                    Main Content Area                    │
│  v   │                                                         │
│      │              (Page-specific content)                    │
│  R   │                                                         │
│  a   │                                                         │
│  i   │                                                         │
│  l   │                                                         │
│      │                                                         │
│ 72px │                                                         │
├──────┴─────────────────────────────────────────────────────────┤
│  (Optional) Status Bar - Running executions, notifications     │
└────────────────────────────────────────────────────────────────┘
```

#### Navigation Rail (Left Side)

```
┌──────────┐
│   Logo   │  ← Automaton logo/icon
├──────────┤
│  🏠      │  Dashboard
│  ⚡      │  Flows (AI-First)
│  🌐      │  Apps
│  🧩      │  Steps
│  🖥️      │  Runners
│  🏢      │  Projects
├──────────┤
│  ─────   │  Divider
├──────────┤
│  ⚙️      │  Settings
│  📖      │  Docs
└──────────┘
```

**Behavior:**
- Default: Collapsed rail (72px) with icons + tooltips
- On hover/click: Expands to full drawer (256px) with labels
- Mobile: Bottom navigation bar instead

---

### Page Redesigns

#### 1. Dashboard Page

**Current Issues:**
- Generic layout, not specific to workflow automation
- Missing at-a-glance flow health status
- No quick actions

**New Design:**

```
┌─────────────────────────────────────────────────────────────────┐
│ Dashboard                                      [+ Create Flow]  │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌───────────┐ │
│  │ Active Flows│ │ Executions  │ │ Success Rate│ │  Runners  │ │
│  │     12      │ │    1,234    │ │    94.2%    │ │   3 / 5   │ │
│  │   ↑ 2 new   │ │  Last 24h   │ │   ↑ 1.2%   │ │  Online   │ │
│  └─────────────┘ └─────────────┘ └─────────────┘ └───────────┘ │
│                                                                 │
│  ┌────────────────────────────────┐ ┌─────────────────────────┐ │
│  │ Recent Executions              │ │ Quick Actions           │ │
│  │ ┌────────────────────────────┐ │ │                         │ │
│  │ │ ✅ Order Processing   2m   │ │ │ [🤖 Generate Flow]     │ │
│  │ │ ✅ Email Campaign     5m   │ │ │ [📱 Create App]        │ │
│  │ │ ❌ Data Sync         12m   │ │ │ [🧩 New Step]          │ │
│  │ │ ✅ Report Gen        15m   │ │ │                         │ │
│  │ │ ⏳ Backup Job      Running │ │ │ ─────────────────────   │ │
│  │ └────────────────────────────┘ │ │                         │ │
│  │           [View All →]         │ │ Recent Flows:           │ │
│  └────────────────────────────────┘ │ • Order Processing      │ │
│                                     │ • Customer Onboarding   │ │
│  ┌────────────────────────────────┐ │ • Daily Reports         │ │
│  │ Execution Trend (7 days)       │ └─────────────────────────┘ │
│  │ ████████████████████████████   │                             │
│  │ ▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░   │                             │
│  │ Mon Tue Wed Thu Fri Sat Sun   │                             │
│  └────────────────────────────────┘                             │
└─────────────────────────────────────────────────────────────────┘
```

**Key Changes:**
- Stats cards with trend indicators
- Prominent "Generate Flow" AI action
- Execution status timeline
- Visual execution trend chart

---

#### 2. Flows Page (AI-First)

**Current:** Drag-and-drop designer with step palette
**New:** Natural language input with AI generation

```
┌─────────────────────────────────────────────────────────────────┐
│ Flows                                        [My Flows ▼] [+ ] │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  🤖 What would you like to automate?                        ││
│  │  ┌─────────────────────────────────────────────────────────┐││
│  │  │ When a new order comes in via webhook, validate the     │││
│  │  │ payment with Stripe, send confirmation email, and       │││
│  │  │ update inventory in our database...                     │││
│  │  │                                                    ⏎   │││
│  │  └─────────────────────────────────────────────────────────┘││
│  │                                                             ││
│  │  Examples: "Send Slack notification when GitHub PR merged"  ││
│  │            "Backup database daily and upload to S3"         ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  ┌──────────────────────────────────────────────────────────────┤
│  │ Your Flows                              [Grid] [List] 🔍    │
│  ├──────────────────────────────────────────────────────────────┤
│  │ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ │
│  │ │ Order Processing│ │ Email Campaign  │ │ Data Sync       │ │
│  │ │                 │ │                 │ │                 │ │
│  │ │ 5 steps         │ │ 3 steps         │ │ 4 steps         │ │
│  │ │ ✅ Last: 2m ago │ │ ✅ Last: 5m ago │ │ ❌ Failed 12m   │ │
│  │ │                 │ │                 │ │                 │ │
│  │ │ [Run] [Edit] ⋮  │ │ [Run] [Edit] ⋮  │ │ [Run] [Edit] ⋮  │ │
│  │ └─────────────────┘ └─────────────────┘ └─────────────────┘ │
│  └──────────────────────────────────────────────────────────────┘
└─────────────────────────────────────────────────────────────────┘
```

**Flow Editor (After Generation/Edit):**

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Back    Order Processing                    [Test] [Save] [▶]│
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────────┐  ┌───────────────────────────────┐ │
│  │ Flow Timeline           │  │ Step Details                  │ │
│  │                         │  │                               │ │
│  │   ┌───────────────┐     │  │ Step: Validate Payment        │ │
│  │   │ 1. Webhook    │     │  │ ─────────────────────────────│ │
│  │   │   Trigger     │     │  │                               │ │
│  │   └───────┬───────┘     │  │ Description:                  │ │
│  │           │             │  │ Validates Stripe payment      │ │
│  │   ┌───────▼───────┐     │  │                               │ │
│  │   │ 2. Validate   │ ◀── │  │ Inputs:                       │ │
│  │   │   Payment     │     │  │ • paymentIntentId (string)    │ │
│  │   └───────┬───────┘     │  │                               │ │
│  │           │             │  │ Outputs:                      │ │
│  │   ┌───────▼───────┐     │  │ • isValid (bool)              │ │
│  │   │ 3. Update     │     │  │ • errorMessage (string)       │ │
│  │   │   Inventory   │     │  │                               │ │
│  │   └───────┬───────┘     │  │ [View Code] [Edit] [🤖 Refine]│ │
│  │           │             │  │                               │ │
│  │   ┌───────▼───────┐     │  └───────────────────────────────┘ │
│  │   │ 4. Send Email │     │                                    │
│  │   └───────────────┘     │  ┌───────────────────────────────┐ │
│  │                         │  │ 🤖 AI Assistant               │ │
│  │   [+ Add Step]          │  │ "Add error handling for       │ │
│  │                         │  │  failed payments"             │ │
│  └─────────────────────────┘  │                          [Go] │ │
│                               └───────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

**Key Changes:**
- Natural language input is primary interface
- Flow cards show last execution status
- Timeline view replaces complex visual designer
- AI refinement panel always available
- Step details in side panel

---

#### 3. Apps Page (Visual IDE)

**Current Issues:**
- Tabs don't render properly (Blazor bug)
- File tree interaction is problematic
- Preview is cramped

**New Design:**

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Back    My Web App                    [Preview] [Publish] ⋮  │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────┬─────────────────────────────────────┬─────────────┐ │
│ │ Files   │ index.html  styles.css  app.js  ✕   │   Preview   │ │
│ │         ├─────────────────────────────────────┤             │ │
│ │ 📁 src  │ 1  <!DOCTYPE html>                  │  ┌───────┐  │ │
│ │  📄 inde│ 2  <html lang="en">                 │  │       │  │ │
│ │  📄 styl│ 3  <head>                           │  │ Live  │  │ │
│ │  📄 app.│ 4    <title>My App</title>          │  │Preview│  │ │
│ │ 📁 asset│ 5    <link rel="stylesheet"         │  │       │  │ │
│ │  🖼️ logo│ 6          href="styles.css">       │  │       │  │ │
│ │         │ 7  </head>                          │  │       │  │ │
│ │         │ 8  <body>                           │  │       │  │ │
│ │ ────────│ 9    <div id="app">                 │  └───────┘  │ │
│ │ Backend │10      <h1>Hello World</h1>         │             │ │
│ │ 📁 steps│11    </div>                         │ [Desktop]   │ │
│ │  🧩 getU│12    <script src="app.js">          │ [Tablet]    │ │
│ │  🧩 save│13    </script>                      │ [Mobile]    │ │
│ │         │14  </body>                          │             │ │
│ │         │15  </html>                          │             │ │
│ │ [+ New] │                                     │             │ │
│ └─────────┴─────────────────────────────────────┴─────────────┘ │
│                                                                 │
│ ┌───────────────────────────────────────────────────────────────┤
│ │ 🤖 AI Assistant                                      [Expand]│
│ │ "Add a login form with email and password fields"       [Go] │
│ └───────────────────────────────────────────────────────────────┘
└─────────────────────────────────────────────────────────────────┘
```

**Key Changes:**
- Three-panel layout: Files | Editor | Preview
- Tabs work correctly (Vue.js/Vuetify)
- Resizable panels via drag handles
- Backend steps shown separately in file tree
- AI assistant panel at bottom
- Responsive preview toggles

---

#### 4. Steps Page

**Current:** Simple list of custom steps
**New:** Organized library with search and categories

```
┌─────────────────────────────────────────────────────────────────┐
│ Steps                                              [+ New Step] │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  🔍 Search steps...                     [All ▼] [Python ▼]     │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ My Steps (12)                                               ││
│  ├─────────────────────────────────────────────────────────────┤│
│  │ ┌───────────────────┐ ┌───────────────────┐ ┌─────────────┐ ││
│  │ │ 🐍 ValidateEmail  │ │ 🐍 SendSlackMsg   │ │ ⚡ RunQuery  │ ││
│  │ │                   │ │                   │ │              │ ││
│  │ │ Validates email   │ │ Sends message to  │ │ Executes SQL│ ││
│  │ │ addresses using   │ │ Slack channel     │ │ query       │ ││
│  │ │ regex + DNS       │ │                   │ │              │ ││
│  │ │                   │ │                   │ │              │ ││
│  │ │ Used in: 5 flows  │ │ Used in: 3 flows  │ │ Used in: 8  │ ││
│  │ │ [Edit] [Test] ⋮   │ │ [Edit] [Test] ⋮   │ │ [Edit] [⋮]  │ ││
│  │ └───────────────────┘ └───────────────────┘ └─────────────┘ ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ AI-Generated Steps (24)                    [Show/Hide]      ││
│  │ Steps created by AI that can be reused                      ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

**Step Editor:**

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Back    ValidateEmail                        [Test] [Save]   │
├─────────────────────────────────────────────────────────────────┤
│ ┌───────────────────────────────┬───────────────────────────────┤
│ │ Code                          │ Configuration                 │
│ │ ┌───────────────────────────┐ │                               │
│ │ │ import re                 │ │ Name: ValidateEmail           │
│ │ │ import dns.resolver       │ │ Display: Validate Email       │
│ │ │                           │ │ Language: [Python ▼]          │
│ │ │ def validate(email):      │ │                               │
│ │ │     pattern = r'^[\w\.-]+'│ │ ─────────────────────────────│
│ │ │     if not re.match(...): │ │                               │
│ │ │         return False      │ │ Inputs:                       │
│ │ │                           │ │ ┌─────────────────────────┐   │
│ │ │     # Check MX record     │ │ │ email (string) *        │   │
│ │ │     domain = email.split( │ │ │ [+ Add Input]           │   │
│ │ │     try:                  │ │ └─────────────────────────┘   │
│ │ │         dns.resolver...   │ │                               │
│ │ │     except:               │ │ Outputs:                      │
│ │ │         return False      │ │ ┌─────────────────────────┐   │
│ │ │                           │ │ │ isValid (bool)          │   │
│ │ │     return True           │ │ │ errorMessage (string)   │   │
│ │ │                           │ │ │ [+ Add Output]          │   │
│ │ └───────────────────────────┘ │ └─────────────────────────┘   │
│ │                               │                               │
│ │ 🤖 "Add timeout handling"     │ Used in Flows:                │
│ │                          [Go] │ • Order Processing            │
│ │                               │ • User Registration           │
│ └───────────────────────────────┴───────────────────────────────┘
└─────────────────────────────────────────────────────────────────┘
```

---

#### 5. Runners Page

```
┌─────────────────────────────────────────────────────────────────┐
│ Runners                                        [+ Add Runner]   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ ┌─────────────────────────────────────────────────────────┐ ││
│  │ │  🟢 production-runner-01                    [⋮]         │ ││
│  │ │  ──────────────────────────────────────────────────────│ ││
│  │ │  OS: Ubuntu 22.04        │  Last seen: Just now         │ ││
│  │ │  IP: 192.168.1.100       │  Executions: 1,234           │ ││
│  │ │  Version: 2.1.0          │  Status: Idle                │ ││
│  │ │                          │                              │ ││
│  │ │  [View Logs]  [Disconnect]                              │ ││
│  │ └─────────────────────────────────────────────────────────┘ ││
│  │                                                             ││
│  │ ┌─────────────────────────────────────────────────────────┐ ││
│  │ │  🟢 staging-runner                          [⋮]         │ ││
│  │ │  OS: Windows Server 2022 │  Last seen: 2 min ago        │ ││
│  │ │  IP: 10.0.0.50           │  Executions: 456             │ ││
│  │ └─────────────────────────────────────────────────────────┘ ││
│  │                                                             ││
│  │ ┌─────────────────────────────────────────────────────────┐ ││
│  │ │  🔴 dev-runner (Offline)                    [⋮]         │ ││
│  │ │  OS: macOS 14            │  Last seen: 3 days ago       │ ││
│  │ │  IP: 192.168.1.50        │  Executions: 89              │ ││
│  │ └─────────────────────────────────────────────────────────┘ ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

---

#### 6. Projects Page (Multi-Tenancy)

**Current:** Projects dropdown in top-left, basic project management
**New:** Enhanced project switcher + comprehensive project management

**Project Switcher (In App Bar):**

```
┌────────────────────────────────────────────────────────────────┐
│  [My Projects ▼]  Page Title                   [User] [⚙️]     │
└────────────────────────────────────────────────────────────────┘
     │
     └──> ┌─────────────────────────────────┐
          │ 🏢 Acme Corp                    │ ← Current
          │ 👤 Personal Workspace           │
          │ 🏢 Client Project X             │
          │ ───────────────────────────────│
          │ ➕ Create New Project           │
          │ ⚙️ Manage Projects              │
          └─────────────────────────────────┘
```

**Projects List Page:**

```
┌─────────────────────────────────────────────────────────────────┐
│ Projects                                      [+ Create Project] │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ Your Projects                                      🔍       ││
│  ├─────────────────────────────────────────────────────────────┤│
│  │ ┌─────────────────────┐ ┌─────────────────────┐            ││
│  │ │ 🏢 Acme Corp        │ │ 👤 Personal         │            ││
│  │ │                     │ │    Workspace        │            ││
│  │ │ Role: Admin         │ │                     │            ││
│  │ │ Members: 5          │ │ Role: Owner         │            ││
│  │ │ Flows: 12           │ │ Members: 1          │            ││
│  │ │ Apps: 3             │ │ Flows: 8            │            ││
│  │ │                     │ │ Apps: 2             │            ││
│  │ │ [Open] [Settings] ⋮ │ │ [Open] [Settings] ⋮ │            ││
│  │ └─────────────────────┘ └─────────────────────┘            ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ Shared With You                                             ││
│  ├─────────────────────────────────────────────────────────────┤│
│  │ ┌─────────────────────┐                                     ││
│  │ │ 🏢 Client Project X │                                     ││
│  │ │                     │                                     ││
│  │ │ Role: Developer     │                                     ││
│  │ │ Owner: john@acme.com│                                     ││
│  │ │ Flows: 5            │                                     ││
│  │ │                     │                                     ││
│  │ │ [Open]              │                                     ││
│  │ └─────────────────────┘                                     ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

**Project Settings Page:**

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Projects    Acme Corp                             [Delete]    │
├───────────────┬─────────────────────────────────────────────────┤
│               │                                                 │
│ General       │  Project Details                                │
│ Members       │  ─────────────────────────────────────────────  │
│ Resources     │                                                 │
│               │  Name: [Acme Corp                           ]   │
│               │  Description:                                   │
│               │  ┌──────────────────────────────────────────┐   │
│               │  │ Main production project for Acme Corp    │   │
│               │  │ workflows and automation                 │   │
│               │  └──────────────────────────────────────────┘   │
│               │                                                 │
│               │                           [Save Changes]        │
└───────────────┴─────────────────────────────────────────────────┘
```

**Project Members Tab:**

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Projects    Acme Corp                                         │
├───────────────┬─────────────────────────────────────────────────┤
│               │                                                 │
│ General       │  Team Members                   [+ Invite]      │
│ Members       │  ─────────────────────────────────────────────  │
│ Resources     │                                                 │
│               │  ┌────────────────────────────────────────────┐ │
│               │  │ 👤 John Doe (You)          [Admin ▼] [⋮] │ │
│               │  │    john@acme.com                           │ │
│               │  │    Last active: 5 min ago                  │ │
│               │  ├────────────────────────────────────────────┤ │
│               │  │ 👤 Jane Smith              [Developer ▼] [⋮]││
│               │  │    jane@acme.com                           │ │
│               │  │    Last active: 2 hours ago                │ │
│               │  ├────────────────────────────────────────────┤ │
│               │  │ 👤 Bob Wilson              [Viewer ▼] [⋮] │ │
│               │  │    bob@acme.com                            │ │
│               │  │    Last active: 1 day ago                  │ │
│               │  └────────────────────────────────────────────┘ │
│               │                                                 │
│               │  Pending Invitations                            │
│               │  ─────────────────────────────────────────────  │
│               │  ┌────────────────────────────────────────────┐ │
│               │  │ 📧 alice@acme.com          [Developer] [✕]│ │
│               │  │    Invited: 2 days ago                     │ │
│               │  │    [Resend Invite]                         │ │
│               │  └────────────────────────────────────────────┘ │
│               │                                                 │
│               │  Roles:                                         │
│               │  • Admin - Full control                         │
│               │  • Developer - Create/edit flows, apps, steps   │
│               │  • AppUser - Execute apps only                  │
│               │  • Viewer - Read-only access                    │
└───────────────┴─────────────────────────────────────────────────┘
```

**Project Resources Tab:**

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Projects    Acme Corp                                         │
├───────────────┬─────────────────────────────────────────────────┤
│               │                                                 │
│ General       │  Project Resources                              │
│ Members       │  ─────────────────────────────────────────────  │
│ Resources     │                                                 │
│               │  Flows (12)                         [View All]  │
│               │  ┌────────────────────────────────────────────┐ │
│               │  │ ⚡ Order Processing                        │ │
│               │  │ ⚡ Email Campaign                          │ │
│               │  │ ⚡ Data Sync                               │ │
│               │  └────────────────────────────────────────────┘ │
│               │                                                 │
│               │  Apps (3)                           [View All]  │
│               │  ┌────────────────────────────────────────────┐ │
│               │  │ 🌐 Customer Portal                        │ │
│               │  │ 🌐 Admin Dashboard                        │ │
│               │  └────────────────────────────────────────────┘ │
│               │                                                 │
│               │  Runners (2)                        [View All]  │
│               │  ┌────────────────────────────────────────────┐ │
│               │  │ 🟢 production-runner-01                   │ │
│               │  │ 🟢 staging-runner                         │ │
│               │  └────────────────────────────────────────────┘ │
│               │                                                 │
│               │  Custom Steps (8)                   [View All]  │
│               │  ┌────────────────────────────────────────────┐ │
│               │  │ 🐍 ValidateEmail                          │ │
│               │  │ 🐍 SendSlackMsg                           │ │
│               │  └────────────────────────────────────────────┘ │
└───────────────┴─────────────────────────────────────────────────┘
```

**Key Changes:**
- Project switcher in app bar (top-left)
- Projects organized into "Your Projects" and "Shared With You"
- Project cards show member count, resource counts
- Comprehensive member management with role assignment
- Pending invitations tracking
- Resource overview showing all project-linked items
- Clear role definitions with permissions

---

#### 7. Settings Page

```
┌─────────────────────────────────────────────────────────────────┐
│ Settings                                                        │
├────────────────┬────────────────────────────────────────────────┤
│                │                                                │
│ Profile        │  Account Settings                              │
│ ────────────── │  ──────────────────────────────────────────── │
│ 👤 Account     │                                                │
│ 🔑 API Keys    │  Email                                         │
│ 🤖 AI Keys     │  ┌────────────────────────────────────────┐   │
│                │  │ user@example.com                       │   │
│ Preferences    │  └────────────────────────────────────────┘   │
│ ────────────── │                                                │
│ 🎨 Appearance  │  Display Name                                  │
│ 🔔 Notificatio │  ┌────────────────────────────────────────┐   │
│                │  │ John Doe                               │   │
│ Organization   │  └────────────────────────────────────────┘   │
│ ────────────── │                                                │
│ 👥 Team        │  Password                                      │
│ 📊 Usage       │  [Change Password]                             │
│                │                                                │
│                │                              [Save Changes]    │
└────────────────┴────────────────────────────────────────────────┘
```

---

### Dark Mode

All pages support dark mode toggle in the app bar:

```
Light Mode Toggle: ☀️ → 🌙
Dark Mode Toggle:  🌙 → ☀️

Automatic: Follow system preference (default)
```

Dark mode colors defined in Color Palette section above.

---

### Responsive Behavior

| Breakpoint | Navigation | Layout Changes |
|------------|------------|----------------|
| **Desktop** (1280px+) | Rail (72px) + expandable | Full multi-panel layouts |
| **Tablet** (600-1279px) | Hamburger menu | Stacked panels, collapsible preview |
| **Mobile** (<600px) | Bottom navigation | Single column, tabbed content |

---

### Animation & Transitions

```
Page Transitions:
├── Enter: Fade in + slide up (200ms)
├── Exit: Fade out (150ms)

Component Transitions:
├── Cards: Elevation change on hover (150ms ease)
├── Buttons: Background color change (100ms)
├── Panels: Resize with spring easing (300ms)

Loading States:
├── Skeleton loaders for cards
├── Circular progress for actions
├── Linear progress for page loads
```

---

### Design Checklist (Per Page)

Before implementing each page, verify:

- [ ] Color usage follows the palette
- [ ] Typography hierarchy is correct
- [ ] Spacing uses the 8px grid
- [ ] Cards have proper elevation
- [ ] Responsive breakpoints defined
- [ ] Dark mode colors specified
- [ ] Loading states designed
- [ ] Error states designed
- [ ] Empty states designed
- [ ] Accessibility (contrast, focus states)

---

## Implementation Strategy

### Design-First Workflow

**IMPORTANT:** Before implementing any page, follow this process:

1. **Review Design Spec** - Check the page design in [UI/UX Redesign](#uiux-redesign) section
2. **Verify Checklist** - Complete the Design Checklist for the page
3. **Create Component Stubs** - Build component structure with Vuetify components
4. **Implement Functionality** - Add logic, API calls, state management
5. **Test Responsive Behavior** - Verify desktop, tablet, and mobile layouts
6. **Dark Mode Testing** - Ensure colors work in both themes

### Development Phases

#### Phase 1: Foundation (Month 1)

**Weeks 1-2: Vue.js Project Setup + Design System**
- [ ] Create Vue 3 + Vite + TypeScript project
- [ ] Install Vuetify, Pinia, Vue Router, Axios
- [ ] Configure TypeScript, ESLint, Prettier
- [ ] **Configure Vuetify theme with design system colors**
- [ ] **Set up typography (Roboto + JetBrains Mono)**
- [ ] **Create spacing/layout utility classes**
- [ ] Set up dev/prod environment configs
- [ ] **Build application shell (App Bar + Navigation Rail)**
- [ ] Implement authentication (login/register pages)
- [ ] Connect to existing API for auth

**Weeks 3-4: Backend Flow Generation API**
- [ ] Create `FlowGenerationController`
- [ ] Implement `FlowGenerationService`
- [ ] Integrate with existing AI provider (Mcp)
- [ ] Add step search/reuse logic
- [ ] Write unit tests for AI prompt building
- [ ] Test with real AI model (OpenAI/Anthropic)

**Deliverable:** Working API that generates flows from natural language + authenticated Vue.js shell with design system

#### Phase 2: Core UI (Month 2)

**Weeks 1-2: Dashboard + Flow Generation UI**
- [ ] **Review Dashboard design spec** (see Section 7.1)
- [ ] Build `DashboardView` with stats cards
- [ ] Implement execution trend chart
- [ ] Add quick actions panel
- [ ] **Review Flows page design spec** (see Section 7.2)
- [ ] Build `FlowGenerationView` component
- [ ] Implement natural language input with examples
- [ ] Create `FlowTimeline` visualization component
- [ ] Build `StepCard` component with expand/collapse
- [ ] Add step reuse suggestions
- [ ] Implement flow refinement dialog
- [ ] Connect to Flow Generation API

**Weeks 3-4: Apps View**
- [ ] **Review Apps page design spec** (see Section 7.3)
- [ ] Build `AppListView` (list of apps with cards)
- [ ] Build `AppEditorView` with three-panel layout
- [ ] Integrate Monaco Editor (direct, no wrapper!)
- [ ] Implement multi-file tab editor
- [ ] Add resizable panels
- [ ] Add app preview iframe with responsive toggles
- [ ] Connect to existing Apps API

**Deliverable:** Can create flows and apps in new Vue.js UI

#### Phase 3: Feature Parity (Month 3)

**Weeks 1-2: Steps & Execution**
- [ ] **Review Steps page design spec** (see Section 7.4)
- [ ] Build `StepListView` with search and filters
- [ ] Build `StepEditorView` with Monaco + config panel
- [ ] Port execution history views
- [ ] Implement real-time execution monitoring (SignalR client)
- [ ] Add flow execution logs display

**Weeks 3-4: Projects, Runners, Settings**
- [ ] **Review Projects page design spec** (see Section 7.6)
- [ ] Build project switcher in app bar
- [ ] Build Projects list view (Your Projects + Shared)
- [ ] Build Project settings page with tabs (General, Members, Resources)
- [ ] Implement member invitation and role management
- [ ] **Review Runners page design spec** (see Section 7.5)
- [ ] Implement runners management page with status cards
- [ ] **Review Settings page design spec** (see Section 7.7)
- [ ] Port settings pages (account, API keys, AI keys)
- [ ] Implement appearance settings (dark mode toggle)
- [ ] Ensure all existing features are available

**Deliverable:** Full feature complete

#### Phase 4: Polish & Launch (Month 4)

**Week 1-2: Design QA & Bug Fixes**
- [ ] **Design review of all pages against specs**
- [ ] Verify consistent spacing, typography, colors
- [ ] End-to-end testing of all workflows
- [ ] Fix critical bugs
- [ ] Performance optimization (bundle size, lazy loading)
- [ ] Accessibility audit (WCAG compliance)
- [ ] **Dark mode testing across all pages**

**Week 3: Beta Testing**
- [ ] Deploy Vue.js UI to beta subdomain (beta.automaton.com)
- [ ] Invite beta testers
- [ ] Collect feedback on design and usability
- [ ] Fix issues
- [ ] Monitor error logs, performance metrics

**Week 4: Production Launch**
- [ ] Deploy Vue.js UI as default
- [ ] Update documentation
- [ ] Monitor metrics closely

### URL Routing

```
Production URLs:

/                           → Vue.js UI
/api/*                      → ASP.NET API
/web/{appSlug}              → Published apps

Development:
http://localhost:5173       → Vue.js dev server (Vite)
http://localhost:5000       → ASP.NET API
```

### Deployment Architecture

```
┌─────────────────────────────────────────┐
│         Web Server (IIS/Nginx)          │
├─────────────────────────────────────────┤
│  /              → /wwwroot/vue/         │  ← Vue.js SPA
│  /api/*         → ASP.NET Core API      │  ← Backend
│  /web/{slug}    → Published apps        │
└─────────────────────────────────────────┘
```

---

## Timeline & Milestones

### High-Level Timeline

```
Month 1 (Jan 2026)
├─ Week 1-2: Vue.js foundation + auth
├─ Week 3-4: Backend flow generation API
└─ Milestone: Working flow generation API

Month 2 (Feb 2026)
├─ Week 1-2: Flow generation UI
├─ Week 3-4: Apps editor with Monaco
└─ Milestone: Can create flows and apps

Month 3 (Mar 2026)
├─ Week 1-2: Steps, execution, monitoring
├─ Week 3-4: Dashboard, settings
└─ Milestone: Feature parity achieved

Month 4 (Apr 2026)
├─ Week 1-2: Testing, bug fixes
├─ Week 3: Beta testing
├─ Week 4: Production launch
└─ Milestone: Vue.js UI live in production
```

### Critical Path Items

1. **Flow Generation API** - Blocking for all flow-related UI work
2. **Monaco Editor Integration** - Blocking for apps/steps editors
3. **Authentication** - Blocking for all secured pages
4. **API Type Generation** - Needed early for TypeScript types

---

## Risk Assessment

### Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| AI-generated flows produce buggy code | High | High | - Extensive validation in `FlowGenerationService`<br>- Dry-run testing before deployment<br>- User can edit generated code<br>- Start with simple flows, iterate |
| Monaco Editor performance issues with large files | Medium | Medium | - Implement virtualization<br>- Lazy load editor<br>- File size warnings |
| Type generation from C# API breaks | Low | High | - Lock openapi-typescript version<br>- Add to CI/CD pipeline<br>- Manual type definitions as fallback |
| SignalR client in Vue.js has issues | Low | Medium | - Use official @microsoft/signalr package<br>- Test thoroughly<br>- Fallback to polling |

### Business Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Development takes longer than 4 months | Medium | High | - Start with MVP (flows + apps only)<br>- Incremental releases<br>- Allocate buffer time |
| AI costs become too high | Low | High | - Cache AI responses<br>- Rate limit generation requests<br>- Offer usage tiers |

### Mitigation Strategies

1. **Feature Flags:** Toggle new features on/off without deployment
2. **Monitoring:** Error tracking (Sentry), performance (Lighthouse), usage analytics
3. **Incremental Development:** Build core features first, add polish later

---

## Success Metrics

### Technical Metrics

- **Initial Load Time:** < 2s (Vue.js should be faster than Blazor WASM)
- **Bundle Size:** < 500KB (gzipped)
- **Time to Interactive:** < 3s
- **Lighthouse Score:** > 90
- **Error Rate:** < 1% of requests

### Product Metrics

- **Flow Generation Success Rate:** > 80% (AI generates working flow on first try)
- **Step Reuse Rate:** > 30% (steps reused vs. newly generated)
- **Time to Create Flow:** < 5 minutes (vs. 20+ minutes with drag-and-drop)
- **User Adoption:** > 70% of active users try flow generation within 2 weeks

### User Satisfaction

- **NPS Score:** > 40
- **Feature Feedback:** Survey after 1 month (target: 4/5 stars)
- **Support Tickets:** < 10% increase (despite major UI change)

---

## Appendix

### Glossary

- **Flow:** A workflow/automation consisting of multiple steps
- **Step:** Individual action (Python/PowerShell/Batch code)
- **App:** Visual web application with Monaco editor
- **Runner:** Agent that executes flows on remote machines
- **AI-First:** Design philosophy where AI generates solutions rather than pre-built components

### References

- [Vuetify Documentation](https://vuetifyjs.com/)
- [Vue 3 Documentation](https://vuejs.org/)
- [Vite Documentation](https://vitejs.dev/)
- [Pinia Documentation](https://pinia.vuejs.org/)
- [Monaco Editor Documentation](https://microsoft.github.io/monaco-editor/)

### Related Documents

- `docs/vue-redesign/API_SPECIFICATION.md` - Detailed API specs
- `docs/vue-redesign/UI_MOCKUPS.md` - UI/UX designs
- `docs/vue-redesign/USER_GUIDE.md` - User guide for the new UI

---

**Document Version:** 1.1
**Last Updated:** January 6, 2026
**Status:** ✅ Approved for Implementation
