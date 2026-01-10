/**
 * AI Chat type definitions
 * Enhanced types supporting streaming, multiple message types, and MCP tool execution
 */

// Message roles
export type AiChatRole = 'user' | 'assistant' | 'system'

// Message types for different rendering
export type AiMessageType =
  | 'text' // Plain text/markdown
  | 'reasoning' // Collapsible AI reasoning/thinking
  | 'tool_execution' // Tool call status and results
  | 'code' // Generated code with apply button
  | 'error' // Error messages
  | 'generating' // "AI is generating..." indicator

// Tool execution status
export type ToolExecutionStatus = 'executing' | 'completed' | 'error'

// Tool execution result
export interface ToolExecutionResult {
  toolName: string
  toolId: string
  isSuccess: boolean
  data?: unknown
  error?: string
}

// Code block in a message
export interface CodeBlock {
  id: string
  language: string
  code: string
  fileName?: string
  canApply?: boolean
}

// Base chat message
export interface AiChatMessage {
  id: string
  role: AiChatRole
  type: AiMessageType
  content: string
  timestamp: Date

  // For tool execution messages
  toolName?: string
  toolStatus?: ToolExecutionStatus
  toolResult?: ToolExecutionResult

  // For code messages
  codeBlocks?: CodeBlock[]

  // For streaming - indicates if message is still being streamed
  isStreaming?: boolean

  // For reasoning - collapsed by default
  isCollapsed?: boolean
}

// Streaming chunk types from SSE
export type StreamChunkType =
  | 'text'
  | 'reasoning'
  | 'tool_call'
  | 'tool_result'
  | 'code_generated'
  | 'complete'
  | 'error'

// Streaming chunk from SSE
export interface StreamChunk {
  type: StreamChunkType
  content?: string
  // For tool calls
  toolCall?: {
    id: string
    name: string
    description: string
    arguments: Record<string, unknown>
  }
  // For tool results
  toolResult?: {
    toolId: string
    toolName: string
    isSuccess: boolean
    data?: unknown
    error?: string
  }
  // For code generation
  code?: string
  language?: string
  codeId?: string
  fileName?: string
}

// Chat configuration
export interface AiChatConfig {
  /** Title/header text for the chat */
  title: string

  /** Placeholder text for input */
  inputPlaceholder: string

  /** Empty state message */
  emptyStateMessage: string

  /** Quick suggestion buttons (optional) */
  suggestions?: string[]

  /** Context to pass to API calls */
  context?: Record<string, unknown>

  /** Resource type for AI context (app, step, flow) */
  resourceType?: 'app' | 'step' | 'flow'

  /** Resource ID */
  resourceId?: string

  /** Programming language (for code generation context) */
  language?: string
}

// Streaming callbacks
export interface StreamingCallbacks {
  onChunk: (chunk: StreamChunk) => void
  onComplete: () => void
  onError: (error: Error) => void
}

// Chat handlers interface
export interface AiChatHandlers {
  /**
   * Send message to AI with streaming support
   * Returns an abort controller to cancel the request
   */
  onSendMessage: (
    message: string,
    context?: Record<string, unknown>,
    streaming?: StreamingCallbacks,
  ) => Promise<AbortController | void>

  /**
   * Optional: Handle suggestion click
   */
  onSuggestion?: (suggestion: string) => void

  /**
   * Optional: Apply generated code to editor
   */
  onApplyCode?: (code: string, fileName?: string) => void
}

// AI Provider info
export interface AiProviderInfo {
  id: string
  name: string // "OpenAI", "Anthropic"
  models: AiModelInfo[]
  isAvailable: boolean
  source: 'User' | 'Environment'
}

export interface AiModelInfo {
  id: string
  name: string
  displayName: string
  contextWindow: number
  supportsStreaming: boolean
}

// Conversation request for SSE endpoint
export interface ConversationRequest {
  conversationId: string
  resourceType: string
  resourceId: string
  language?: string
  message: string
  history: Array<{ role: AiChatRole; content: string }>
}
