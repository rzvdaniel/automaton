/**
 * AI Conversation Client
 * Handles SSE streaming for AI conversations
 */
import type {
  StreamConversationRequest,
  SseConversationUpdate,
  SseDoneEvent,
  SseErrorEvent,
} from '@/shared/types/ai.types'
import { tokenStorage } from '../api/axios-instance'

export type SseEventHandler = {
  onUpdate?: (data: SseConversationUpdate) => void
  onDone?: (data: SseDoneEvent) => void
  onError?: (data: SseErrorEvent) => void
  onCancelled?: () => void
}

/**
 * AI Conversation Client for SSE streaming
 */
export class AiConversationClient {
  private baseUrl: string
  private eventSource: EventSource | null = null
  private abortController: AbortController | null = null
  private reader: ReadableStreamDefaultReader<Uint8Array> | null = null

  constructor(baseUrl: string = import.meta.env.VITE_API_BASE_URL || '') {
    this.baseUrl = baseUrl
  }

  /**
   * Start a streaming conversation with AI using native fetch + ReadableStream
   * Replaces event-source-polyfill which had compatibility issues
   */
  async startConversation(
    request: StreamConversationRequest,
    handlers: SseEventHandler,
    abortSignal?: AbortSignal,
  ): Promise<void> {
    // Close any existing connection
    this.close()

    try {
      if (!this.baseUrl) {
        throw new Error('API base URL is not configured (VITE_API_BASE_URL).')
      }

      // Get auth token (use shared token storage to match axios config)
      const token = tokenStorage.getAccessToken() || localStorage.getItem('auth_token')
      if (!token) {
        throw new Error('No authentication token found')
      }

      const url = `${this.baseUrl}/api/ai/conversation/stream`

      // Create abort controller for cancellation
      this.abortController = new AbortController()

      // Link external abort signal if provided
      if (abortSignal) {
        abortSignal.addEventListener('abort', () => {
          this.abortController?.abort()
        })
      }

      // Use native fetch with ReadableStream
      const response = await fetch(url, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
          'Content-Type': 'application/json',
          Accept: 'text/event-stream',
        },
        body: JSON.stringify(request),
        credentials: 'include',
        signal: this.abortController.signal,
      })

      if (!response.ok) {
        const errorText = await response.text()
        throw new Error(`HTTP ${response.status}: ${errorText}`)
      }

      if (!response.body) {
        throw new Error('Response body is null')
      }

      // Process SSE stream
      this.reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      let shouldStop = false

      while (!shouldStop) {
        const { done, value } = await this.reader.read()

        if (done) {
          break
        }

        // Decode chunk and add to buffer
        buffer += decoder.decode(value, { stream: true })

        // Process complete SSE messages (separated by \n\n)
        const messages = buffer.split('\n\n')
        buffer = messages.pop() || '' // Keep incomplete message in buffer

        for (const message of messages) {
          if (!message.trim()) continue

          // Parse SSE format: "event: <type>\ndata: <json>"
          const lines = message.split('\n')
          let eventType = 'message'
          let eventData = ''

          for (const line of lines) {
            if (line.startsWith('event:')) {
              eventType = line.substring(6).trim()
            } else if (line.startsWith('data:')) {
              eventData = line.substring(5).trim()
            }
          }

          // Handle different event types
          try {
            if (eventType === 'update') {
              const data = this.parseJson<SseConversationUpdate>(eventData)
              if (data) {
                handlers.onUpdate?.(data)
              }
            } else if (eventType === 'done') {
              const data = this.parseJson<SseDoneEvent>(eventData)
              if (data) {
                handlers.onDone?.(data)
              }
              shouldStop = true
              break
            } else if (eventType === 'error') {
              const data = this.parseJson<SseErrorEvent>(eventData)
              if (data) {
                handlers.onError?.(data)
              }
              shouldStop = true
              break
            } else if (eventType === 'cancelled') {
              handlers.onCancelled?.()
              shouldStop = true
              break
            }
          } catch (err) {
            console.error('[SSE Client] Error parsing event:', err)
          }
        }
      }

      // Clean up after stream ends
      this.close()
    } catch (error) {
      if (error instanceof Error && error.name === 'AbortError') {
        handlers.onCancelled?.()
      } else {
        const errorMessage = error instanceof Error ? error.message : 'Failed to start conversation'
        handlers.onError?.({ error: errorMessage })
        console.error('[SSE Client] Connection error:', error)
      }
      this.close()
    }
  }

  /**
   * Close the SSE connection
   */
  close(): void {
    if (this.eventSource) {
      this.eventSource.close()
      this.eventSource = null
    }
    if (this.abortController) {
      this.abortController.abort()
      this.abortController = null
    }
    if (this.reader) {
      this.reader.cancel()
      this.reader = null
    }
  }

  private parseJson<T>(raw: unknown): T | null {
    if (!raw || typeof raw !== 'string') {
      return null
    }
    try {
      return JSON.parse(raw) as T
    } catch {
      return null
    }
  }
}

/**
 * Create a singleton instance
 */
export const aiConversationClient = new AiConversationClient()
