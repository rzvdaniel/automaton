/**
 * AI Streaming Service
 * Handles SSE streaming connections to the backend AI endpoint
 */
import type {
  AiChatConfig,
  StreamChunk,
  StreamingCallbacks,
  ConversationRequest,
  AiChatRole,
} from '@/shared/types/ai-chat.types'
import axiosInstance from '../api/axios-instance'

export interface StreamConversationRequest {
  conversationId?: string
  resourceType: string
  resourceId?: string
  language?: string
  message: string
  providerName?: string
  model?: string
  apiKeyId?: string
  history?: Array<{ role: AiChatRole; content: string }>
  context?: Record<string, unknown>
}

export interface ConversationResponse {
  conversationId: string
  content: string
  generatedCode?: string
  codeLanguage?: string
  toolCalls: Array<{
    id: string
    name: string
    description: string
    arguments: Record<string, unknown>
  }>
  toolResults: Array<{
    toolId: string
    toolName: string
    isSuccess: boolean
    data?: string
    error?: string
  }>
}

/**
 * Stream an AI conversation using Server-Sent Events
 * @returns AbortController to cancel the request
 */
export async function streamConversation(
  request: StreamConversationRequest,
  callbacks: StreamingCallbacks,
): Promise<AbortController> {
  const abortController = new AbortController()

  // Generate conversation ID if not provided
  const conversationId = request.conversationId || crypto.randomUUID()

  try {
    // Get the auth token
    const token = localStorage.getItem('auth_token')
    if (!token) {
      callbacks.onError(new Error('Not authenticated'))
      return abortController
    }

    // Build the full request URL
    const baseUrl = import.meta.env.VITE_API_URL || ''
    const url = `${baseUrl}/api/ai/stream`

    // Use fetch with streaming for SSE
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
        Accept: 'text/event-stream',
      },
      body: JSON.stringify({
        ...request,
        conversationId,
      }),
      signal: abortController.signal,
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(`HTTP ${response.status}: ${errorText}`)
    }

    if (!response.body) {
      throw new Error('Response body is null')
    }

    // Read the stream
    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    while (true) {
      const { done, value } = await reader.read()

      if (done) {
        callbacks.onComplete()
        break
      }

      // Decode the chunk and add to buffer
      buffer += decoder.decode(value, { stream: true })

      // Parse SSE events from buffer
      const lines = buffer.split('\n')
      buffer = lines.pop() || '' // Keep incomplete line in buffer

      let eventType = 'message'

      for (const line of lines) {
        if (line.startsWith('event: ')) {
          eventType = line.substring(7).trim()
        } else if (line.startsWith('data: ')) {
          const data = line.substring(6)
          try {
            const chunk = JSON.parse(data) as StreamChunk
            callbacks.onChunk(chunk)

            if (eventType === 'done' || chunk.type === 'complete') {
              callbacks.onComplete()
            } else if (eventType === 'error' || chunk.type === 'error') {
              callbacks.onError(new Error(chunk.content || 'Unknown error'))
            }
          } catch (e) {
            console.warn('Failed to parse SSE data:', data, e)
          }
        }
      }
    }
  } catch (error) {
    if ((error as Error).name === 'AbortError') {
      // User cancelled - don't call error callback
      callbacks.onComplete()
    } else {
      callbacks.onError(error as Error)
    }
  }

  return abortController
}

/**
 * Non-streaming conversation (simpler but no real-time updates)
 */
export async function sendConversation(
  request: StreamConversationRequest,
): Promise<ConversationResponse> {
  const response = await axiosInstance.post<ConversationResponse>('/api/ai/conversation', request)
  return response.data
}

/**
 * Simulate streaming for demo/fallback when backend is not available
 * This provides a realistic UI experience without a backend connection
 */
export async function simulateStreaming(
  request: StreamConversationRequest,
  callbacks: StreamingCallbacks,
): Promise<AbortController> {
  const abortController = new AbortController()

  // Simulate AI processing delay
  await delay(500)

  if (abortController.signal.aborted) return abortController

  // Send thinking indicator
  callbacks.onChunk({
    type: 'reasoning',
    content: `Analyzing request for ${request.resourceType}...`,
  })

  await delay(800)

  if (abortController.signal.aborted) return abortController

  // Simulate tool call for apps
  if (request.resourceType === 'app') {
    callbacks.onChunk({
      type: 'tool_call',
      toolCall: {
        id: 'tool-1',
        name: 'get_app_files',
        description: 'Reading app files...',
        arguments: {},
      },
    })

    await delay(600)

    callbacks.onChunk({
      type: 'tool_result',
      toolResult: {
        toolId: 'tool-1',
        toolName: 'get_app_files',
        isSuccess: true,
        data: 'Found: index.html, styles.css, app.js',
      },
    })

    await delay(400)
  }

  if (abortController.signal.aborted) return abortController

  // Generate response based on message content
  const response = generateSimulatedResponse(request)

  // Stream the response character by character (simulated)
  callbacks.onChunk({
    type: 'text',
    content: response.text,
  })

  await delay(300)

  // If there's code, send it separately
  if (response.code) {
    callbacks.onChunk({
      type: 'code_generated',
      content: `Here's the code:\n\`\`\`${response.language || 'html'}\n${response.code}\n\`\`\``,
      code: response.code,
      language: response.language,
      fileName: response.fileName,
    })
  }

  await delay(200)

  callbacks.onChunk({ type: 'complete' })
  callbacks.onComplete()

  return abortController
}

function generateSimulatedResponse(request: StreamConversationRequest): {
  text: string
  code?: string
  language?: string
  fileName?: string
} {
  const message = request.message.toLowerCase()

  // App-related responses
  if (request.resourceType === 'app') {
    if (message.includes('login') || message.includes('form')) {
      return {
        text: "I'll create a login form for you with email and password fields, including validation and styling.",
        code: `<!-- Login Form -->
<div class="login-container">
  <h2>Welcome Back</h2>
  <form id="loginForm" onsubmit="handleLogin(event)">
    <div class="form-group">
      <label for="email">Email</label>
      <input type="email" id="email" name="email" required placeholder="Enter your email">
    </div>
    <div class="form-group">
      <label for="password">Password</label>
      <input type="password" id="password" name="password" required placeholder="Enter your password">
    </div>
    <button type="submit" class="btn-primary">Sign In</button>
  </form>
</div>

<style>
.login-container {
  max-width: 400px;
  margin: 50px auto;
  padding: 30px;
  background: white;
  border-radius: 8px;
  box-shadow: 0 2px 10px rgba(0,0,0,0.1);
}

.form-group {
  margin-bottom: 20px;
}

.form-group label {
  display: block;
  margin-bottom: 5px;
  font-weight: 500;
}

.form-group input {
  width: 100%;
  padding: 10px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 16px;
}

.btn-primary {
  width: 100%;
  padding: 12px;
  background: #3b82f6;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 16px;
}

.btn-primary:hover {
  background: #2563eb;
}
</style>

<script>
function handleLogin(event) {
  event.preventDefault();
  const email = document.getElementById('email').value;
  const password = document.getElementById('password').value;
  console.log('Login attempt:', { email });
  // Add your login logic here
}
</script>`,
        language: 'html',
        fileName: 'index.html',
      }
    }

    if (message.includes('navigation') || message.includes('navbar') || message.includes('menu')) {
      return {
        text: "I'll add a responsive navigation bar to your app.",
        code: `<nav class="navbar">
  <div class="nav-brand">MyApp</div>
  <button class="nav-toggle" onclick="toggleMenu()">
    <i class="pi pi-bars"></i>
  </button>
  <ul class="nav-menu">
    <li><a href="#home">Home</a></li>
    <li><a href="#about">About</a></li>
    <li><a href="#services">Services</a></li>
    <li><a href="#contact">Contact</a></li>
  </ul>
</nav>`,
        language: 'html',
        fileName: 'index.html',
      }
    }
  }

  // Default response
  return {
    text: `I understand you want to ${request.message}. Let me help you with that. In a production environment, I would analyze your request and generate appropriate code using AI. For now, this is a simulated response to demonstrate the chat interface functionality.`,
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

export const aiStreamingService = {
  streamConversation,
  sendConversation,
  simulateStreaming,
}

export default aiStreamingService
