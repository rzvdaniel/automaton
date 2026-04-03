<script setup lang="ts">
import { computed } from 'vue'
import { toolDetailsExtensions } from './tool-details/extensions'

type ToolPayload = Record<string, unknown> | unknown[] | string | number | boolean | null
type DetailVisibility = 'full' | 'trim' | 'hidden'

const props = defineProps<{
  toolName?: string
  toolArgs?: unknown
  toolResult?: unknown
  toolStatus?: 'executing' | 'completed' | 'error'
  error?: string
  showDebugDetails?: boolean
}>()

interface ToolDetailPolicy {
  instructions: DetailVisibility
  result: DetailVisibility
  maxInstructionsChars: number
  maxResultChars: number
}

function tryParseJson(value: unknown): ToolPayload {
  if (typeof value !== 'string') {
    return (value as ToolPayload) ?? null
  }

  const trimmed = value.trim()
  if (!trimmed || (trimmed[0] !== '{' && trimmed[0] !== '[')) {
    return value
  }

  try {
    return JSON.parse(trimmed) as ToolPayload
  } catch {
    return value
  }
}

const parsedArgs = computed(() => tryParseJson(props.toolArgs))
const parsedResult = computed(() => tryParseJson(props.toolResult))

const toolExtensionComponent = computed(() => {
  const name = props.toolName
  if (!name) return null
  return toolDetailsExtensions[name as keyof typeof toolDetailsExtensions] ?? null
})

const isDebugMode = computed(() => props.showDebugDetails === true)

const defaultPolicy: ToolDetailPolicy = {
  instructions: 'trim',
  result: 'trim',
  maxInstructionsChars: 1200,
  maxResultChars: 1400,
}

const productionPolicies: Record<string, Partial<ToolDetailPolicy>> = {
  // Most noisy payload: file contents. Keep concise outside debug mode.
  read_file: {
    result: 'hidden',
  },
  // Still useful, but can be very long.
  search_file: {
    result: 'trim',
    maxResultChars: 1000,
  },
}

const effectivePolicy = computed<ToolDetailPolicy>(() => {
  if (isDebugMode.value) {
    return {
      instructions: 'full',
      result: 'full',
      maxInstructionsChars: Number.MAX_SAFE_INTEGER,
      maxResultChars: Number.MAX_SAFE_INTEGER,
    }
  }

  const toolName = props.toolName || ''
  const overrides = productionPolicies[toolName] ?? {}
  return {
    instructions: overrides.instructions ?? defaultPolicy.instructions,
    result: overrides.result ?? defaultPolicy.result,
    maxInstructionsChars: overrides.maxInstructionsChars ?? defaultPolicy.maxInstructionsChars,
    maxResultChars: overrides.maxResultChars ?? defaultPolicy.maxResultChars,
  }
})

function toPrettyString(value: unknown): string {
  if (value == null || value === '') return ''
  if (typeof value === 'string') return value
  try {
    return JSON.stringify(value, null, 2)
  } catch {
    return String(value)
  }
}

function isEmptyJsonObject(value: unknown): boolean {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return false
  }
  return Object.keys(value as Record<string, unknown>).length === 0
}

function trimText(value: string, limit: number): { value: string; trimmed: boolean } {
  if (value.length <= limit) return { value, trimmed: false }
  return {
    value: `${value.slice(0, limit)}\n... [trimmed ${value.length - limit} chars]`,
    trimmed: true,
  }
}

const resultSummary = computed(() => {
  const result = parsedResult.value
  if (result == null || result === '') return ''
  if (typeof result === 'string') {
    return result.length > 140 ? `${result.slice(0, 140)}...` : result
  }
  if (Array.isArray(result)) return `Array(${result.length})`
  if (typeof result === 'object') {
    const keys = Object.keys(result as Record<string, unknown>)
    return keys.length > 0 ? `Object(${keys.join(', ')})` : 'Object'
  }
  return String(result)
})

const rawPrettyArgs = computed(() => toPrettyString(parsedArgs.value))
const rawPrettyResult = computed(() => toPrettyString(parsedResult.value))

const instructionsDisplay = computed(() => {
  const mode = effectivePolicy.value.instructions
  if (mode === 'hidden') return { visible: false, content: '', trimmed: false }

  if (isEmptyJsonObject(parsedArgs.value)) {
    return { visible: false, content: '', trimmed: false }
  }

  if (!rawPrettyArgs.value) return { visible: false, content: '', trimmed: false }
  if (mode === 'full') return { visible: true, content: rawPrettyArgs.value, trimmed: false }

  const trimmed = trimText(rawPrettyArgs.value, effectivePolicy.value.maxInstructionsChars)
  return { visible: true, content: trimmed.value, trimmed: trimmed.trimmed }
})

const resultDisplay = computed(() => {
  const mode = effectivePolicy.value.result
  if (mode === 'hidden') return { visible: false, content: '', trimmed: false, hidden: true }

  if (!rawPrettyResult.value) return { visible: false, content: '', trimmed: false, hidden: false }
  if (mode === 'full') return { visible: true, content: rawPrettyResult.value, trimmed: false, hidden: false }

  const trimmed = trimText(rawPrettyResult.value, effectivePolicy.value.maxResultChars)
  return { visible: true, content: trimmed.value, trimmed: trimmed.trimmed, hidden: false }
})
</script>

<template>
  <div class="tool-details">
    <div v-if="toolStatus === 'error' && error" class="tool-error">
      {{ error }}
    </div>

    <div v-if="resultSummary" class="tool-summary">
      {{ resultSummary }}
    </div>

    <component
      :is="toolExtensionComponent"
      v-if="toolExtensionComponent"
      :tool-name="toolName"
      :tool-args="toolArgs"
      :tool-result="toolResult"
      :tool-status="toolStatus"
      :error="error"
    />

    <div v-if="instructionsDisplay.visible" class="tool-json-block">
      <div class="tool-json-label">Instructions</div>
      <pre class="tool-json-content">{{ instructionsDisplay.content }}</pre>
      <div v-if="instructionsDisplay.trimmed" class="tool-json-hint">
        Trimmed for normal view. Enable AI debug details in Settings to see full payload.
      </div>
    </div>

    <div v-if="resultDisplay.hidden" class="tool-json-block">
      <div class="tool-json-label">Result</div>
      <div class="tool-json-hint">
        Hidden in normal view. Enable AI debug details in Settings to see full payload.
      </div>
    </div>

    <div v-else-if="resultDisplay.visible" class="tool-json-block">
      <div class="tool-json-label">Result</div>
      <pre class="tool-json-content">{{ resultDisplay.content }}</pre>
      <div v-if="resultDisplay.trimmed" class="tool-json-hint">
        Trimmed for normal view. Enable AI debug details in Settings to see full payload.
      </div>
    </div>
  </div>
</template>

<style scoped>
.tool-details {
  margin-top: 0.35rem;
  padding-top: 0.35rem;
  border-top: 1px dashed var(--p-surface-300);
}

.tool-error {
  font-size: 0.72rem;
  color: var(--p-red-500);
  margin-bottom: 0.25rem;
  white-space: pre-wrap;
}

.tool-summary {
  font-size: 0.7rem;
  color: var(--p-text-muted-color);
  margin-bottom: 0.25rem;
}

.tool-json-block {
  margin-top: 0.25rem;
}

.tool-json-label {
  font-size: 0.68rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  margin-bottom: 0.2rem;
}

.tool-json-content {
  margin: 0;
  padding: 0.4rem;
  font-size: 0.66rem;
  line-height: 1.35;
  border-radius: 4px;
  border: 1px solid var(--p-surface-300);
  background: var(--p-surface-100);
  color: var(--p-text-color);
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 12rem;
  overflow: auto;
}

.tool-json-hint {
  margin-top: 0.2rem;
  font-size: 0.63rem;
  color: var(--p-text-muted-color);
}

:global(.app-dark) .tool-details {
  border-color: var(--p-surface-600);
}

:global(.app-dark) .tool-json-content {
  border-color: var(--p-surface-600);
  background: var(--p-surface-900);
}
</style>
