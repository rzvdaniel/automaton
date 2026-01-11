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

  constructor(baseUrl: string = import.meta.env.VITE_API_BASE_URL || '') {
    this.baseUrl = baseUrl
  }

  /**
   * Start a streaming conversation with AI
   */
  async startConversation(
    request: StreamConversationRequest,
    handlers: SseEventHandler,
    abortSignal?: AbortSignal,
  ): Promise<void> {
    // Close any existing connection
    this.close()

    try {
      // Get auth token
      const token = localStorage.getItem('auth_token')
      if (!token) {
        throw new Error('No authentication token found')
      }

      const url = `${this.baseUrl}/api/ai/conversation/stream`

      // Use event-source-polyfill for POST support
      const EventSourcePolyfill = await this.getEventSourcePolyfill()

      this.eventSource = new EventSourcePolyfill(url, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      })

      // Handle update events
      if (this.eventSource) {
        this.eventSource.addEventListener('update', (event: MessageEvent) => {
          try {
            const data: SseConversationUpdate = JSON.parse(event.data)
            handlers.onUpdate?.(data)
          } catch (error) {
            console.error('Error parsing update event:', error)
          }
        })

        // Handle done events
        this.eventSource.addEventListener('done', (event: MessageEvent) => {
          try {
            const data: SseDoneEvent = JSON.parse(event.data)
            handlers.onDone?.(data)
            this.close()
          } catch (error) {
            console.error('Error parsing done event:', error)
          }
        })

        // Handle error events
        this.eventSource.addEventListener('error', (event: MessageEvent) => {
          try {
            const data: SseErrorEvent = JSON.parse(event.data)
            handlers.onError?.(data)
            this.close()
          } catch (error) {
            console.error('Error parsing error event:', error)
          }
        })

        // Handle cancelled events
        this.eventSource.addEventListener('cancelled', () => {
          handlers.onCancelled?.()
          this.close()
        })

        // Handle generic errors (connection issues)
        this.eventSource.onerror = () => {
          handlers.onError?.({ error: 'Connection to AI server lost' })
          this.close()
        }
      }

      // Handle abort signal
      if (abortSignal) {
        abortSignal.addEventListener('abort', () => {
          this.close()
        })
      }
    } catch (error) {
      handlers.onError?.({
        error: error instanceof Error ? error.message : 'Failed to start conversation',
      })
      throw error
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
  }

  /**
   * Get EventSource polyfill for POST support
   */
  private async getEventSourcePolyfill(): Promise<any> {
    try {
      const module = await import('event-source-polyfill')
      return module.EventSourcePolyfill
    } catch (error) {
      console.warn('event-source-polyfill not available, falling back to native EventSource')
      return EventSource
    }
  }
}

/**
 * Create a singleton instance
 */
export const aiConversationClient = new AiConversationClient()
