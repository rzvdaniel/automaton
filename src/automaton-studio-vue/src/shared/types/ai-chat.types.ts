/**
 * AI Chat type definitions for generic reusable component
 */

export type AiChatRole = 'user' | 'assistant'

export interface AiChatMessage {
  id: string
  role: AiChatRole
  content: string
  timestamp: Date
}

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
  context?: Record<string, any>
}

export interface AiChatHandlers {
  /**
   * Send message to AI
   * Should return the AI response content
   */
  onSendMessage: (message: string, context?: Record<string, any>) => Promise<string>
  
  /**
   * Optional: Handle suggestion click
   */
  onSuggestion?: (suggestion: string) => void
}
