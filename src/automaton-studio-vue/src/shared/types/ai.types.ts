/**
 * AI integration type definitions
 * Matches backend DTOs and MCP models
 */

// ============================================
// AI Providers and Models
// ============================================

export interface AiProvider {
  providerName: string
  displayName: string
  description?: string
  iconName?: string
  apiKeyHelpUrl?: string
  hasStoredKeys: boolean
  storedKeyCount: number
  hasEnvironmentKey: boolean
  supportedModels: string[]
  defaultModel?: string // The default model marked in admin (IsDefault = true)
  storedKeys?: AiApiKey[]
}

export interface AiApiKey {
  id: string
  name: string
  providerName: string
  description?: string
  createdAt: string
  lastUsedAt?: string
  isActive: boolean
}

export interface AiModel {
  id: string
  name: string
  displayName?: string
  provider: string
  contextWindow?: number
  supportsStreaming?: boolean
}

// ============================================
// Conversation Request/Response
// ============================================

export interface ConversationMessage {
  role: 'system' | 'user' | 'assistant' | 'tool'
  content: string
  toolCallId?: string
  toolName?: string
  // Token usage (from AI provider)
  inputTokens?: number
  outputTokens?: number
  model?: string
}

export interface StreamConversationRequest {
  conversationId?: string
  provider: string
  model?: string
  message: string
  systemPrompt?: string
  history?: ConversationMessage[]
  context?: Record<string, any>
  sessionId?: string
  resourceType?: string
  resourceId?: string
  language?: string
}

// ============================================
// SSE Event Types
// ============================================

export type SseEventType = 'update' | 'done' | 'error' | 'cancelled'

export interface SseConversationUpdate {
  conversationId: string
  messages: ConversationMessage[]
  toolCalls: ToolCall[]
  toolResults: ToolResult[]
  timestamp: string
}

export interface SseDoneEvent {
  conversationId: string
  sessionId?: string
  success: boolean
}

export interface SseErrorEvent {
  error: string
}

// ============================================
// Tool Calling
// ============================================

export interface ToolCall {
  id: string
  name: string // Backend normalized format (works for all AI providers)
  arguments: Record<string, any> // Backend normalized format
}

export interface ToolResult {
  toolCallId: string
  content: string
  isError?: boolean
}

// ============================================
// Chat UI Types
// ============================================

export type ChatMessageRole = 'user' | 'assistant' | 'system'
export type ChatMessageType =
  | 'text'
  | 'reasoning'
  | 'toolExecution'
  | 'codeSuccess'
  | 'codeError'
  | 'generating'
  | 'system'
  | 'planApproval'
  | 'instructionUpdate'

// Payload embedded in a planApproval message
export interface PlanData {
  planType: 'design' | 'implementation' | 'stage_complete'
  title: string
  summary: string
  planFile?: string
  components?: Array<{ name: string; description?: string; responsibility?: string }>
  techDecisions?: Array<{ decision: string; rationale?: string }>
  stages?: Array<{ name: string; description?: string; complexity?: string; estimatedComplexity?: string }>
  stageIndex?: number
  stageSummary?: string
  awaitingApproval: boolean
}

export interface ChatMessage {
  id: string
  role: ChatMessageRole
  content: string
  type?: ChatMessageType
  timestamp: Date
  
  // For tool execution messages
  toolName?: string
  toolArgs?: any
  toolStatus?: 'executing' | 'completed' | 'error'
  toolResult?: any
  
  // For plan approval cards
  planData?: PlanData
  
  // For instruction update notifications
  instructionNotification?: string
  
  // For code generation messages
  generatedCode?: string
  language?: string
  codeId?: string
  
  // For error messages
  error?: string
  
  // For streaming messages
  isStreaming?: boolean
  
  // Token usage
  inputTokens?: number
  outputTokens?: number
  totalTokens?: number
  model?: string
}

// ============================================
// Chat History (from backend)
// ============================================

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
  complexityTier?: string
  
  // Usage metrics
  totalPromptsSubmitted: number
  totalInputTokens: number
  totalOutputTokens: number
  totalTokens: number
}

export interface ChatSessionWithMessages extends ChatSession {
  messages: ChatMessage[]
}

export interface CreateChatSessionRequest {
  resourceType: ChatResourceType
  resourceId: string
  title: string
  provider?: string
  model?: string
}

export interface SaveChatMessageRequest {
  sessionId: string
  role: ChatMessageRole
  content: string
}

// ============================================
// AI Chat Configuration
// ============================================

export interface AiChatConfig {
  /** Resource type for context */
  resourceType: ChatResourceType
  
  /** Resource ID */
  resourceId: string
  
  /** Additional context to pass to AI */
  context?: Record<string, any>
  
  /** Default provider */
  defaultProvider?: string
  
  /** Default model */
  defaultModel?: string
  
  /** Enable model selector */
  showModelSelector?: boolean
  
  /** Enable chat history */
  enableHistory?: boolean
}

// ============================================
// Chat Statistics
// ============================================

/**
 * Statistics for a single AI chat session
 */
export interface ChatSessionStats {
  sessionId: string
  title: string
  promptsSubmitted: number
  totalTokens: number
  inputTokens: number
  outputTokens: number
}

/**
 * Aggregate statistics for an app's AI chat sessions
 */
export interface AppChatStats {
  totalPromptsSubmitted: number
  totalTokens: number
  inputTokens: number
  outputTokens: number
  sessionCount: number
}

// ============================================
// App Runtime Execution Logs
// ============================================

export interface AppExecutionLog {
  id: string
  appId: string
  controllerName: string
  actionName: string
  requestPayload?: string
  responsePayload?: string
  success: boolean
  errorMessage?: string
  durationMs: number
  userId?: string
  ipAddress?: string
  createdAt: string
}

export interface AppExecutionLogPage {
  items: AppExecutionLog[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface AppExecutionLogFilter {
  success?: boolean
  controllerName?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}
