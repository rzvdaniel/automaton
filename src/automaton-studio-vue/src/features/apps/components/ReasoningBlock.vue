<script setup lang="ts">
/**
 * ReasoningBlock.vue
 * Collapsible display for chain-of-thought reasoning produced by thinking models
 * (e.g. Moonshot kimi-k2.5). Collapsed by default; always collapsible.
 */
import { ref } from 'vue'

defineProps<{
  content: string
}>()

const isExpanded = ref(false)

function toggle() {
  isExpanded.value = !isExpanded.value
}
</script>

<template>
  <div class="reasoning-block" :class="{ expanded: isExpanded }">
    <div class="reasoning-header" @click="toggle">
      <i class="pi" :class="isExpanded ? 'pi-chevron-down' : 'pi-chevron-right'" />
      <i class="reasoning-icon pi pi-sparkles" />
      <span class="reasoning-label">Thinking</span>
    </div>
    <div v-if="isExpanded" class="reasoning-body">
      <pre class="reasoning-text">{{ content }}</pre>
    </div>
  </div>
</template>

<style scoped>
.reasoning-block {
  border: 1px solid var(--p-surface-200);
  border-radius: 6px;
  overflow: hidden;
  font-size: 0.8rem;
  margin-bottom: 0.5rem;
}

.reasoning-header {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.35rem 0.6rem;
  cursor: pointer;
  user-select: none;
  color: var(--p-text-muted-color);
  transition: background 0.15s;
}

.reasoning-header:hover {
  background: var(--p-surface-100);
}

.reasoning-header .pi-chevron-down,
.reasoning-header .pi-chevron-right {
  font-size: 0.65rem;
  flex-shrink: 0;
}

.reasoning-icon {
  font-size: 0.7rem;
  color: var(--p-violet-400);
}

.reasoning-label {
  font-size: 0.75rem;
  font-style: italic;
}

.reasoning-body {
  border-top: 1px solid var(--p-surface-200);
  max-height: 300px;
  overflow-y: auto;
}

.reasoning-text {
  margin: 0;
  padding: 0.6rem 0.75rem;
  font-size: 0.75rem;
  line-height: 1.55;
  white-space: pre-wrap;
  word-break: break-word;
  color: var(--p-text-muted-color);
  font-family: inherit;
}

/* Dark mode */
:global(.app-dark) .reasoning-block {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .reasoning-header:hover {
  background: var(--p-surface-800);
}

:global(.app-dark) .reasoning-body {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .reasoning-icon {
  color: var(--p-violet-300);
}
</style>
