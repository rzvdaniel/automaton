# AI Chat Components - Generic Session Management

## Overview

Automaton provides two components for AI chat with session management:

1. **`AiAssistant`** ⭐ **Primary Component** - Full chat interface with built-in session management
2. **`ChatSessionHistory`** - Standalone sessions list (specialized use cases only)

**Recommendation**: Use `AiAssistant` for all editors (Apps, Steps, Flows). It's the complete solution with sessions built-in.

---

## AiAssistant Component ⭐

**Location**: `src/automaton-studio-vue/src/features/apps/components/AiAssistant.vue`

### What It Does

A fully generic AI chat interface with **built-in session management**. Works with any resource type (Apps, Steps, Flows, etc.).

**Key Features**:
- ✅ Built-in session management (create, rename, delete, activate)
- ✅ Dual-view mode (sessions list + chat interface)
- ✅ Real-time AI streaming (SSE)
- ✅ Tool execution with callbacks
- ✅ Provider/model selection
- ✅ Smart session auto-naming
- ✅ Message persistence to database
- ✅ Fully generic via props

### Component Structure

```
AiAssistant
├── Sessions View (when no active session or back button clicked)
│   ├── Empty state ("No chat sessions yet")
│   ├── Session list (search, sorted by active/date)
│   ├── Create new session
│   └── Rename/Delete actions
│
└── Chat View (when session is active)
    ├── Message history
    ├── Provider/Model selector
    ├── Message input
    ├── Tool execution indicators
    └── Back to sessions button
```

### Props

```typescript
interface Props {
  resourceType: string          // 'WebApp', 'CustomStep', 'Flow', 'General'
  resourceId: string            // ID of the resource
  runnerId?: string | null      // Optional runner for tool execution
  initialPrompt?: string        // Auto-send this prompt on mount
  systemPrompt?: string         // System context for AI
  suggestions?: string[]        // Quick suggestion buttons
}
```

### Events

```typescript
interface Emits {
  codeGenerated: [code: string]
  toolExecuted: [toolName: string, args: any, result?: any]
}
```

### Usage Examples

#### App Editor (Current Implementation)

```vue
<template>
  <AiAssistant
    resource-type="WebApp"
    :resource-id="appId"
    :runner-id="selectedRunnerId"
    :system-prompt="systemPromptForApps"
    :suggestions="appSuggestions"
    @tool-executed="handleAiToolExecuted"
  />
</template>

<script setup lang="ts">
import AiAssistant from '../components/AiAssistant.vue'

const appId = ref('app-123')
const selectedRunnerId = ref('runner-1')

const systemPromptForApps = `You are an AI assistant helping to build a web application. 
You can create and modify HTML, CSS, and JavaScript code.
When a runner is attached, you can also execute backend steps for data processing.`

const appSuggestions = [
  'Add a login form',
  'Create a navigation bar',
  'Add a contact form',
  'Create a data table',
]

function handleAiToolExecuted(toolName: string, args: any, result?: any) {
  console.log('AI tool executed:', { toolName, args, result })
  
  if (toolName === 'write_file' && args.path) {
    editorStore.refreshFile(args.path)
  }
}
</script>
```

#### Step Editor

```vue
<template>
  <AiAssistant
    resource-type="CustomStep"
    :resource-id="stepId"
    :system-prompt="systemPromptForSteps"
    :suggestions="stepSuggestions"
    @code-generated="handleCodeGenerated"
  />
</template>

<script setup lang="ts">
import AiAssistant from '@/features/apps/components/AiAssistant.vue'

const stepId = ref('step-456')

const systemPromptForSteps = `You are an AI assistant helping to build a custom automation step.
You can write Python, PowerShell, or Batch scripts.
Focus on creating reusable, well-documented code with proper error handling.`

const stepSuggestions = [
  'Create a file processing step',
  'Add error handling',
  'Write unit tests',
  'Add input validation',
]

function handleCodeGenerated(code: string) {
  stepEditor.value?.setValue(code)
}
</script>
```

#### Flow Editor

```vue
<template>
  <AiAssistant
    resource-type="Flow"
    :resource-id="flowId"
    :runner-id="selectedRunner"
    :system-prompt="systemPromptForFlows"
    :suggestions="flowSuggestions"
  />
</template>

<script setup lang="ts">
import AiAssistant from '@/features/apps/components/AiAssistant.vue'

const flowId = ref('flow-789')

const systemPromptForFlows = `You are an AI assistant helping to design and optimize automation flows.
You can suggest step sequences, help with variable mapping, and debug flow execution.
Focus on efficiency, error handling, and maintainability.`

const flowSuggestions = [
  'Optimize this flow',
  'Add error handling steps',
  'Create a retry mechanism',
  'Add logging steps',
]
</script>
```

### How Sessions Work

#### Database Schema

```sql
AiChatSession
├── Id (Guid)
├── ResourceType (enum: General, CustomStep, WebApp, Flow)
├── ResourceId (Guid) ← Links to App/Step/Flow
├── Title (string)
├── Provider (string) ← e.g., "OpenAI"
├── Model (string) ← e.g., "gpt-4"
├── IsActive (bool) ← Only one active session per resource
├── CreatedAt (DateTime)
└── UpdatedAt (DateTime)

AiChatMessage
├── Id (Guid)
├── SessionId (Guid) ← Links to AiChatSession
├── Role (enum: user, assistant, system, tool)
├── Content (string)
├── MessageType (string)
├── Metadata (JSON)
└── CreatedAt (DateTime)
```

#### API Endpoints

```http
# Get all sessions for a resource
GET /api/AiChatHistory/sessions?resourceType=WebApp&resourceId={id}

# Get or create active session
GET /api/AiChatHistory/sessions/active?resourceType=WebApp&resourceId={id}

# Create new session
POST /api/AiChatHistory/sessions
{ "resourceType": "WebApp", "resourceId": "...", "title": "..." }

# Get session messages
GET /api/AiChatHistory/sessions/{sessionId}/messages

# Activate a session
PUT /api/AiChatHistory/sessions/{sessionId}/activate

# Update session title
PUT /api/AiChatHistory/sessions/{sessionId}/title
{ "title": "New Title" }

# Delete session
DELETE /api/AiChatHistory/sessions/{sessionId}

# Save messages (batch)
POST /api/AiChatHistory/messages/batch
```

#### Session Lifecycle

1. **On Mount**:
   - Load AI providers
   - Load existing sessions for the resource
   - If sessions exist → show active session in Chat View
   - If no sessions → show Sessions View (empty state)

2. **Creating a Session**:
   - User clicks "New Session" → enters title
   - Session created via API
   - Switches to Chat View with new session

3. **Sending Messages**:
   - User types message → sends to AI
   - Response streams in real-time (SSE)
   - All messages auto-saved to session
   - If first message → AI generates smart title

4. **Switching Sessions**:
   - User clicks session in list
   - Loads messages for that session
   - Switches to Chat View

### UI Behavior

#### Empty State
```
┌─────────────────────────────────────┐
│  AI Assistant                    [+]│
├─────────────────────────────────────┤
│                                     │
│           💬                        │
│    No chat sessions yet             │
│                                     │
│    [Start New Chat]                 │
│                                     │
└─────────────────────────────────────┘
```

#### Sessions List
```
┌─────────────────────────────────────┐
│  AI Assistant                    [+]│
├─────────────────────────────────────┤
│  [Search sessions...]               │
│                                     │
│  ┌─────────────────────────────┐   │
│  │ Login Form Implementation   │   │
│  │ [Active] 🟢                 │   │
│  │ Jan 8, 2026 • OpenAI       │   │
│  │                    [✏️] [🗑️]│   │
│  └─────────────────────────────┘   │
└─────────────────────────────────────┘
```

#### Chat View
```
┌─────────────────────────────────────┐
│ [←] Login Form Implementation       │
│     [Provider ▼] [Model ▼]         │
├─────────────────────────────────────┤
│                                     │
│  User: Add a login form             │
│                                     │
│  AI: I'll create a login form...    │
│  [Code block]                       │
│                                     │
│  🔧 Tool: write_file                │
│     ✓ Completed                     │
│                                     │
├─────────────────────────────────────┤
│ [Type a message...]         [Send]  │
└─────────────────────────────────────┘
```

### Composable: useAiChat

The component uses the `useAiChat` composable for all session logic.

**Location**: `src/automaton-studio-vue/src/shared/composables/useAiChat.ts`

```typescript
const {
  // Messages
  messages,                    // ChatMessage[]
  hasMessages,                 // boolean
  
  // Generation
  isGenerating,                // boolean
  sendMessage,                 // (message: string) => Promise<void>
  clearMessages,               // () => Promise<void>
  
  // Providers
  selectedProvider,            // string
  selectedModel,               // string
  availableProviders,          // AiProvider[]
  availableModels,             // string[]
  loadProviders,               // () => Promise<void>
  
  // Sessions
  sessions,                    // ChatSession[]
  activeSession,               // ChatSession | null
  isLoadingSessions,           // boolean
  loadSessions,                // () => Promise<void>
  createSession,               // (title: string) => Promise<void>
  activateSession,             // (sessionId: string) => Promise<void>
  updateSessionTitle,          // (sessionId: string, title: string) => Promise<void>
  deleteSession,               // (sessionId: string) => Promise<void>
  
  // Errors
  error,                       // string | null
} = useAiChat(resourceType, resourceId, options)
```

---

## ChatSessionHistory Component

**Location**: `src/automaton-studio-vue/src/shared/components/ChatSessionHistory.vue`

### What It Does

A **standalone sessions list** component without chat interface. Use this only for specialized scenarios where you need just the sessions list.

**When to Use**:
- ❌ NOT for normal editor integration (use `AiAssistant` instead)
- ✅ Custom session browser/picker UI
- ✅ Read-only session history view
- ✅ Specialized dashboard widgets

### Props

```typescript
interface Props {
  resourceType: ChatResourceType  // 'WebApp', 'CustomStep', 'Flow', 'General'
  resourceId: string              // ID of the resource
  title?: string                  // Custom panel title
}
```

### Events

```typescript
interface Emits {
  sessionSelected: [session: ChatSession]
  sessionActivated: [session: ChatSession]
}
```

### Usage Example

```vue
<template>
  <ChatSessionHistory
    resource-type="WebApp"
    :resource-id="appId"
    title="AI Chat Sessions"
    @session-selected="handleSessionSelected"
  />
</template>

<script setup lang="ts">
import ChatSessionHistory from '@/shared/components/ChatSessionHistory.vue'
import type { ChatSession } from '@/shared/types/ai.types'

const appId = ref('app-123')

function handleSessionSelected(session: ChatSession) {
  // Navigate to full chat interface
  router.push(`/chat/${session.id}`)
}
</script>
```

---

## Backend Integration

Both components use the same backend infrastructure:

### Resource Types

**Enum**: `src/Automaton.Studio.Server/Enums/ChatResourceType.cs`

```csharp
public enum ChatResourceType
{
    General = 0,      // General chat (not linked to resource)
    CustomStep = 1,   // Linked to a custom step
    WebApp = 2,       // Linked to a web app
    Flow = 3          // Linked to a flow
}
```

### TypeScript Types

**File**: `src/automaton-studio-vue/src/shared/types/ai.types.ts`

```typescript
export const ChatResourceType = {
  General: 'General',
  CustomStep: 'CustomStep',
  WebApp: 'WebApp',
  Flow: 'Flow',
} as const

export type ChatResourceType = typeof ChatResourceType[keyof typeof ChatResourceType]

export interface ChatSession {
  id: string
  resourceType: ChatResourceType
  resourceId: string
  title: string
  isActive: boolean
  provider?: string
  model?: string
  createdAt: string
  updatedAt: string
}
```

### Service Layer

**File**: `src/automaton-studio-vue/src/shared/services/api/ai-chat-history.service.ts`

```typescript
import { aiChatHistoryService } from '@/shared/services/api/ai-chat-history.service'

// Get sessions
const sessions = await aiChatHistoryService.getSessions('WebApp', appId)

// Create session
const session = await aiChatHistoryService.createSession({
  resourceType: 'WebApp',
  resourceId: appId,
  title: 'My Chat Session',
})

// Activate session
await aiChatHistoryService.activateSession(sessionId)

// Update title
await aiChatHistoryService.updateSessionTitle(sessionId, 'New Title')

// Delete session
await aiChatHistoryService.deleteSession(sessionId)
```

---

## Quick Start Guide

### Adding AI Chat to Any Editor

1. **Import the component**:
   ```typescript
   import AiAssistant from '@/features/apps/components/AiAssistant.vue'
   ```

2. **Add to template**:
   ```vue
   <AiAssistant
     resource-type="CustomStep"  <!-- or WebApp, Flow -->
     :resource-id="resourceId"
     :system-prompt="systemPrompt"
     :suggestions="suggestions"
     @tool-executed="handleToolExecuted"
   />
   ```

3. **Configure props**:
   ```typescript
   const systemPrompt = 'You are helping to build...'
   const suggestions = ['Suggestion 1', 'Suggestion 2']
   ```

4. **Handle events** (optional):
   ```typescript
   function handleToolExecuted(toolName: string, args: any) {
     // Refresh UI when AI modifies code
   }
   ```

That's it! The component handles everything else automatically.

---

## Comparison Table

| Feature | AiAssistant | ChatSessionHistory |
|---------|-------------|-------------------|
| **Purpose** | Full chat + sessions | Sessions list only |
| **Use Case** | Primary AI interaction | Specialized history browser |
| **UI** | Dual-view (list + chat) | Single list view |
| **Message Input** | ✅ Yes | ❌ No |
| **Session CRUD** | ✅ Yes | ✅ Yes |
| **AI Streaming** | ✅ Yes | ❌ No |
| **Tool Execution** | ✅ Yes | ❌ No |
| **When to Use** | Always (primary) | Rarely (specialized) |

---

## Testing

1. **Navigate to App Editor** → AI Assistant panel
2. **Empty state** → "No chat sessions yet"
3. **Create session** → Enter title → Session created
4. **Send message** → AI responds → Title auto-updated
5. **Back button (←)** → Return to sessions list
6. **Create more sessions** → All appear in list
7. **Active badge** → Green badge on active session
8. **Rename/Delete** → Pencil/trash icons work
9. **Click session** → Opens that conversation

---

## Summary

✅ **Use `AiAssistant`** for Apps, Steps, Flows - it's the complete solution
✅ **Sessions are built-in** - no need for separate components
✅ **Fully generic** - works with any resource type via props
✅ **Production-ready** - currently used in App Editor
✅ **Well-documented** - examples for all resource types
✅ **Zero setup** - backend already supports all resource types

**The infrastructure was already generic - we just added `Flow` to the enum and documented how to use it!** 🎉

