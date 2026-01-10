# AI Models Integration Plan for Vue.js Portal

**Status:** Architecture Planning  
**Date:** January 2026  
**Purpose:** Port AI functionality from Blazor to Vue.js with enhanced architecture

---

## Executive Summary

This document outlines the architecture for integrating AI models into the Vue.js Automaton Studio portal. The implementation will port proven AI functionality from the Blazor version (StepPage.razor, AppPage.razor) while introducing modern Vue.js patterns and industry best practices.

### Key Components to Port

1. **AI Chat Panel** - Interactive AI assistant for code generation
2. **AI Providers** - OpenAI and Anthropic integration with streaming support
3. **MCP (Model Context Protocol) Clients** - Context-aware AI interactions for Steps and Apps
4. **System Prompts** - Well-tuned prompts for Step and App code generation
5. **Tool Providers** - MCP tools for reading/writing code, testing, and variable management
6. **API Key Management** - Secure storage and retrieval of AI provider keys

---

## ⚠️ Important Architectural Decision: Streaming Implementation

### Blazor Implementation Analysis

The current Blazor implementation uses **client-side AI streaming**:
- AI provider SDKs (OpenAI.NET, Anthropic.SDK) run **in the Blazor WASM app** (client-side)
- API keys are fetched from backend but **used directly in the browser**
- Streaming happens via C# events in the client
- **No server-side streaming endpoint exists**

### Vue.js Options Considered

#### Option 1: Client-Side Streaming (Same as Blazor)
Use official JavaScript SDKs (`openai`, `@anthropic-ai/sdk`) directly in browser.

**Pros:**
- ✅ Same architecture as Blazor (easier port)
- ✅ Lower server load
- ✅ Direct streaming to UI
- ✅ Both SDKs fully support browser + streaming + tool calling

**Cons:**
- ❌ **CRITICAL SECURITY RISK: API keys exposed in browser**
  - Keys visible in network tab, localStorage, memory
  - Cannot be fully secured client-side
  - Violates enterprise security requirements
- ❌ **CORS limitations** (Anthropic may not support all origins)
- ❌ **No centralized rate limiting or caching**
- ❌ **API key rotation complexity**

#### Option 2: Server-Side Streaming (SSE) - ✅ RECOMMENDED

Port AI provider code to backend and stream via Server-Sent Events (SSE).

**Pros:**
- ✅ **API keys stay secure on server** (encrypted, never exposed)
- ✅ **Enterprise-ready security** (SOC2, HIPAA compliant)
- ✅ **Better rate limiting & caching** (centralized control)
- ✅ **No CORS issues** (server-to-server calls)
- ✅ **Consistent with existing API architecture**
- ✅ **Better monitoring, logging, error handling**

**Cons:**
- ❌ Server load (all AI calls through backend)
- ❌ Slightly higher latency (extra hop ~50-100ms)
- ❌ Need to implement SSE endpoint
- ❌ Need to port C# provider code to backend API

### Final Decision: Server-Side Streaming (SSE)

**For production multi-tenant SaaS, API keys MUST NOT be exposed to browsers.**

The architecture will:
1. **Port** AI provider code from Blazor to ASP.NET backend API
2. **Create** new SSE endpoint: `POST /api/ai/stream`
3. **Keep** API keys on server (encrypted in database)
4. **Use** EventSource in Vue.js to consume SSE stream
5. **Build** system prompts client-side in TypeScript (no server call needed)

This ensures security while maintaining streaming UX.

---

## Table of Contents

1. [Current Blazor Architecture](#current-blazor-architecture)
2. [Streaming Architecture Decision](#streaming-architecture-decision)
3. [Vue.js Architecture Design](#vuejs-architecture-design)
4. [Shared AI Services](#shared-ai-services)
5. [Feature-Specific Integration](#feature-specific-integration)
6. [API Endpoints](#api-endpoints)
7. [Implementation Phases](#implementation-phases)
8. [Security Considerations](#security-considerations)
9. [Testing Strategy](#testing-strategy)

---

## Current Blazor Architecture

### Overview

The Blazor implementation has a mature AI integration that:
- Streams AI responses in real-time (Server-Sent Events pattern)
- Supports multiple AI providers (OpenAI, Anthropic)
- Uses MCP (Model Context Protocol) for tool-based interactions
- Maintains conversation history per resource (Step/App)
- Provides context-aware system prompts

### Key Components

```
Blazor AI Architecture
├── Components
│   └── AiChat
│       ├── AiChatPanel.razor         # Main chat UI component
│       └── AiChatPanel.razor.cs      # Chat logic + streaming
│
├── Services
│   ├── AiApiKeyService.cs            # API key management
│   ├── AiChatHistoryService.cs       # Conversation persistence
│   └── Mcp/
│       ├── AiProviderRegistry.cs     # Provider discovery
│       ├── Clients/
│       │   ├── McpClient.cs          # Base MCP client
│       │   ├── StepMcpClient.cs      # Step-specific context
│       │   ├── AppMcpClient.cs       # App-specific context
│       │   └── Providers/
│       │       ├── IAiProvider.cs    # Provider interface
│       │       ├── OpenAiProvider.cs # OpenAI SDK integration
│       │       └── AnthropicProvider.cs # Anthropic SDK integration
│       ├── SystemPrompts/
│       │   ├── StepSystemPromptBuilder.cs  # Step code generation prompts
│       │   └── AppSystemPromptBuilder.cs   # App code generation prompts
│       └── ToolProviders/
│           ├── StepMcpToolProvider.cs      # Step tools (read/save code, test)
│           └── AppMcpToolProvider.cs       # App tools (file tree, backend steps)
│
└── Models
    ├── AiApiKeyModels.cs             # API key DTOs
    └── AiChatHistoryModels.cs        # Conversation DTOs
```

### Data Flow

```
User Input → AiChatPanel → AiApiKeyService (get key) 
           ↓
           McpClient → Provider (OpenAI/Anthropic) → Stream Response
           ↓
           Tool Calls → ToolProvider (execute) → Tool Results
           ↓
           Continue Conversation → Final Response
           ↓
           AiChatHistoryService (save) → Display to User
```

---

## Streaming Architecture Decision

### Current Blazor Implementation (Client-Side)

The Blazor app implements AI streaming **entirely in the client (WASM)**:

**Architecture:**
```
Browser (Blazor WASM)
├── AiChatPanel.razor
├── McpClient (subscribes to ConversationUpdated event)
└── AI Providers (run in browser)
    ├── OpenAiProvider.cs
    │   └── Uses OpenAI.NET SDK directly
    │   └── API key: fetched from backend, used in browser
    │   └── Streaming: C# async enumerables
    └── AnthropicProvider.cs
        └── Uses Anthropic.SDK directly
        └── API key: fetched from backend, used in browser
        └── Streaming: C# async enumerables

Flow:
User Input → Blazor Component → AI Provider (in WASM) → Stream Response → Update UI
                                      ↓
                             OpenAI/Anthropic API (direct from browser)
```

**Key Characteristics:**
- AI SDK code runs **in the browser** (Blazor WASM)
- API keys stored in browser memory during conversation
- Direct HTTP calls from browser to OpenAI/Anthropic APIs
- No backend API endpoint for AI streaming
- Events emitted to UI components via C# events

### Vue.js Implementation Options

#### Option 1: Client-Side (JavaScript SDKs in Browser)

**Implementation:**
```typescript
// Use official SDKs directly in browser
import OpenAI from 'openai'
import Anthropic from '@anthropic-ai/sdk'

// In Vue component
const openai = new OpenAI({
  apiKey: await fetchApiKeyFromBackend(),
  dangerouslyAllowBrowser: true  // ⚠️ Security warning!
})

const stream = await openai.chat.completions.create({
  model: 'gpt-4',
  messages: [...],
  stream: true
})

for await (const chunk of stream) {
  // Update UI with chunk
  responseText.value += chunk.choices[0]?.delta?.content || ''
}
```

**Pros:**
- ✅ Exact same architecture as Blazor
- ✅ No server-side changes needed
- ✅ Lower server load
- ✅ Faster responses (no server hop)
- ✅ Both SDKs fully support:
  - Browser environments
  - Streaming via async iterators
  - Function/tool calling
  - Full TypeScript support

**Cons:**
- ❌ **CRITICAL SECURITY RISK: API keys in browser**
  - Keys visible in DevTools Network tab
  - Keys stored in localStorage/memory
  - Cannot be encrypted client-side
  - User can extract and misuse keys
  - Violates enterprise security (SOC2, HIPAA)
  
- ❌ **CORS requirements**
  - OpenAI: CORS enabled for whitelisted origins
  - Anthropic: May not support custom domains
  - Requires verification with each provider
  
- ❌ **No centralized control**
  - Rate limiting per browser (not per user/tenant)
  - No response caching possible
  - Harder to implement usage quotas
  - No audit logging of AI calls
  
- ❌ **API key management complexity**
  - Need secure key exchange mechanism
  - User-specific keys harder to manage
  - Key rotation requires browser refresh

**Security Analysis:**
```
API Key Exposure Risk:

1. Network Tab:
   Authorization: Bearer sk-proj-xxx... ← Visible in DevTools

2. JavaScript Memory:
   const apiKey = 'sk-proj-xxx...' ← Extractable via debugger

3. localStorage (if cached):
   localStorage.getItem('ai_api_key') → 'sk-proj-xxx...'

4. Source Maps:
   Even minified code can be reverse-engineered

⚠️ UNACCEPTABLE for production multi-tenant SaaS
```

#### Option 2: Server-Side Streaming (SSE) - ✅ RECOMMENDED

**Implementation:**

**Backend (NEW - Port from Blazor):**
```csharp
// src/Automaton.Studio.Server/Controllers/AiController.cs

[ApiController]
[Route("api/ai")]
[Authorize]  // JWT required
public class AiController : ControllerBase
{
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly IAiApiKeyService _apiKeyService;
    
    [HttpPost("stream")]
    public async Task StreamConversation([FromBody] StreamConversationRequest request)
    {
        // Set SSE headers
        Response.ContentType = "text/event-stream";
        Response.Headers.Add("Cache-Control", "no-cache");
        Response.Headers.Add("Connection", "keep-alive");
        
        // Get API key from database (NEVER from client request)
        var apiKey = await _apiKeyService.GetDecryptedKeyAsync(
            User.GetUserId(), 
            request.ProviderId
        );
        
        if (string.IsNullOrEmpty(apiKey))
        {
            await WriteErrorEvent("No API key configured for this provider");
            return;
        }
        
        // Get provider (reuse existing code from Blazor)
        var provider = _providerRegistry.GetProvider(request.ProviderName);
        
        // Build conversation request
        var conversationRequest = new ConversationRequest
        {
            ConversationId = request.ConversationId,
            Model = request.Model,
            SystemPrompt = request.SystemPrompt,  // Built client-side, sent in request
            InitialMessage = request.Message,
            ConversationHistory = request.History,
            ApiKey = apiKey  // From server, secure
        };
        
        // Stream response using existing provider code
        provider.ConversationUpdated += async (sender, e) =>
        {
            var eventData = JsonSerializer.Serialize(e);
            await Response.WriteAsync($"data: {eventData}\n\n");
            await Response.Body.FlushAsync();
        };
        
        try
        {
            await provider.CallAsync(
                conversationRequest,
                await GetToolsForContext(request.ResourceType),
                async (toolId, toolName, args) => await ExecuteToolAsync(toolId, toolName, args),
                HttpContext.RequestAborted
            );
            
            // Send completion event
            await Response.WriteAsync("event: done\ndata: {}\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            await WriteErrorEvent(ex.Message);
        }
    }
    
    private async Task WriteErrorEvent(string message)
    {
        var errorData = JsonSerializer.Serialize(new { error = message });
        await Response.WriteAsync($"event: error\ndata: {errorData}\n\n");
        await Response.Body.FlushAsync();
    }
}
```

**Frontend (Vue.js):**
```typescript
// src/shared/services/ai/ai-client.ts

export class AiClient {
  async startConversation(
    request: ConversationRequest,
    onChunk: (chunk: StreamChunk) => void,
    onComplete: () => void,
    onError: (error: Error) => void,
    signal?: AbortSignal
  ): Promise<void> {
    try {
      const token = localStorage.getItem('auth_token')
      
      // Create SSE connection to backend
      const eventSource = new EventSourcePolyfill('/api/ai/stream', {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,  // JWT, not API key!
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(request)
      })
      
      // Handle streaming chunks
      eventSource.onmessage = (event) => {
        const chunk: StreamChunk = JSON.parse(event.data)
        onChunk(chunk)
      }
      
      // Handle completion
      eventSource.addEventListener('done', () => {
        onComplete()
        eventSource.close()
      })
      
      // Handle errors
      eventSource.addEventListener('error', (event: any) => {
        const error = JSON.parse(event.data)
        onError(new Error(error.message))
        eventSource.close()
      })
      
      eventSource.onerror = () => {
        onError(new Error('Streaming connection failed'))
        eventSource.close()
      }
      
      // Handle abort
      if (signal) {
        signal.addEventListener('abort', () => {
          eventSource.close()
        })
      }
    } catch (error) {
      onError(error as Error)
    }
  }
}
```

**Pros:**
- ✅ **API keys stay secure on server**
  - Encrypted in database
  - Never sent to browser
  - Proper secret management
  
- ✅ **Enterprise-ready security**
  - SOC2 compliant
  - HIPAA compliant
  - Audit logging of all AI calls
  - Centralized access control
  
- ✅ **Better rate limiting & quotas**
  - Per-user/tenant limits
  - Centralized enforcement
  - Response caching possible
  - Cost tracking per user
  
- ✅ **No CORS issues**
  - Backend → AI provider (server-to-server)
  - SSE from own backend (no CORS)
  
- ✅ **Consistent architecture**
  - All API calls use JWT authentication
  - Same error handling patterns
  - Better monitoring and logging
  
- ✅ **Code reuse**
  - Port existing C# provider code to API
  - Same system prompts (copy to TypeScript)
  - Same tool providers

**Cons:**
- ❌ Server load increases (all AI calls through backend)
- ❌ Slightly higher latency (~50-100ms extra hop)
- ❌ Need to implement SSE endpoint
- ❌ Need to port provider code from Blazor Services to API Controllers

**Latency Comparison:**
```
Client-Side:
User → AI Provider API
Latency: 200-500ms (network + AI processing)

Server-Side:
User → Backend → AI Provider API → Backend → User
Latency: 250-600ms (2x network hops + AI processing)

Additional latency: ~50-100ms
Trade-off: Acceptable for enterprise security requirements
```

### System Prompts: Client-Side Building

**Question:** Can prompt builder logic be replicated in UI?

**Answer:** YES, and it SHOULD be (no backend call needed):

**Current (Blazor):**
```csharp
// StepSystemPromptBuilder.cs (in Blazor Services)
public class StepSystemPromptBuilder
{
    public string BuildSystemPrompt(string language)
    {
        return $@"Generate {language} code for automation workflow steps.
        
        ## WORKFLOW
        1. For modifications: Call get_current_code first
        2. Check variables: Call get_step_variables
        ...";
    }
}
```

**New (Vue.js):**
```typescript
// src/shared/services/ai/prompts/step-prompt-builder.ts

export class StepSystemPromptBuilder {
  buildSystemPrompt(language: string): string {
    return `
      Generate ${language} code for automation workflow steps.
      
      ## WORKFLOW
      1. For modifications: Call get_current_code first
      2. Check variables: Call get_step_variables
      ...
    `.trim()
  }
}

// Usage in composable
import { StepSystemPromptBuilder } from '@/shared/services/ai/prompts'

const promptBuilder = new StepSystemPromptBuilder()
const systemPrompt = promptBuilder.buildSystemPrompt(language.value)

// Send to backend in request
await aiClient.startConversation({
  ...request,
  systemPrompt,  // Built client-side, sent as part of request
  message: userMessage
})
```

**Benefits:**
- ✅ No server roundtrip for prompt generation
- ✅ Prompts can be dynamic based on UI state
- ✅ Easier to customize per user/context
- ✅ Faster iteration during development
- ✅ Same prompts as Blazor (copy & paste)

**Implementation:**
1. Copy C# prompt builders to TypeScript
2. Keep exact same prompts (already tested and optimized)
3. Add to `shared/services/ai/prompts/`
4. System prompts sent in API request body (not built on server)

### Final Architecture Decision

**✅ Use Server-Side Streaming (SSE) with Client-Side Prompt Building**

**Rationale:**
1. **Security is non-negotiable** for multi-tenant SaaS
2. **API keys must never be exposed** to browser
3. **Enterprise requirements** (compliance, audit logging)
4. **Consistent with existing architecture** (all APIs use JWT)
5. **Better monitoring, rate limiting, caching**
6. **Acceptable latency trade-off** (~50-100ms for enterprise security)

**Implementation Path:**
1. Port AI provider code from `Automaton.Studio/Services/Mcp/Clients/Providers/` to `Automaton.Studio.Server/Controllers/`
2. Create new `AiController` with SSE endpoint
3. Keep existing provider code (OpenAiProvider.cs, AnthropicProvider.cs) - minimal changes
4. Copy system prompt builders to TypeScript (exact same prompts)
5. Frontend uses EventSource to consume SSE stream
6. API keys stay on server (encrypted in database)

This ensures **production-ready security** while maintaining **streaming UX**.

---

## Vue.js Architecture Design

### Principles

1. **Feature-Based Structure** - AI components colocated with their features
2. **Shared AI Infrastructure** - Common AI services in `src/shared/services/ai/`
3. **Composable Pattern** - Reusable Vue composables (`useAiChat`, `useAiStreaming`)
4. **Provider Abstraction** - Pluggable AI provider architecture
5. **Type Safety** - Full TypeScript support with generated types
6. **Reactive State** - Pinia stores for AI state management
7. **Streaming Support** - Server-Sent Events (SSE) or WebSocket for real-time streaming

### Project Structure

```
src/automaton-studio-vue/src/
├── shared/
│   ├── services/
│   │   └── ai/
│   │       ├── ai-client.ts                    # Core AI client (HTTP + SSE)
│   │       ├── providers/
│   │       │   ├── provider-registry.ts        # Dynamic provider discovery
│   │       │   ├── openai-provider.ts          # OpenAI integration
│   │       │   └── anthropic-provider.ts       # Anthropic integration
│   │       ├── mcp/
│   │       │   ├── mcp-client.ts               # Base MCP client
│   │       │   ├── step-mcp-client.ts          # Step context
│   │       │   └── app-mcp-client.ts           # App context
│   │       ├── api-keys-service.ts             # API key CRUD
│   │       └── chat-history-service.ts         # Conversation persistence
│   │
│   ├── composables/
│   │   ├── useAiChat.ts                        # Main AI chat composable
│   │   ├── useAiStreaming.ts                   # Streaming response handler
│   │   ├── useAiProviders.ts                   # Provider management
│   │   └── useChatHistory.ts                   # History management
│   │
│   ├── components/
│   │   └── ai/
│   │       ├── AiChatPanel.vue                 # Reusable chat UI
│   │       ├── AiMessageList.vue               # Message display
│   │       ├── AiMessageItem.vue               # Single message
│   │       ├── AiProviderSelector.vue          # Provider dropdown
│   │       ├── AiStreamingIndicator.vue        # Loading/streaming state
│   │       └── AiToolCallDisplay.vue           # Show tool execution
│   │
│   ├── stores/
│   │   └── ai.store.ts                         # Global AI state (providers, keys)
│   │
│   └── types/
│       └── ai.types.ts                         # AI-specific types
│
├── features/
│   ├── steps/
│   │   ├── components/
│   │   │   └── StepAiAssistant.vue             # Step AI panel wrapper
│   │   ├── composables/
│   │   │   └── useStepAiContext.ts             # Step-specific AI context
│   │   └── views/
│   │       └── StepEditorView.vue              # Includes StepAiAssistant
│   │
│   ├── apps/
│   │   ├── components/
│   │   │   └── AppAiAssistant.vue              # App AI panel wrapper
│   │   ├── composables/
│   │   │   └── useAppAiContext.ts              # App-specific AI context
│   │   └── views/
│   │       └── AppEditorView.vue               # Includes AppAiAssistant
│   │
│   └── flows/
│       ├── components/
│       │   └── FlowAiAssistant.vue             # NEW: Flow AI panel
│       ├── composables/
│       │   └── useFlowAiContext.ts             # NEW: Flow-specific context
│       └── views/
│           └── FlowGenerationView.vue          # Includes FlowAiAssistant
```

---

## Shared AI Services

### 1. AI Client (`shared/services/ai/ai-client.ts`)

Core HTTP client for AI API communication with streaming support.

```typescript
/**
 * Core AI client for making requests to backend AI endpoints
 * Supports both regular HTTP and Server-Sent Events (SSE) streaming
 */

import axios, { AxiosInstance } from 'axios'
import { EventSourcePolyfill } from 'event-source-polyfill'
import type { 
  ConversationRequest, 
  ConversationResponse, 
  StreamChunk 
} from '@/shared/types/ai.types'

export class AiClient {
  private axios: AxiosInstance
  private baseUrl: string
  
  constructor(baseUrl: string = import.meta.env.VITE_API_BASE_URL) {
    this.baseUrl = baseUrl
    this.axios = axios.create({
      baseURL: baseUrl,
      headers: { 'Content-Type': 'application/json' }
    })
  }
  
  /**
   * Start a new AI conversation with streaming support
   */
  async startConversation(
    request: ConversationRequest,
    onChunk: (chunk: StreamChunk) => void,
    onComplete: (response: ConversationResponse) => void,
    onError: (error: Error) => void,
    signal?: AbortSignal
  ): Promise<void> {
    try {
      // Get auth token from storage
      const token = localStorage.getItem('auth_token')
      
      // Use Server-Sent Events for streaming
      const url = `${this.baseUrl}/api/ai/conversation/stream`
      const eventSource = new EventSourcePolyfill(url, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(request)
      })
      
      eventSource.onmessage = (event) => {
        const chunk: StreamChunk = JSON.parse(event.data)
        onChunk(chunk)
      }
      
      eventSource.addEventListener('done', (event: any) => {
        const response: ConversationResponse = JSON.parse(event.data)
        onComplete(response)
        eventSource.close()
      })
      
      eventSource.onerror = (error) => {
        onError(new Error('Streaming connection failed'))
        eventSource.close()
      }
      
      // Handle abort signal
      if (signal) {
        signal.addEventListener('abort', () => {
          eventSource.close()
        })
      }
    } catch (error) {
      onError(error as Error)
    }
  }
  
  /**
   * Get available AI providers (with API keys configured)
   */
  async getAvailableProviders(includeEnvironment = false): Promise<AiProviderInfo[]> {
    const response = await this.axios.get('/api/aiApiKeys/providers', {
      params: { includeEnvironment }
    })
    return response.data
  }
}

export const aiClient = new AiClient()
```

### 2. MCP Client (`shared/services/ai/mcp/mcp-client.ts`)

Base MCP client for context-aware AI interactions with tool calling.

```typescript
/**
 * Base MCP (Model Context Protocol) client
 * Handles conversation management and tool execution
 */

import { aiClient } from '../ai-client'
import type {
  ConversationRequest,
  ConversationResponse,
  McpTool,
  ToolCall,
  ToolResult,
  StreamChunk
} from '@/shared/types/ai.types'

export abstract class McpClient {
  protected conversationId: string | null = null
  protected tools: McpTool[] = []
  
  /**
   * Subclasses must implement to provide context-specific tools
   */
  protected abstract getTools(): McpTool[]
  
  /**
   * Subclasses must implement to provide system prompt
   */
  protected abstract getSystemPrompt(): string
  
  /**
   * Execute a tool call
   */
  protected abstract executeTool(
    toolName: string, 
    args: Record<string, any>
  ): Promise<ToolResult>
  
  /**
   * Start a new conversation
   */
  async startConversation(
    message: string,
    provider: string,
    model: string,
    context: Record<string, any> = {},
    onChunk: (chunk: StreamChunk) => void,
    onComplete: (response: ConversationResponse) => void,
    onError: (error: Error) => void,
    signal?: AbortSignal
  ): Promise<void> {
    this.conversationId = crypto.randomUUID()
    this.tools = this.getTools()
    
    const request: ConversationRequest = {
      conversationId: this.conversationId,
      provider,
      model,
      messages: [
        { role: 'system', content: this.getSystemPrompt() },
        { role: 'user', content: message }
      ],
      tools: this.tools.map(t => ({
        name: t.name,
        description: t.description,
        inputSchema: t.inputSchema
      })),
      context
    }
    
    await aiClient.startConversation(
      request,
      onChunk,
      onComplete,
      onError,
      signal
    )
  }
  
  /**
   * Continue an existing conversation
   */
  async continueConversation(
    message: string,
    onChunk: (chunk: StreamChunk) => void,
    onComplete: (response: ConversationResponse) => void,
    onError: (error: Error) => void,
    signal?: AbortSignal
  ): Promise<void> {
    if (!this.conversationId) {
      throw new Error('No active conversation')
    }
    
    // Implementation similar to startConversation
    // but appends to existing conversation history
  }
  
  /**
   * End the conversation and cleanup
   */
  endConversation(): void {
    this.conversationId = null
    this.tools = []
  }
}
```

### 3. Provider Registry (`shared/services/ai/providers/provider-registry.ts`)

Dynamic AI provider discovery and management.

```typescript
/**
 * Registry for AI providers
 * Handles dynamic discovery of available providers from backend
 */

import { ref, computed } from 'vue'
import { aiClient } from '../ai-client'
import type { AiProviderInfo } from '@/shared/types/ai.types'

class AiProviderRegistry {
  private providers = ref<AiProviderInfo[]>([])
  private loading = ref(false)
  private error = ref<string | null>(null)
  
  /**
   * Get all available providers (cached)
   */
  get availableProviders() {
    return computed(() => this.providers.value)
  }
  
  /**
   * Load providers from backend
   */
  async loadProviders(includeEnvironment = false): Promise<void> {
    this.loading.value = true
    this.error.value = null
    
    try {
      this.providers.value = await aiClient.getAvailableProviders(includeEnvironment)
    } catch (err) {
      this.error.value = (err as Error).message
      this.providers.value = []
    } finally {
      this.loading.value = false
    }
  }
  
  /**
   * Get provider by name
   */
  getProvider(name: string): AiProviderInfo | undefined {
    return this.providers.value.find(
      p => p.name.toLowerCase() === name.toLowerCase()
    )
  }
  
  /**
   * Check if provider is available
   */
  hasProvider(name: string): boolean {
    return this.getProvider(name) !== undefined
  }
  
  /**
   * Get default provider (first available)
   */
  getDefaultProvider(): AiProviderInfo | undefined {
    return this.providers.value[0]
  }
}

export const providerRegistry = new AiProviderRegistry()
```

---

## Feature-Specific Integration

### Steps AI Assistant

**File:** `features/steps/components/StepAiAssistant.vue`

```vue
<template>
  <div class="step-ai-assistant">
    <AiChatPanel
      :context="aiContext"
      :mcp-client="mcpClient"
      resource-type="CustomStep"
      :resource-id="stepId"
      @code-generated="handleCodeGenerated"
    />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import AiChatPanel from '@/shared/components/ai/AiChatPanel.vue'
import { useStepAiContext } from '../composables/useStepAiContext'

interface Props {
  stepId: string
  currentCode: string
  language: string
}

const props = defineProps<Props>()
const emit = defineEmits<{
  codeGenerated: [code: string]
}>()

// Get Step-specific AI context and MCP client
const { aiContext, mcpClient } = useStepAiContext(
  computed(() => props.stepId),
  computed(() => props.currentCode),
  computed(() => props.language)
)

function handleCodeGenerated(code: string) {
  emit('codeGenerated', code)
}
</script>
```

**Composable:** `features/steps/composables/useStepAiContext.ts`

```typescript
/**
 * Step-specific AI context composable
 * Provides MCP client with Step tools and system prompt
 */

import { computed, type Ref } from 'vue'
import { StepMcpClient } from '@/shared/services/ai/mcp/step-mcp-client'
import type { AiChatContext } from '@/shared/types/ai.types'

export function useStepAiContext(
  stepId: Ref<string>,
  currentCode: Ref<string>,
  language: Ref<string>
) {
  // Create Step MCP client
  const mcpClient = new StepMcpClient(stepId, language)
  
  // Define AI chat context
  const aiContext = computed<AiChatContext>(() => ({
    type: 'step',
    resourceId: stepId.value,
    initialSystemMessage: `I can help you write ${language.value} code for your custom step. I can read your current code, suggest variables, test execution, and save changes.`,
    metadata: {
      currentCode: currentCode.value,
      language: language.value
    }
  }))
  
  return {
    aiContext,
    mcpClient
  }
}
```

### Apps AI Assistant

**File:** `features/apps/components/AppAiAssistant.vue`

Similar structure to StepAiAssistant, but uses `AppMcpClient` with app-specific tools for:
- Reading/writing frontend files (HTML, CSS, JS)
- Managing file tree
- Creating/linking backend steps
- Testing backend step execution

### Flows AI Assistant (NEW)

**File:** `features/flows/components/FlowAiAssistant.vue`

New AI assistant for flow generation and refinement:
- Natural language flow description → generated flow
- Suggest step reuse from existing steps
- Refine existing flows
- Debug flow execution issues

---

## API Endpoints

### Required Backend Endpoints

These endpoints should already exist in the ASP.NET backend. If not, they need to be created.

#### AI Conversation Endpoints

```
POST   /api/ai/conversation/stream
  - Start streaming AI conversation
  - Request: ConversationRequest
  - Response: Server-Sent Events stream
  - Auth: Required (JWT)

GET    /api/aiApiKeys/providers
  - Get available AI providers
  - Query: includeEnvironment=true/false
  - Response: AiProviderInfo[]
  - Auth: Required

GET    /api/aiApiKeys
  - Get user's AI API keys
  - Query: projectId (optional)
  - Response: AiApiKeyModel[]
  - Auth: Required

POST   /api/aiApiKeys
  - Create new AI API key
  - Request: CreateAiApiKeyRequest
  - Response: AiApiKeyModel
  - Auth: Required

DELETE /api/aiApiKeys/{id}
  - Delete AI API key
  - Auth: Required
```

#### Chat History Endpoints

```
GET    /api/ai/chat/sessions
  - Get chat sessions for resource
  - Query: resourceType, resourceId
  - Response: ChatSessionModel[]
  - Auth: Required

POST   /api/ai/chat/sessions
  - Create or update chat session
  - Request: ChatSessionModel
  - Response: ChatSessionModel
  - Auth: Required

DELETE /api/ai/chat/sessions/{id}
  - Delete chat session
  - Auth: Required
```

---

## Implementation Phases

### Phase 1: Shared AI Infrastructure (Week 1-2)

**Goal:** Build foundation for all AI features

- [ ] Create `shared/services/ai/` directory structure
- [ ] Implement `AiClient` with streaming support (SSE)
- [ ] Create TypeScript types in `shared/types/ai.types.ts`
- [ ] Implement `provider-registry.ts`
- [ ] Create base `McpClient` class
- [ ] Build `AiChatPanel.vue` reusable component
- [ ] Implement `useAiChat` composable
- [ ] Implement `useAiStreaming` composable
- [ ] Create Pinia store for AI state
- [ ] Add API key management UI (if not exists in settings)
- [ ] Test streaming with mock responses

**Deliverables:**
- Working AiChatPanel component with mock streaming
- Provider selection working
- API key CRUD working

### Phase 2: Steps AI Integration (Week 3)

**Goal:** Port Step AI assistant from Blazor

- [ ] Create `StepMcpClient` extending `McpClient`
- [ ] Implement Step tools:
  - `get_current_code` - Read current step code
  - `get_step_variables` - Read input/output variables
  - `suggest_variables` - Create new variables
  - `save_code` - Save step code
  - `execute_code` - Test step execution
- [ ] Port Step system prompt from Blazor
- [ ] Create `StepAiAssistant.vue` component
- [ ] Create `useStepAiContext` composable
- [ ] Integrate into `StepEditorView.vue`
- [ ] Add backend API endpoint for streaming (if needed)
- [ ] Test full flow: prompt → code generation → save → test

**Deliverables:**
- Working Step AI assistant in Step editor
- Can generate, save, and test Step code
- Streaming responses display correctly

### Phase 3: Apps AI Integration (Week 4)

**Goal:** Port App AI assistant from Blazor

- [ ] Create `AppMcpClient` extending `McpClient`
- [ ] Implement App tools:
  - `get_current_code` - Read frontend file content
  - `list_files` - Get file tree
  - `save_file` - Save frontend file
  - `create_backend_step` - Create Custom Step for backend
  - `link_backend_step` - Link step to app
  - `get_linked_steps` - Get app's backend steps
  - `execute_backend_step_code` - Test backend step
- [ ] Port App system prompt from Blazor
- [ ] Create `AppAiAssistant.vue` component
- [ ] Create `useAppAiContext` composable
- [ ] Integrate into `AppEditorView.vue`
- [ ] Handle backend step creation events
- [ ] Test full flow: create app → generate code → create backend → link → test

**Deliverables:**
- Working App AI assistant in App editor
- Can generate frontend + backend code
- Backend steps link correctly to app

### Phase 4: Flows AI Integration (NEW) (Week 5)

**Goal:** Add AI assistant for Flow generation

- [ ] Create `FlowMcpClient` extending `McpClient`
- [ ] Implement Flow tools:
  - `search_steps` - Find reusable steps
  - `create_step` - Generate new custom step
  - `create_flow` - Generate flow structure
  - `refine_flow` - Modify existing flow
  - `test_flow` - Dry-run flow execution
- [ ] Design Flow system prompt
- [ ] Create `FlowAiAssistant.vue` component
- [ ] Create `useFlowAiContext` composable
- [ ] Integrate into `FlowGenerationView.vue`
- [ ] Test natural language → flow generation

**Deliverables:**
- Working Flow AI assistant
- Can generate flows from description
- Suggests step reuse

### Phase 5: Chat History & Polish (Week 6)

**Goal:** Conversation persistence and UX polish

- [ ] Implement chat history service
- [ ] Add session management (save, restore, delete)
- [ ] Add session rename/title editing
- [ ] Implement conversation export
- [ ] Add keyboard shortcuts
- [ ] Improve error handling
- [ ] Add loading states and animations
- [ ] Mobile responsive design
- [ ] Dark mode support
- [ ] Accessibility audit

**Deliverables:**
- Chat history persists across sessions
- Polished UX with smooth animations
- Mobile-friendly
- Accessible (WCAG 2.1 AA)

### Phase 6: Testing & Documentation (Week 7)

**Goal:** Comprehensive testing and documentation

- [ ] Unit tests for AI services
- [ ] Component tests for AiChatPanel
- [ ] Integration tests for MCP clients
- [ ] E2E tests for full AI workflows
- [ ] API documentation updates
- [ ] User guide for AI features
- [ ] Developer guide for extending AI
- [ ] Performance testing (streaming latency)
- [ ] Security audit

**Deliverables:**
- 80%+ test coverage for AI code
- Complete documentation
- Performance benchmarks
- Security review complete

---

## Security Considerations

### API Key Storage

1. **Backend Storage**
   - API keys stored encrypted in database
   - User-specific keys scoped to user/project
   - Environment variable fallback for system keys

2. **Frontend Handling**
   - NEVER store raw API keys in browser
   - Only retrieve provider info (name, models)
   - Keys fetched from backend on-demand
   - No API keys in localStorage/sessionStorage

### Authentication

1. **All AI endpoints require JWT authentication**
2. **Rate limiting per user/project**
3. **Usage tracking for billing/quotas**

### Content Security

1. **Sanitize AI-generated code before execution**
2. **Validate tool calls before execution**
3. **Limit tool execution permissions**
4. **Log all AI interactions for audit**

---

## Testing Strategy

### Unit Tests

```typescript
// Example: Test AiClient streaming
describe('AiClient', () => {
  it('should stream conversation chunks', async () => {
    const chunks: StreamChunk[] = []
    const onChunk = (chunk: StreamChunk) => chunks.push(chunk)
    
    await aiClient.startConversation(
      mockRequest,
      onChunk,
      jest.fn(),
      jest.fn()
    )
    
    expect(chunks.length).toBeGreaterThan(0)
    expect(chunks[0].type).toBe('text')
  })
})
```

### Component Tests

```typescript
// Example: Test AiChatPanel
import { mount } from '@vue/test-utils'
import AiChatPanel from '@/shared/components/ai/AiChatPanel.vue'

describe('AiChatPanel', () => {
  it('should send message and display response', async () => {
    const wrapper = mount(AiChatPanel, {
      props: {
        context: mockContext,
        mcpClient: mockMcpClient,
        resourceType: 'CustomStep',
        resourceId: 'test-id'
      }
    })
    
    await wrapper.find('input').setValue('Generate Python code')
    await wrapper.find('button').trigger('click')
    
    // Assert message sent and response displayed
    expect(wrapper.text()).toContain('Generate Python code')
  })
})
```

### E2E Tests

```typescript
// Example: Test full Step AI workflow with Playwright
test('Step AI generates and tests code', async ({ page }) => {
  await page.goto('/stepdesigner/new')
  
  // Open AI panel
  await page.click('[data-testid="ai-tab"]')
  
  // Send prompt
  await page.fill('[data-testid="ai-input"]', 'Generate code to fetch weather')
  await page.click('[data-testid="ai-send"]')
  
  // Wait for streaming to complete
  await page.waitForSelector('[data-testid="ai-complete"]')
  
  // Verify code generated
  const code = await page.textContent('[data-testid="code-editor"]')
  expect(code).toContain('weather')
  
  // Test code
  await page.click('[data-testid="test-code"]')
  await page.waitForSelector('[data-testid="test-success"]')
})
```

---

## TypeScript Types

**File:** `shared/types/ai.types.ts`

```typescript
/**
 * AI-related TypeScript interfaces
 */

// AI Provider
export interface AiProviderInfo {
  id: string
  name: string                    // "OpenAI", "Anthropic"
  models: AiModelInfo[]
  isAvailable: boolean
  source: 'User' | 'Environment'  // User API key or env variable
}

export interface AiModelInfo {
  id: string
  name: string                    // "gpt-4", "claude-3-opus"
  displayName: string
  contextWindow: number
  supportsStreaming: boolean
}

// Conversation
export interface ConversationRequest {
  conversationId: string
  provider: string
  model: string
  messages: ConversationMessage[]
  tools?: McpTool[]
  context?: Record<string, any>
  temperature?: number
  maxTokens?: number
}

export interface ConversationMessage {
  role: 'system' | 'user' | 'assistant' | 'tool'
  content: string
  toolCallId?: string
  toolName?: string
}

export interface ConversationResponse {
  conversationId: string
  message: string
  toolCalls?: ToolCall[]
  isComplete: boolean
  usage?: {
    promptTokens: number
    completionTokens: number
    totalTokens: number
  }
}

// Streaming
export interface StreamChunk {
  type: 'text' | 'tool_call' | 'tool_result' | 'complete'
  content?: string
  toolCall?: ToolCall
  toolResult?: ToolResult
}

// MCP Tools
export interface McpTool {
  name: string
  description: string
  inputSchema: Record<string, any>  // JSON Schema
}

export interface ToolCall {
  id: string
  name: string
  arguments: Record<string, any>
}

export interface ToolResult {
  id: string
  content: string
  isError: boolean
}

// Chat Context
export interface AiChatContext {
  type: 'step' | 'app' | 'flow'
  resourceId: string
  initialSystemMessage: string
  metadata?: Record<string, any>
}

// Chat History
export interface ChatSessionModel {
  id: string
  resourceType: 'CustomStep' | 'WebApp' | 'Flow'
  resourceId: string
  title: string
  isActive: boolean
  createdAt: string
  updatedAt: string
  messages: ChatMessageModel[]
}

export interface ChatMessageModel {
  id: string
  role: 'system' | 'user' | 'assistant'
  content: string
  timestamp: string
  toolCalls?: ToolCall[]
}

// API Keys
export interface AiApiKeyModel {
  id: string
  provider: string
  name: string
  maskedKey: string      // "sk_...xyz"
  projectId?: string
  createdAt: string
}

export interface CreateAiApiKeyRequest {
  provider: string
  name: string
  apiKey: string
  projectId?: string
}
```

---

## Best Practices

### 1. Streaming Performance

- Use Server-Sent Events (SSE) for streaming
- Batch small chunks to reduce re-renders
- Debounce UI updates (16ms for 60fps)
- Use virtual scrolling for long messages

### 2. Error Handling

- Graceful degradation when streaming fails
- Retry logic with exponential backoff
- Clear error messages to user
- Log errors for debugging

### 3. UX Best Practices

- Show typing indicators during streaming
- Allow cancelling in-progress requests
- Persist conversation history
- Keyboard shortcuts (Cmd+Enter to send)
- Copy code blocks with syntax highlighting
- Diff view for code changes

### 4. Code Quality

- TypeScript strict mode
- ESLint + Prettier formatting
- Comprehensive JSDoc comments
- Meaningful variable names
- Small, focused functions

---

## Migration Checklist

### From Blazor to Vue.js

- [ ] Port AiChatPanel component (Blazor → Vue)
- [ ] Port MCP client architecture
- [ ] Port AI provider implementations
- [ ] Port system prompts (no changes, copy as-is)
- [ ] Port tool providers
- [ ] Port API key service
- [ ] Port chat history service
- [ ] Test feature parity with Blazor version
- [ ] Verify streaming performance
- [ ] Verify mobile responsiveness
- [ ] Dark mode support

---

## Future Enhancements

1. **Multi-modal Support**
   - Image generation with DALL-E
   - Vision API for screenshot analysis
   - Voice input/output

2. **Advanced Features**
   - Multi-agent collaboration
   - Code version comparison
   - AI code review
   - Automated testing generation

3. **Performance**
   - Response caching
   - Preload provider info
   - WebSocket upgrade from SSE

4. **Analytics**
   - Track AI usage metrics
   - Monitor success rates
   - Cost tracking per user/project

---

## References

- [REDESIGN_PLAN.md](./REDESIGN_PLAN.md) - Overall Vue.js migration plan
- [Server-Sent Events](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events)
- [OpenAI API](https://platform.openai.com/docs/api-reference)
- [Anthropic API](https://docs.anthropic.com/claude/reference)
- [Model Context Protocol](https://modelcontextprotocol.io/)

---

**Document Version:** 1.0  
**Last Updated:** January 10, 2026  
**Status:** ✅ Ready for Implementation
