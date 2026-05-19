<script setup lang="ts">
import { computed } from 'vue'
import { renderMarkdown } from '@/shared/utils/markdown'

const props = defineProps<{
  title: string
  eyebrow: string
  markdown: string
}>()

const renderedHtml = computed(() => renderMarkdown(props.markdown))
</script>

<template>
  <div class="legal-view">
    <div class="legal-shell">
      <header class="legal-header">
        <div class="legal-eyebrow">{{ eyebrow }}</div>
        <h1>{{ title }}</h1>
      </header>

      <article class="legal-card legal-prose" v-html="renderedHtml" />
    </div>
  </div>
</template>

<style scoped>
.legal-view {
  min-height: 100vh;
  padding: 3rem 1.25rem 4rem;
  background:
    radial-gradient(circle at top left, color-mix(in srgb, var(--p-cyan-500) 14%, transparent), transparent 28rem),
    var(--p-surface-50);
}

:global(.app-dark) .legal-view {
  background:
    radial-gradient(circle at top left, color-mix(in srgb, var(--p-cyan-400) 12%, transparent), transparent 28rem),
    var(--p-surface-950);
}

.legal-shell {
  width: min(860px, 100%);
  margin: 0 auto;
}

.legal-header {
  margin-bottom: 1.5rem;
}

.legal-eyebrow {
  display: inline-flex;
  align-items: center;
  min-height: 1.8rem;
  padding: 0 0.75rem;
  border: 1px solid color-mix(in srgb, var(--p-cyan-500) 30%, transparent);
  border-radius: 999px;
  background: color-mix(in srgb, var(--p-cyan-500) 10%, transparent);
  color: var(--p-cyan-700);
  font-size: 0.74rem;
  font-weight: 800;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

:global(.app-dark) .legal-eyebrow {
  color: var(--p-cyan-200);
}

.legal-header h1 {
  margin: 1rem 0 0;
  color: var(--p-text-color);
  font-size: clamp(2rem, 4vw, 3.2rem);
  line-height: 0.98;
  letter-spacing: -0.04em;
}

.legal-card {
  padding: 2rem;
  border: 1px solid var(--p-surface-200);
  border-radius: 1rem;
  background: var(--p-surface-0);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.08);
}

:global(.app-dark) .legal-card {
  border-color: var(--p-surface-700);
  background: var(--p-surface-900);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.28);
}

.legal-prose :deep(h1),
.legal-prose :deep(h2),
.legal-prose :deep(h3) {
  color: var(--p-text-color);
  letter-spacing: -0.03em;
}

.legal-prose :deep(h1) {
  margin: 0 0 1.2rem;
  font-size: 2rem;
}

.legal-prose :deep(h2) {
  margin: 2rem 0 0.9rem;
  font-size: 1.32rem;
}

.legal-prose :deep(h3) {
  margin: 1.4rem 0 0.7rem;
  font-size: 1.08rem;
}

.legal-prose :deep(p),
.legal-prose :deep(li) {
  color: var(--p-text-muted-color);
  line-height: 1.72;
  font-size: 0.98rem;
}

.legal-prose :deep(p) {
  margin: 0 0 1rem;
}

.legal-prose :deep(ul) {
  margin: 0 0 1rem;
  padding-left: 1.3rem;
}

.legal-prose :deep(code) {
  padding: 0.1rem 0.35rem;
  border-radius: 0.35rem;
  background: var(--p-surface-100);
  color: var(--p-text-color);
  font-size: 0.92em;
}

:global(.app-dark) .legal-prose :deep(code) {
  background: var(--p-surface-800);
}

.legal-prose :deep(a) {
  color: var(--p-primary-500);
  text-decoration: none;
}

.legal-prose :deep(a:hover) {
  text-decoration: underline;
}

@media (max-width: 640px) {
  .legal-view {
    padding: 2rem 1rem 3rem;
  }

  .legal-card {
    padding: 1.4rem;
  }
}
</style>
