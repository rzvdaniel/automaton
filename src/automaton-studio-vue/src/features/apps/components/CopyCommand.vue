<script setup lang="ts">
import { onUnmounted, ref } from 'vue'

const props = withDefaults(defineProps<{
  command: string
  buttonLabel?: string
  showCommand?: boolean
}>(), {
  buttonLabel: 'Copy command',
  showCommand: true,
})

type CopyState = 'idle' | 'copied' | 'failed'

const copyState = ref<CopyState>('idle')
let resetTimer: ReturnType<typeof setTimeout> | undefined

const buttonText = () => {
  if (copyState.value === 'copied') return 'Copied'
  if (copyState.value === 'failed') return 'Copy failed'
  return props.buttonLabel
}

const buttonAriaLabel = () => {
  if (copyState.value !== 'idle') return buttonText()
  return `${props.buttonLabel}: ${props.command}`
}

const statusMessage = () => {
  if (copyState.value === 'copied') return 'Command copied to clipboard.'
  if (copyState.value === 'failed') return 'Could not copy command.'
  return ''
}

async function copyCommand() {
  if (resetTimer) clearTimeout(resetTimer)

  try {
    await navigator.clipboard.writeText(props.command)
    copyState.value = 'copied'
  } catch {
    copyState.value = 'failed'
  }

  resetTimer = setTimeout(() => {
    copyState.value = 'idle'
    resetTimer = undefined
  }, 2000)
}

onUnmounted(() => {
  if (resetTimer) clearTimeout(resetTimer)
})
</script>

<template>
  <div class="copy-command" :class="{ 'copy-command--button-only': !showCommand }">
    <code v-if="showCommand">{{ command }}</code>
    <button
      class="copy-command__button"
      type="button"
      :aria-label="buttonAriaLabel()"
      @click="copyCommand"
    >
      <i :class="copyState === 'copied' ? 'pi pi-check' : copyState === 'failed' ? 'pi pi-exclamation-circle' : 'pi pi-copy'" aria-hidden="true"></i>
      {{ buttonText() }}
    </button>
    <span class="copy-command__status" role="status" aria-live="polite">{{ statusMessage() }}</span>
  </div>
</template>

<style scoped>
.copy-command {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  min-width: 0;
}

.copy-command code {
  min-width: 0;
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}

.copy-command__button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--p-primary-color);
  font: inherit;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
}

.copy-command__button:focus-visible {
  outline: 2px solid var(--p-primary-color);
  outline-offset: 3px;
}

.copy-command__status {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
