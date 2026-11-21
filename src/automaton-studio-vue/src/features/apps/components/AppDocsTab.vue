<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { appsService } from '../services/apps.service'
import { renderMarkdown } from '@/shared/utils/markdown'
import { resolveDocsPath } from '../docs-path'

const props = defineProps<{ appId: string; description?: string | null }>()

const ENTRY = 'docs/README.md'
const path = ref(ENTRY)
const markdown = ref<string | null>(null)
const loading = ref(true)
const failed = ref(false)

const html = computed(() => (markdown.value === null ? '' : renderMarkdown(markdown.value)))

async function open(target: string) {
  loading.value = true
  failed.value = false
  path.value = target
  try {
    markdown.value = await appsService.readSourceFile(props.appId, target)
  } catch {
    markdown.value = null
    // A missing entry page is normal (the app ships no docs); a missing linked page is an error.
    failed.value = target !== ENTRY
  } finally {
    loading.value = false
  }
}

/** Relative links open another page of the app's docs; other links keep the browser's behaviour. */
function onClick(event: MouseEvent) {
  const anchor = (event.target as HTMLElement).closest('a')
  const href = anchor?.getAttribute('href')
  if (!anchor || !href || /^[a-z][a-z0-9+.-]*:/i.test(href)) return
  event.preventDefault()
  if (href.startsWith('#')) return
  const next = resolveDocsPath(path.value, href)
  if (next) void open(next)
}

onMounted(() => open(ENTRY))
</script>

<template>
  <div class="docs" data-testid="docs-tab">
    <p v-if="loading" class="hint">Loading…</p>
    <template v-else-if="markdown !== null">
      <button v-if="path !== ENTRY" type="button" class="back" data-testid="docs-home" @click="open(ENTRY)">← Docs home</button>
      <article class="prose" data-testid="docs-page" @click="onClick" v-html="html" />
    </template>
    <p v-else-if="failed" class="hint" data-testid="docs-missing">This page is not in the app's docs. <button type="button" class="back" @click="open(ENTRY)">Docs home</button></p>
    <div v-else data-testid="docs-fallback">
      <p class="hint">This app ships no docs. Add <code>docs/README.md</code> to its folder and deploy.</p>
      <p v-if="description">{{ description }}</p>
    </div>
  </div>
</template>

<style scoped>
.hint { margin: 0 0 8px; color: var(--ink-soft); font-size: 13px; }
.back { border: 0; background: transparent; color: inherit; font: inherit; font-size: 13px; text-decoration: underline; cursor: pointer; padding: 0; margin-bottom: 8px; }
.prose { max-width: 70ch; line-height: 1.55; }
.prose :deep(pre) { overflow: auto; padding: 10px 12px; border: 1px solid var(--line); border-radius: 8px; }
.prose :deep(code) { font-family: var(--mono); font-size: 13px; }
</style>
