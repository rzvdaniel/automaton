<script setup lang="ts">
/**
 * AiConsoleView.vue
 * Terminal-style console view for AI chat messages.
 * User prompts are rendered in green; AI answers in white/light-gray.
 * Tool calls, reasoning blocks, and per-message token counts are all visible.
 * Inspired by the JS/UIX terminal aesthetic (https://masswerk.at/jsuix/).
 */
import { ref, watch, nextTick, computed } from 'vue'
import type { ChatMessage, ChatSession } from '@/shared/types/ai.types'

const props = defineProps<{
  messages: ChatMessage[]
  session?: ChatSession | null
  isBusy?: boolean
}>()

const consoleBodyRef = ref<HTMLElement | null>(null)

// ── Helpers ──────────────────────────────────────────────────────────────────

function formatTimestamp(ts: Date): string {
  return ts.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

function formatTokenLabel(
  inputTokens?: number,
  outputTokens?: number,
  totalTokens?: number,
): string {
  if (totalTokens) return `[${totalTokens} tkn]`
  const parts: string[] = []
  if (inputTokens) parts.push(`↑${inputTokens}`)
  if (outputTokens) parts.push(`↓${outputTokens}`)
  return parts.length > 0 ? `[${parts.join(' ')} tkn]` : ''
}

function truncate(value: unknown, maxLen = 160): string {
  if (value === undefined || value === null) return ''
  try {
    const s = typeof value === 'string' ? value : JSON.stringify(value)
    return s.length > maxLen ? s.slice(0, maxLen) + '…' : s
  } catch {
    return String(value)
  }
}

// ── Line model ────────────────────────────────────────────────────────────────

type LineKind =
  | 'user-prompt'
  | 'ai-text'
  | 'tool-call'
  | 'tool-result'
  | 'tool-error'
  | 'reasoning'
  | 'separator'

interface ConsoleLine {
  id: string
  kind: LineKind
  prefix: string
  content: string
  timestamp?: Date
  tokenLabel?: string
  streaming?: boolean
}

// ── Build flat console lines from messages ─────────────────────────────────

const lines = computed<ConsoleLine[]>(() => {
  const out: ConsoleLine[] = []

  for (const msg of props.messages) {
    const tokens = formatTokenLabel(msg.inputTokens, msg.outputTokens, msg.totalTokens)

    // ── USER prompt ──────────────────────────────────────────────────────────
    if (msg.role === 'user') {
      out.push({
        id: `${msg.id}-user`,
        kind: 'user-prompt',
        prefix: '> ',
        content: msg.content,
        timestamp: msg.timestamp,
        tokenLabel: tokens || undefined,
      })
      continue
    }

    // ── ASSISTANT messages ────────────────────────────────────────────────────

    // Chain-of-thought / reasoning block
    if (msg.reasoningContent) {
      out.push({
        id: `${msg.id}-reasoning`,
        kind: 'reasoning',
        prefix: '  ◌ [thinking] ',
        content: msg.reasoningContent,
        timestamp: msg.timestamp,
      })
    }

    // Tool execution
    if (msg.type === 'toolExecution' && msg.toolName) {
      const argsSnippet = truncate(msg.toolArgs)
      out.push({
        id: `${msg.id}-tool`,
        kind: 'tool-call',
        prefix: `  ▶ TOOL [${msg.toolName}]`,
        content: argsSnippet ? ` → ${argsSnippet}` : '',
        timestamp: msg.timestamp,
      })

      // Inline tool result / error
      if (msg.toolResult !== undefined && msg.toolResult !== null) {
        const resultSnippet = truncate(msg.toolResult, 200)
        const statusIcon = msg.toolStatus === 'error' ? '✗' : '✓'
        out.push({
          id: `${msg.id}-result`,
          kind: msg.toolStatus === 'error' ? 'tool-error' : 'tool-result',
          prefix: `    ${statusIcon} `,
          content: resultSnippet,
        })
      }
      continue
    }

    // Regular text answer (skip empty content)
    if (msg.content) {
      out.push({
        id: `${msg.id}-ai`,
        kind: 'ai-text',
        prefix: '  ',
        content: msg.content,
        timestamp: msg.timestamp,
        tokenLabel: tokens || undefined,
        streaming: msg.isStreaming,
      })
    }
  }

  return out
})

// ── Auto-scroll ────────────────────────────────────────────────────────────

async function scrollToBottom() {
  await nextTick()
  if (consoleBodyRef.value) {
    consoleBodyRef.value.scrollTop = consoleBodyRef.value.scrollHeight
  }
}

watch(() => props.messages.length, scrollToBottom)
watch(() => props.isBusy, scrollToBottom)
</script>

<template>
  <div class="ai-console-wrapper">
    <!-- Terminal header bar -->
    <div class="console-header">
      <span class="console-brand">JS/UIX – AI Console</span>
      <span v-if="session?.title" class="console-session-title">{{ session.title }}</span>
      <span v-else class="console-session-title console-session-empty">no active session</span>
      <span class="console-hint">prompts in green · answers in white · tool calls in cyan</span>
    </div>

    <!-- Scrollable output body -->
    <div ref="consoleBodyRef" class="console-body">
      <template v-if="lines.length === 0 && !isBusy">
        <div class="console-line line-ai-text">
          <span class="console-ts">--:--:--</span>
          <span class="console-prefix">  </span>
          <span class="console-content content-ai-text">
            AI Console is waiting. Start a conversation in the AI Assistant panel.
          </span>
        </div>
      </template>

      <div
        v-for="line in lines"
        :key="line.id"
        class="console-line"
        :class="`line-${line.kind}`"
      >
        <span class="console-ts">{{ line.timestamp ? formatTimestamp(line.timestamp) : '        ' }}</span>
        <span class="console-prefix" :class="`prefix-${line.kind}`">{{ line.prefix }}</span>
        <span class="console-content" :class="`content-${line.kind}`">{{ line.content }}</span>
        <span v-if="line.tokenLabel" class="console-tokens">{{ line.tokenLabel }}</span>
        <span v-if="line.streaming" class="console-cursor">█</span>
      </div>

      <!-- Busy / thinking indicator -->
      <div v-if="isBusy" class="console-line line-ai-text">
        <span class="console-ts">{{ formatTimestamp(new Date()) }}</span>
        <span class="console-prefix prefix-ai-text">  </span>
        <span class="console-content content-ai-text">Processing</span>
        <span class="console-cursor blink">█</span>
      </div>
    </div>

    <!-- Footer status bar -->
    <div class="console-footer">
      <span>
        {{ lines.length }} line{{ lines.length !== 1 ? 's' : '' }} ·
        {{ props.messages.filter(m => m.role === 'user').length }} prompt{{ props.messages.filter(m => m.role === 'user').length !== 1 ? 's' : '' }} ·
        tokens:
        {{ props.messages.reduce((sum, m) => sum + (m.totalTokens ?? (m.inputTokens ?? 0) + (m.outputTokens ?? 0)), 0).toLocaleString() }}
      </span>
      <span v-if="isBusy" class="console-footer-busy">● BUSY</span>
      <span v-else class="console-footer-idle">● IDLE</span>
    </div>
  </div>
</template>

<style scoped>
/* ─────────────────────────────────────────────────
   Base — monospace dark terminal
───────────────────────────────────────────────── */
.ai-console-wrapper {
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: #0a0a0a;
  font-family: 'Courier New', 'Lucida Console', 'Consolas', monospace;
  font-size: 0.82rem;
  color: #c8c8c8;
  overflow: hidden;
}

/* ─────────────────────────────────────────────────
   Header bar
───────────────────────────────────────────────── */
.console-header {
  display: flex;
  align-items: center;
  gap: 1.25rem;
  padding: 0.4rem 1rem;
  background-color: #060606;
  border-bottom: 1px solid #1a3a1a;
  flex-shrink: 0;
}

.console-brand {
  color: #00cc00;
  font-weight: bold;
  letter-spacing: 0.06em;
  flex-shrink: 0;
}

.console-session-title {
  color: #a0a0a0;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.console-session-empty {
  color: #444;
  font-style: italic;
}

.console-hint {
  color: #3a3a3a;
  font-size: 0.72rem;
  flex-shrink: 0;
}

/* ─────────────────────────────────────────────────
   Scrollable body
───────────────────────────────────────────────── */
.console-body {
  flex: 1;
  overflow-y: auto;
  padding: 0.75rem 1rem 1rem;
  line-height: 1.65;
}

.console-body::-webkit-scrollbar {
  width: 6px;
}
.console-body::-webkit-scrollbar-track {
  background: #0a0a0a;
}
.console-body::-webkit-scrollbar-thumb {
  background: #1a4a1a;
  border-radius: 3px;
}

/* ─────────────────────────────────────────────────
   Individual lines
───────────────────────────────────────────────── */
.console-line {
  display: flex;
  align-items: flex-start;
  gap: 0;
  margin-bottom: 0.05rem;
  white-space: pre-wrap;
  word-break: break-word;
}

/* Timestamp column — fixed width, dim */
.console-ts {
  color: #383838;
  font-size: 0.72rem;
  flex-shrink: 0;
  width: 8ch;
  margin-right: 0.5rem;
  padding-top: 0.05rem;
}

/* Prefix (prompt symbol, tool label, etc.) */
.console-prefix {
  flex-shrink: 0;
  white-space: pre;
}

/* ─── Content colour classes ─── */

/* USER → bright green */
.prefix-user-prompt {
  color: #00cc00;
  font-weight: bold;
}
.content-user-prompt {
  color: #00ee00;
  font-weight: 500;
}

/* AI text → near-white */
.prefix-ai-text {
  color: transparent; /* invisible spacer */
}
.content-ai-text {
  color: #e2e2e2;
}

/* Reasoning → dim italic */
.prefix-reasoning {
  color: #505050;
}
.content-reasoning {
  color: #686868;
  font-style: italic;
}

/* Tool call → cyan */
.prefix-tool-call {
  color: #00aacc;
  font-weight: bold;
}
.content-tool-call {
  color: #80ccdd;
}

/* Tool result → muted teal */
.prefix-tool-result {
  color: #00aa44;
}
.content-tool-result {
  color: #77aaaa;
}

/* Tool error → red */
.prefix-tool-error {
  color: #cc3333;
  font-weight: bold;
}
.content-tool-error {
  color: #cc6666;
}

/* Token label — very dim, right-margin */
.console-tokens {
  color: #3a3a50;
  font-size: 0.72rem;
  margin-left: 0.6rem;
  flex-shrink: 0;
  padding-top: 0.05rem;
}

/* Blinking cursor */
.console-cursor {
  color: #00cc00;
}
.console-cursor.blink {
  animation: cursor-blink 1s step-end infinite;
}
@keyframes cursor-blink {
  0%, 100% { opacity: 1; }
  50%       { opacity: 0; }
}

/* ─────────────────────────────────────────────────
   Footer status bar
───────────────────────────────────────────────── */
.console-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.3rem 1rem;
  background-color: #060606;
  border-top: 1px solid #1a3a1a;
  color: #3a3a3a;
  font-size: 0.72rem;
  flex-shrink: 0;
}

.console-footer-busy {
  color: #cc8800;
  animation: cursor-blink 1.5s step-end infinite;
}

.console-footer-idle {
  color: #1a4a1a;
}
</style>
