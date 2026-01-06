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

Automaton Studio follows [Material Design 3](https://m3.material.io/) guidelines using Vuetify's default implementation:

1. **Dynamic Color** - Consistent color theming with light/dark mode support
2. **Elevated Surfaces** - Clear visual hierarchy through elevation
3. **Responsive Layout** - Adaptive layouts for desktop, tablet, and mobile
4. **Motion** - Meaningful animations for state transitions
5. **Accessibility** - WCAG 2.1 AA compliance

**Note:** We use Vuetify's **default theme, colors, typography, and spacing**. This ensures:
- Consistent Material Design 3 implementation
- Automatic updates when Vuetify evolves
- No custom theme maintenance overhead
- Battle-tested accessibility and responsive behavior

#### Color Palette

We use **Vuetify's default color palette** with built-in light/dark mode support:
- **Primary:** Blue (configurable per Vuetify's theme system)
- **Secondary, Success, Warning, Error, Info:** Vuetify defaults
- **Surface & Background:** Material Design 3 defaults
- **Dark Mode:** Automatic theme switching

See: [Vuetify Theme Configuration](https://vuetifyjs.com/en/features/theme/)

#### Typography

We use **Vuetify's default typography** based on Material Design 3:
- **Font Family:** Roboto (loaded via Vuetify)
- **Headings:** h1-h6 with Material Design 3 scale
- **Body Text:** Material Design 3 text styles
- **Code/Monospace:** Use `class="font-mono"` with JetBrains Mono (loaded separately for code editors)

See: [Vuetify Typography](https://vuetifyjs.com/en/styles/text-and-typography/)

#### Spacing & Layout

We use **Vuetify's default spacing system** (4px base unit):
- **Spacing:** `ma-1` to `ma-16` (margins), `pa-1` to `pa-16` (padding)
- **Grid System:** 12-column responsive grid with built-in breakpoints
- **Breakpoints:** `xs`, `sm`, `md`, `lg`, `xl` (Vuetify defaults)

See: [Vuetify Spacing](https://vuetifyjs.com/en/styles/spacing/)

#### Component Styling

We use **Vuetify's default component styles**:
- **Cards:** Default border radius, elevation, and padding
- **Buttons:** Default variants (flat, outlined, text, etc.)
- **Inputs:** Default variants (outlined, filled, solo, etc.)
- **Navigation:** Default navigation drawer and app bar dimensions

**Custom overrides:** Only when necessary for code editor integration (Monaco) or specific UX requirements

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

**IMPORTANT:** We follow a **stub-first, then functionality** approach:

**Phase A: Create All Page Stubs (Phase 1.2)**
1. **Review All Design Specs** - Read through all page designs in [UI/UX Redesign](#uiux-redesign) section
2. **Create Page Structure** - Build all pages as Vue components with proper layout structure
3. **Use Placeholder Content** - Static text, empty cards, skeleton loaders where data would go
4. **Implement Routing** - Wire up all routes so you can navigate between pages
5. **Test Navigation Flow** - Verify the entire app navigation works end-to-end
6. **Responsive & Dark Mode** - Ensure all page layouts work on mobile/tablet/desktop and in dark mode

**Phase B: Implement Functionality (Phases 2-3)**

For each page/feature, follow this process:

1. **Review Design Spec** - Check the specific page design in [UI/UX Redesign](#uiux-redesign) section
2. **Verify Checklist** - Complete the Design Checklist for the page
3. **Add State Management** - Create Pinia stores for the feature
4. **Implement API Services** - Create service classes for API calls
5. **Connect Components** - Replace placeholder content with real data
6. **Add Interactivity** - Wire up buttons, forms, dialogs
7. **Test Responsive Behavior** - Verify desktop, tablet, and mobile layouts with real data
8. **Dark Mode Testing** - Ensure colors work in both themes with all states

**Why Stub-First?**
- See the entire app structure immediately
- Identify navigation/UX issues early
- Work on features in parallel without breaking other pages
- Easy to demo progress to stakeholders
- Reduces risk of architectural changes later

### Page Stub Implementation Guide (Phase 1.2)

**Goal:** Create visual structure for all pages using Vuetify components with placeholder/static content.

**Example Stub Structure:**

```vue
<script setup lang="ts">
// No logic yet - just imports
import { ref } from 'vue'

// Placeholder data
const mockData = ref([
  { id: 1, name: 'Item 1', status: 'active' },
  { id: 2, name: 'Item 2', status: 'pending' },
])
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h5 font-weight-bold">Page Title</h1>
        <p class="text-body-2 text-medium-emphasis">Page description</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus">
        Create New
      </v-btn>
    </div>

    <!-- Main Content Area -->
    <v-row>
      <v-col cols="12" md="8">
        <v-card>
          <v-card-title>Section Title</v-card-title>
          <v-divider />
          <v-card-text>
            <!-- Placeholder content -->
            <v-skeleton-loader type="list-item-three-line" />
          </v-card-text>
        </v-card>
      </v-col>

      <v-col cols="12" md="4">
        <v-card>
          <v-card-title>Side Panel</v-card-title>
          <v-divider />
          <v-card-text>
            <p class="text-body-2 text-medium-emphasis">
              This panel will show details...
            </p>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
```

**Key Principles for Stubs:**
- ✅ Use Vuetify components only (v-card, v-btn, v-row, etc.)
- ✅ Follow the layout structure from the design spec exactly
- ✅ Use `v-skeleton-loader` for data areas
- ✅ Use placeholder text for descriptions
- ✅ Include all major UI sections (headers, panels, sidebars)
- ✅ Wire up navigation (buttons can route to other stubs)
- ❌ No API calls yet
- ❌ No complex state management yet
- ❌ No real data loading
- ❌ Forms can be present but don't submit

**Checklist per Page Stub:**
- [ ] Matches layout from design spec
- [ ] Responsive (test on mobile/tablet/desktop)
- [ ] Dark mode works
- [ ] Navigation links work
- [ ] All major UI sections present

### Development Phases

#### Phase 1: Project Setup & Foundation

**1.1: Vue.js Project Initialization**
- [ ] Create Vue 3 + Vite + TypeScript project
- [ ] Install Vuetify, Pinia, Vue Router, Axios
- [ ] Configure TypeScript, ESLint, Prettier
- [ ] Configure Vuetify with default Material Design 3 theme
- [ ] Set up JetBrains Mono font for code editors
- [ ] Set up dev/prod environment configs
- [ ] Build application shell (App Bar + Navigation Rail + Router)

**1.2: Page Structure & Stubs**
- [ ] **Create all page view stubs following the UI/UX design specs**
- [ ] **Auth Pages Stub** - Login and Register forms with validation
- [ ] **Dashboard Page Stub** - Layout with stat cards placeholders, charts area, quick actions panel
- [ ] **Flows List Page Stub** - Layout with AI input area, flow cards grid/list toggle
- [ ] **Flow Generation Page Stub** - Three-column layout (timeline, details, AI assistant)
- [ ] **Apps List Page Stub** - Layout with app cards grid
- [ ] **App Editor Page Stub** - Three-panel layout (file tree, editor, preview)
- [ ] **Steps List Page Stub** - Layout with step cards, search, filters
- [ ] **Step Editor Page Stub** - Two-panel layout (code editor, configuration)
- [ ] **Runners Page Stub** - Layout with runner status cards
- [ ] **Projects List Page Stub** - Layout with project cards (yours + shared)
- [ ] **Project Settings Page Stub** - Tabs layout (general, members, resources)
- [ ] **Settings Page Stub** - Sidebar + content area layout
- [ ] Wire up routing for all pages
- [ ] Add navigation items to app shell
- [ ] **Verify responsive breakpoints work on all page stubs**
- [ ] **Test dark mode toggle on all page stubs**

**Deliverable:** Vue.js shell with all page stubs navigable

#### Phase 2: Authentication & Dashboard

**2.1: Authentication**
- [ ] **Follow Project Structure** for `auth/` feature (see Section 6)
- [ ] Implement authentication feature
- [ ] Build `LoginView` with form validation
- [ ] Build `RegisterView` with email confirmation
- [ ] Create `useAuth` composable for auth state management
- [ ] Implement auth store (Pinia) with JWT token handling
- [ ] Create auth service for API calls (login, register, logout, refresh)
- [ ] Add auth guards to router (protected routes)
- [ ] Connect to existing API for authentication

**2.2: Dashboard**
- [ ] **Follow Project Structure** for `dashboard/` feature (see Section 6)
- [ ] **Review Dashboard design spec** (see Section 7.1)
- [ ] **Review Blazor implementation** for feature parity
- [ ] Build `DashboardView` with stats cards
- [ ] Implement execution trend chart
- [ ] Add quick actions panel
- [ ] Connect to existing Dashboard API

**Deliverable:** Authentication working + functional dashboard

#### Phase 3: Feature Parity

**Important:** For all Phase 3 features, review the existing Blazor implementation to ensure feature parity while applying the new Vue.js design system.

**3.1: Flow Generation**
- [ ] **Follow Project Structure** for `flows/` feature (see Section 6)
- [ ] **Backend Flow Generation API**
- [ ] Create `FlowGenerationController`
- [ ] Implement `FlowGenerationService`
- [ ] Integrate with existing AI provider (Mcp)
- [ ] Add step search/reuse logic
- [ ] Write unit tests for AI prompt building
- [ ] Test with real AI model (OpenAI/Anthropic)
- [ ] **Frontend Flow Generation UI**
- [ ] **Review Flows page design spec** (see Section 7.2)
- [ ] **Review Blazor Flows implementation** for feature parity
- [ ] Build `FlowGenerationView` component
- [ ] Implement natural language input with examples
- [ ] Create `FlowTimeline` visualization component
- [ ] Build `StepCard` component with expand/collapse
- [ ] Add step reuse suggestions
- [ ] Implement flow refinement dialog
- [ ] Connect to Flow Generation API

**3.2: Flow Management & Execution**
- [ ] **Follow Project Structure** for `flows/` feature (see Section 6)
- [ ] **Review Blazor Flow management** for feature parity
- [ ] Build `FlowListView` with grid/list toggle
- [ ] Build `FlowEditorView` for manual flow editing
- [ ] Implement flow execution controls (run, test, schedule)
- [ ] Port execution history views
- [ ] Implement real-time execution monitoring (SignalR client)
- [ ] Add flow execution logs display

**3.3: Apps**
- [ ] **Follow Project Structure** for `apps/` feature (see Section 6)
- [ ] **Review Apps page design spec** (see Section 7.3)
- [ ] **Review Blazor Apps implementation** for feature parity
- [ ] Build `AppListView` (list of apps with cards)
- [ ] Build `AppEditorView` with three-panel layout
- [ ] Integrate Monaco Editor (direct, no wrapper!)
- [ ] Implement multi-file tab editor
- [ ] Add resizable panels
- [ ] Add app preview iframe with responsive toggles
- [ ] Connect to existing Apps API
- [ ] Port app publish/unpublish functionality
- [ ] Implement PWA settings

**3.4: Steps**
- [ ] **Follow Project Structure** for `steps/` feature (see Section 6)
- [ ] **Review Steps page design spec** (see Section 7.4)
- [ ] **Review Blazor Steps implementation** for feature parity
- [ ] Build `StepListView` with search and filters
- [ ] Build `StepEditorView` with Monaco + config panel
- [ ] Implement step testing functionality
- [ ] Port step execution and debugging features

**3.5: Projects**
- [ ] **Follow Project Structure** for `projects/` feature (see Section 6)
- [ ] **Review Projects page design spec** (see Section 7.6)
- [ ] **Review Blazor Projects implementation** for feature parity
- [ ] Build project switcher in app bar
- [ ] Build Projects list view (Your Projects + Shared)
- [ ] Build Project settings page with tabs (General, Members, Resources)
- [ ] Implement member invitation and role management
- [ ] Port project-level permissions and access control

**3.6: Runners**
- [ ] **Follow Project Structure** for `runners/` feature (see Section 6)
- [ ] **Review Runners page design spec** (see Section 7.5)
- [ ] **Review Blazor Runners implementation** for feature parity
- [ ] Implement runners management page with status cards
- [ ] Port runner connection and monitoring features
- [ ] Add runner registration and configuration UI
- [ ] Implement real-time runner status updates

**3.7: Settings**
- [ ] **Follow Project Structure** for `settings/` feature (see Section 6)
- [ ] **Review Settings page design spec** (see Section 7.7)
- [ ] **Review Blazor Settings implementation** for feature parity
- [ ] Port settings pages (account, API keys, AI keys)
- [ ] Implement appearance settings (dark mode toggle)
- [ ] Add notification preferences
- [ ] Ensure all existing features are available

**Deliverable:** Full feature parity with existing Blazor application

#### Phase 4: Polish & Launch

**4.1: Quality Assurance**
- [ ] **Design review of all pages against specs**
- [ ] Verify consistent spacing, typography, colors
- [ ] End-to-end testing of all workflows
- [ ] Fix critical bugs
- [ ] Performance optimization (bundle size, lazy loading)
- [ ] Accessibility audit (WCAG compliance)
- [ ] **Dark mode testing across all pages**

**4.2: Beta Testing**
- [ ] Deploy Vue.js UI to beta subdomain (beta.automaton.com)
- [ ] Invite beta testers
- [ ] Collect feedback on design and usability
- [ ] Fix issues
- [ ] Monitor error logs, performance metrics

**4.3: Production Launch**
- [ ] Deploy Vue.js UI as default
- [ ] Update documentation
- [ ] Monitor metrics closely

**Deliverable:** Vue.js UI live in production

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

## AI-Assisted Implementation Guide

This section provides specific instructions for AI agents/models implementing this redesign, including strategies for parallel development.

### Parallel Development Strategy

#### Phase 1: Sequential Only
**Phase 1 must be completed sequentially** - it creates the foundation that all other work depends on.

- ✅ **1.1** must complete first (project setup)
- ✅ **1.2** must complete second (all page stubs)
- ❌ Cannot parallelize Phase 1 - it's foundational

#### Phase 2: Can Be Parallelized After Phase 1

Once Phase 1 is complete, Phase 2 tasks can run in parallel:

**Parallel Tracks for Phase 2:**

```
PR #1: Phase 2.1 - Authentication
├─ Branch: feature/phase-2.1-authentication
├─ Dependencies: Phase 1 complete
├─ Blocked by: None
├─ Blocks: All Phase 3 work (need auth guards)
└─ Can start: Immediately after Phase 1

PR #2: Phase 2.2 - Dashboard  
├─ Branch: feature/phase-2.2-dashboard
├─ Dependencies: Phase 1 complete, Phase 2.1 (auth guards)
├─ Blocked by: Phase 2.1 (needs authenticated API calls)
├─ Blocks: Nothing
└─ Can start: After Phase 2.1 completes
```

**Note:** Phase 2.2 depends on 2.1 because it needs authentication to make API calls. They cannot run truly in parallel.

#### Phase 3: Maximum Parallelization

Phase 3 has the most opportunities for parallel work. After Phase 2 completes, you can run **up to 7 parallel PRs**:

**Parallel Tracks for Phase 3:**

```
PR #3: Phase 3.1 - Flow Generation
├─ Branch: feature/phase-3.1-flow-generation
├─ Files: features/flows/ (generation components only)
├─ Dependencies: Phase 2 complete
├─ Blocked by: None
├─ Blocks: Phase 3.2 (same feature folder)
├─ Conflicts with: Phase 3.2 (same feature - must do sequentially)
└─ Can start: Immediately after Phase 2

PR #4: Phase 3.2 - Flow Management
├─ Branch: feature/phase-3.2-flow-management  
├─ Files: features/flows/ (management components)
├─ Dependencies: Phase 2 complete, Phase 3.1 complete
├─ Blocked by: Phase 3.1 (same feature folder)
├─ Blocks: Nothing
└─ Can start: After Phase 3.1 completes

PR #5: Phase 3.3 - Apps
├─ Branch: feature/phase-3.3-apps
├─ Files: features/apps/
├─ Dependencies: Phase 2 complete
├─ Blocked by: None
├─ Blocks: Nothing
├─ Conflicts with: None
└─ Can start: Immediately after Phase 2 (parallel with 3.1)

PR #6: Phase 3.4 - Steps
├─ Branch: feature/phase-3.4-steps
├─ Files: features/steps/
├─ Dependencies: Phase 2 complete
├─ Blocked by: None
├─ Blocks: Nothing
├─ Conflicts with: None
└─ Can start: Immediately after Phase 2 (parallel with 3.1, 3.3)

PR #7: Phase 3.5 - Projects
├─ Branch: feature/phase-3.5-projects
├─ Files: features/projects/
├─ Dependencies: Phase 2 complete
├─ Blocked by: None
├─ Blocks: Nothing
├─ Conflicts with: None
└─ Can start: Immediately after Phase 2 (parallel with 3.1, 3.3, 3.4)

PR #8: Phase 3.6 - Runners
├─ Branch: feature/phase-3.6-runners
├─ Files: features/runners/
├─ Dependencies: Phase 2 complete
├─ Blocked by: None
├─ Blocks: Nothing
├─ Conflicts with: None
└─ Can start: Immediately after Phase 2 (parallel with 3.1, 3.3, 3.4, 3.5)

PR #9: Phase 3.7 - Settings
├─ Branch: feature/phase-3.7-settings
├─ Files: features/settings/
├─ Dependencies: Phase 2 complete
├─ Blocked by: None
├─ Blocks: Nothing
├─ Conflicts with: None
└─ Can start: Immediately after Phase 2 (parallel with 3.1, 3.3, 3.4, 3.5, 3.6)
```

**Optimal Phase 3 Parallelization Strategy:**

1. **Wave 1 (Immediately after Phase 2):** Start 3.1, 3.3, 3.4, 3.5, 3.6, 3.7 in parallel (6 PRs)
2. **Wave 2 (After 3.1 completes):** Start 3.2 (1 PR)

**File Conflict Prevention:**
- Phase 3 sub-phases work in **separate feature folders**
- Only exception: 3.1 and 3.2 both touch `features/flows/` - must be sequential
- All other Phase 3 work can run simultaneously
- Shared files (`shared/`, `router/`, `App.vue`) may need coordination

#### Phase 4: Sequential Validation

Phase 4 must be sequential - it's validation and cleanup work that requires all features to be complete.

### AI Agent Instructions Per Phase

#### Phase 1.1 - Project Initialization

**Prompt for AI Agent:**

```
Implement Phase 1.1 - Vue.js Project Initialization from REDESIGN_PLAN.md.

PREREQUISITE: Before starting, READ THE ENTIRE src/automaton-studio-vue/REDESIGN_PLAN.md file.
Pay special attention to:
- Section 6: Project Structure (Feature-Based)
- Section 8: Implementation Strategy
- Common Pitfalls for AI Agents (avoid these mistakes!)

Key Instructions:
1. Create a new Vite + Vue 3 + TypeScript project in src/automaton-studio-vue/
2. Install latest stable versions from package.json template
3. Configure Vuetify with DEFAULT Material Design 3 theme (no custom themes)
4. Use vite-plugin-vuetify with autoImport: true
5. Load JetBrains Mono font for code editors ONLY (Roboto loaded by Vuetify)
6. Create App.vue with AppShell layout: AppBar + NavigationRail + Router
7. Set up environment files (.env.development, .env.production) with API_BASE_URL
8. Configure TypeScript with strict mode
9. Set up ESLint + Prettier

Deliverables:
- [ ] npm run dev starts successfully
- [ ] App displays with AppBar and NavigationRail
- [ ] Dark mode toggle works
- [ ] No console errors
- [ ] TypeScript compiles without errors

Do NOT:
- Create custom Vuetify themes (use defaults)
- Implement any page content yet (stubs come in Phase 1.2)
- Make API calls (backend not connected yet)
```

#### Phase 1.2 - Page Stubs

**Prompt for AI Agent:**

```
Implement Phase 1.2 - All Page Stubs from REDESIGN_PLAN.md.

PREREQUISITE: Before starting, READ THE ENTIRE src/automaton-studio-vue/REDESIGN_PLAN.md file.
Pay special attention to:
- Section 6: Project Structure (Feature-Based) - follow this exactly!
- Section 7: UI/UX Redesign - contains all page design specs
- Common Pitfalls for AI Agents - avoid these mistakes!
- Page Stub Implementation Guide - follow the stub examples

CRITICAL: Follow the UI/UX design specs in Section 7 EXACTLY for each page layout.

For EACH page stub:
1. Read the corresponding design spec (Section 7.1-7.7)
2. Create the view component in features/{feature-name}/views/
3. Use ONLY Vuetify components (v-card, v-row, v-col, v-btn, etc.)
4. Implement the EXACT layout structure from the design (columns, panels, sections)
5. Use v-skeleton-loader for data areas
6. Use placeholder/mock data (no API calls)
7. Add route to features/{feature-name}/routes.ts
8. Import route in router/index.ts
9. Add navigation item to NavigationRail component
10. Test responsive behavior (mobile/tablet/desktop)
11. Test dark mode

Page Stubs to Create:
- [ ] Auth: LoginView, RegisterView (features/auth/)
- [ ] Dashboard: DashboardView (features/dashboard/)
- [ ] Flows: FlowListView, FlowGenerationView (features/flows/)
- [ ] Apps: AppListView, AppEditorView (features/apps/)
- [ ] Steps: StepListView, StepEditorView (features/steps/)
- [ ] Runners: RunnersView (features/runners/)
- [ ] Projects: ProjectsListView, ProjectSettingsView (features/projects/)
- [ ] Settings: SettingsView (features/settings/)

Deliverables:
- [ ] All 13 page stubs created
- [ ] All routes work (can navigate between all pages)
- [ ] NavigationRail has all menu items
- [ ] Every page matches layout from design spec
- [ ] Responsive works on all pages
- [ ] Dark mode works on all pages
- [ ] No console errors

Do NOT:
- Make API calls
- Implement real data loading
- Add complex state management (use ref() for mock data only)
- Implement form submissions
```

#### Phase 2.1 - Authentication

**Prompt for AI Agent:**

```
Implement Phase 2.1 - Authentication from REDESIGN_PLAN.md.

PREREQUISITE: Before starting, READ THE ENTIRE src/automaton-studio-vue/REDESIGN_PLAN.md file.
Pay special attention to:
- Section 6: Project Structure (Feature-Based) - create features/auth/ structure
- Common Pitfalls for AI Agents - avoid these mistakes!
- Quality Gates (Phase 2.x) - your deliverable checklist

IMPORTANT: 
- Follow Project Structure (Section 6) for features/auth/ folder
- Review existing Blazor authentication implementation for API contract
- Replace Phase 1.2 auth stubs with full implementation

Tasks:
1. Create auth.types.ts (LoginRequest, RegisterRequest, AuthResponse, User types)
2. Create auth.service.ts (login, register, logout, refresh API calls)
3. Create auth.store.ts (Pinia store for user state, token management)
4. Create useAuth.ts composable (auth state helpers, login/logout methods)
5. Update LoginView with real form validation (Vuelidate or VeeValidate)
6. Update RegisterView with email confirmation flow
7. Add auth guards to router (requireAuth middleware)
8. Configure Axios interceptor for JWT token injection
9. Handle token refresh on 401 responses
10. Add logout functionality

API Integration:
- Connect to existing ASP.NET endpoints: POST /api/auth/login, POST /api/auth/register
- Store JWT in localStorage (or httpOnly cookie if available)
- Add Authorization: Bearer {token} header to all authenticated requests

Deliverables:
- [ ] Can register new user
- [ ] Can login with credentials
- [ ] Token stored and used for API calls
- [ ] Protected routes redirect to login
- [ ] Logout clears token and redirects to login
- [ ] Token refresh works on expiration
- [ ] No console errors

Testing:
- Test login with valid/invalid credentials
- Test registration with validation errors
- Test navigation to protected pages (should redirect)
- Test logout and re-login
```

#### Phase 2.2 - Dashboard

**Prompt for AI Agent:**

```
Implement Phase 2.2 - Dashboard from REDESIGN_PLAN.md.

PREREQUISITE: Before starting, READ THE ENTIRE src/automaton-studio-vue/REDESIGN_PLAN.md file.
Pay special attention to:
- Section 6: Project Structure (Feature-Based) - create features/dashboard/ structure
- Section 7.1: Dashboard design spec - follow exact layout
- Common Pitfalls for AI Agents - avoid these mistakes!
- Quality Gates (Phase 2.x) - your deliverable checklist

IMPORTANT:
- Follow Project Structure (Section 6) for features/dashboard/ folder
- Review Dashboard design spec (Section 7.1) for exact layout
- Review Blazor Dashboard implementation for feature parity

Prerequisites:
- Phase 2.1 (Authentication) must be complete

Tasks:
1. Create dashboard.types.ts (DashboardStats, RecentExecution types)
2. Create dashboard.service.ts (API calls for stats, recent executions)
3. Create dashboard.store.ts (Pinia store for dashboard data)
4. Create useDashboardStats.ts composable
5. Create StatsCard.vue component (shows count + icon + label)
6. Create RecentExecutions.vue component (list of recent flows)
7. Create QuickActions.vue component (buttons for common actions)
8. Create ExecutionTrendChart.vue (using Chart.js or similar)
9. Update DashboardView to use real data from API

API Integration:
- GET /api/dashboard/stats (total flows, executions, success rate)
- GET /api/dashboard/recent-executions (last 10 executions)

Deliverables:
- [ ] Dashboard displays real stats from API
- [ ] Recent executions list populated
- [ ] Quick action buttons navigate correctly
- [ ] Execution trend chart displays data
- [ ] Loading states work (skeleton loaders)
- [ ] Error states work (error messages)
- [ ] Responsive layout works
- [ ] Dark mode works

Testing:
- Test with no data (empty state)
- Test with API errors (error state)
- Test loading states
- Test all quick action buttons
```

#### Phase 3.x - Feature Implementation Template

**Generic Prompt for Phase 3 Features:**

```
Implement Phase 3.{X} - {Feature Name} from REDESIGN_PLAN.md.

PREREQUISITE: Before starting, READ THE ENTIRE src/automaton-studio-vue/REDESIGN_PLAN.md file.
Pay special attention to:
- Section 6: Project Structure (Feature-Based) - create features/{feature}/ structure
- Section 7.{X}: {Feature} design spec - follow exact layout
- Common Pitfalls for AI Agents - avoid these mistakes!
- Quality Gates (Phase 3.x) - your deliverable checklist
- File Change Tracking - see which files you should modify
- Merge Conflict Prevention - coordinate with other parallel PRs

IMPORTANT:
- Follow Project Structure (Section 6) for features/{feature}/ folder
- Review {Feature} design spec (Section 7.{X}) for exact layout
- Review Blazor {Feature} implementation for feature parity
- All Phase 3 work can run in parallel (except 3.1 and 3.2 - those are sequential)

Prerequisites:
- Phase 2 (Authentication & Dashboard) must be complete

Standard Tasks for Each Feature:
1. Create {feature}.types.ts (all TypeScript interfaces)
2. Create {feature}.service.ts (API service class)
3. Create {feature}.store.ts (Pinia store)
4. Create use{Feature}.ts composables (as needed)
5. Create feature-specific components (in components/ folder)
6. Update view components (replace stubs with real implementation)
7. Add routes.ts for feature routing
8. Connect to existing ASP.NET API endpoints

For Each View Component:
- Replace placeholder/mock data with API calls
- Implement all CRUD operations (Create, Read, Update, Delete)
- Add form validation
- Add loading states (v-skeleton-loader)
- Add error states (error messages, retry buttons)
- Add empty states (when no data)
- Test responsive layouts
- Test dark mode

API Integration:
- Review Blazor implementation to find existing API endpoints
- Use same endpoint contracts (don't break existing API)
- Add Authorization header (handled by Axios interceptor from Phase 2.1)

Deliverables:
- [ ] All CRUD operations work
- [ ] Data loads from API
- [ ] Forms validate correctly
- [ ] Loading/error/empty states work
- [ ] Navigation between views works
- [ ] Responsive layout works
- [ ] Dark mode works
- [ ] No console errors
- [ ] Feature parity with Blazor implementation verified

Testing Checklist:
- [ ] Test with empty data
- [ ] Test with API errors (network failure)
- [ ] Test all form validations
- [ ] Test all buttons/actions
- [ ] Test responsive on mobile/tablet/desktop
- [ ] Test dark mode toggle
```

### Quality Gates

**Before Moving to Next Phase:**

Each phase must pass these quality gates before proceeding:

#### Phase 1.1 Quality Gate
- [ ] `npm run dev` starts without errors
- [ ] `npm run build` succeeds
- [ ] `npm run type-check` passes
- [ ] App displays with AppBar + NavigationRail
- [ ] Dark mode toggle works
- [ ] No TypeScript errors
- [ ] No console errors/warnings

#### Phase 1.2 Quality Gate
- [ ] All 13 page stubs exist and are navigable
- [ ] All routes work (no 404s)
- [ ] NavigationRail shows all menu items
- [ ] Every page matches layout from design spec (visual review)
- [ ] Responsive works on mobile (375px), tablet (768px), desktop (1440px)
- [ ] Dark mode works on all pages
- [ ] No console errors
- [ ] No TypeScript errors

#### Phase 2.x Quality Gate
- [ ] Feature functionality works end-to-end
- [ ] API integration works (real data loads)
- [ ] All forms validate correctly
- [ ] Loading states display during API calls
- [ ] Error states display on API failures
- [ ] Empty states display when no data
- [ ] Navigation works correctly
- [ ] Responsive works on all breakpoints
- [ ] Dark mode works
- [ ] No console errors
- [ ] No TypeScript errors

#### Phase 3.x Quality Gate
Same as Phase 2.x, plus:
- [ ] Feature parity verified against Blazor implementation
- [ ] All existing features from Blazor are present
- [ ] No regressions (existing functionality still works)

### Common Pitfalls for AI Agents

**Avoid These Mistakes:**

1. **❌ Custom Vuetify Themes**
   - Don't create custom themes - use Vuetify defaults
   - Don't add CSS variables for colors - use Vuetify's theme system

2. **❌ Importing Vuetify Styles Manually**
   - Don't import 'vuetify/styles' in plugins/vuetify.ts
   - vite-plugin-vuetify with autoImport handles this

3. **❌ Custom CSS Before Vuetify**
   - Import custom CSS AFTER Vuetify initialization in main.ts
   - Wrong: Import main.css before createVuetify()
   - Correct: Import main.css after app.use(vuetify)

4. **❌ Not Following Project Structure**
   - Don't put all components in src/components/
   - Use feature-based structure: features/{feature}/components/
   - Keep feature code isolated

5. **❌ Skipping Design Spec Review**
   - Don't guess at layouts - read Section 7 design specs
   - Match the exact structure from ASCII diagrams
   - Use correct Vuetify components (v-card, v-row, v-col, etc.)

6. **❌ Mock Data in Production Code**
   - Remove all mock/placeholder data when implementing features
   - Connect to real API endpoints
   - Don't leave hardcoded arrays in production components

7. **❌ Missing States**
   - Don't forget loading states (use v-skeleton-loader)
   - Don't forget error states (show error messages)
   - Don't forget empty states (show helpful message when no data)

8. **❌ Not Testing Responsive**
   - Don't only test on desktop
   - Test on mobile (375px), tablet (768px), desktop (1440px)
   - Use Vuetify's responsive props (cols, sm, md, lg)

9. **❌ Not Testing Dark Mode**
   - Don't only test in light mode
   - Toggle dark mode and verify all pages
   - Use Vuetify's theme colors (not hardcoded colors)

10. **❌ TypeScript Errors Ignored**
    - Don't proceed with `any` types everywhere
    - Create proper interfaces in {feature}.types.ts
    - Run `npm run type-check` before committing

### File Change Tracking

**Per Phase, Track Which Files Are Modified:**

This helps prevent merge conflicts when running parallel PRs.

**Phase 1.1 Files:**
```
CREATE: src/automaton-studio-vue/ (entire project)
CREATE: package.json, vite.config.ts, tsconfig.json
CREATE: src/main.ts, src/App.vue
CREATE: src/plugins/vuetify.ts
CREATE: src/router/index.ts
CREATE: src/shared/components/layout/AppShell.vue
CREATE: src/shared/components/layout/AppBar.vue
CREATE: src/shared/components/layout/NavigationRail.vue
CREATE: src/assets/styles/main.css
```

**Phase 1.2 Files:**
```
MODIFY: src/router/index.ts (add all routes)
MODIFY: src/shared/components/layout/NavigationRail.vue (add menu items)

CREATE: features/auth/routes.ts
CREATE: features/auth/views/LoginView.vue (stub)
CREATE: features/auth/views/RegisterView.vue (stub)

CREATE: features/dashboard/routes.ts
CREATE: features/dashboard/views/DashboardView.vue (stub)

CREATE: features/flows/routes.ts
CREATE: features/flows/views/FlowListView.vue (stub)
CREATE: features/flows/views/FlowGenerationView.vue (stub)

... (repeat for all 8 features)
```

**Phase 2.1 Files (Auth):**
```
MODIFY: src/router/index.ts (add auth guards)
MODIFY: src/shared/services/api/axios-instance.ts (add interceptors)

CREATE: features/auth/types/auth.types.ts
CREATE: features/auth/services/auth.service.ts
CREATE: features/auth/stores/auth.store.ts
CREATE: features/auth/composables/useAuth.ts

MODIFY: features/auth/views/LoginView.vue (full implementation)
MODIFY: features/auth/views/RegisterView.vue (full implementation)
```

**Phase 3.3 Files (Apps) - Example of Parallel PR:**
```
Only touches features/apps/ folder - safe to run in parallel with other Phase 3 PRs

CREATE: features/apps/types/apps.types.ts
CREATE: features/apps/services/apps.service.ts
CREATE: features/apps/stores/apps.store.ts
CREATE: features/apps/composables/useAppEditor.ts
CREATE: features/apps/composables/useMonacoEditor.ts

MODIFY: features/apps/views/AppListView.vue (full implementation)
MODIFY: features/apps/views/AppEditorView.vue (full implementation)

CREATE: features/apps/components/AppCard.vue
CREATE: features/apps/components/AppEditor.vue
CREATE: features/apps/components/AppPreview.vue
... (other components)
```

### Merge Conflict Prevention

**When Running Parallel PRs:**

1. **Shared Files Coordination:**
   - `src/router/index.ts` - Each feature adds its own routes; merge carefully
   - `src/shared/components/layout/NavigationRail.vue` - Each feature may add menu items; merge carefully
   - `package.json` - Lock dependencies in Phase 1.1; no changes in later phases

2. **Strategy:**
   - Merge Phase 1 first (foundation)
   - Merge Phase 2.1 (auth) before any Phase 3 PRs
   - Merge Phase 3 PRs in any order (they don't conflict)
   - If two Phase 3 PRs both modify router/index.ts:
     - Merge one first
     - Rebase the other and resolve conflicts
     - Both route additions should be preserved

3. **Review Checklist for Merging Parallel PRs:**
   - [ ] No TypeScript errors after merge
   - [ ] npm run dev still works
   - [ ] All routes still work (no 404s)
   - [ ] NavigationRail shows all menu items
   - [ ] No duplicate route paths
   - [ ] No duplicate imports in router

---

## Timeline & Milestones

### High-Level Milestones

```
Phase 1: Project Setup & Foundation
├─ Setup complete
├─ All page stubs navigable
└─ Milestone: Navigable app shell with all page stubs

Phase 2: Authentication & Dashboard
├─ Authentication working
├─ Dashboard functional
└─ Milestone: Users can log in and view dashboard

Phase 3: Feature Parity
├─ Flow generation and management
├─ Apps, Steps, Projects
├─ Runners and Settings
└─ Milestone: All features from Blazor app available

Phase 4: Polish & Launch
├─ Quality assurance complete
├─ Beta testing complete
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
