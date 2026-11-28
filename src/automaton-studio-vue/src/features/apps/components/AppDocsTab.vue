<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { appsService } from '../services/apps.service'
import { renderMarkdown } from '@/shared/utils/markdown'
import { resolveDocsPath } from '../docs-path'
import type { ApplicationContract } from '../types/apps.types'
import { docsAtAGlance } from '../utils/app-overview'

const props = defineProps<{
  appId: string
  description?: string | null
  version?: string | null
  application?: ApplicationContract | null
}>()
const emit = defineEmits<{ navigate: [view: 'source'] }>()

/** The app's README.md; apps that keep their pages in docs/ open docs/README.md. */
const ENTRIES = ['README.md', 'docs/README.md']
const ENTRY = ENTRIES[0]!
const path = ref(ENTRY)
const markdown = ref<string | null>(null)
const loading = ref(true)
const failed = ref(false)

const root = ref<HTMLElement | null>(null)
const page = ref<HTMLElement | null>(null)
let objectUrls: string[] = []

const html = computed(() => (markdown.value === null ? '' : renderMarkdown(markdown.value)))

const glanceItems = computed(() => {
  const info = docsAtAGlance(props.application)
  if (!info) return []
  const items: Array<{ key: string; label: string; lines: string[] }> = []
  if (info.pages) items.push({ key: 'pages', label: 'Pages', lines: [info.pages] })
  if (info.schedules.length) items.push({ key: 'scheduled', label: 'Scheduled', lines: info.schedules })
  if (info.needs) items.push({ key: 'needs', label: 'Needs', lines: [info.needs] })
  return items
})

async function open(target: string) {
  loading.value = true
  failed.value = false
  revokeImages()
  path.value = target
  markdown.value = null
  for (const candidate of target === ENTRY ? ENTRIES : [target]) {
    try {
      const text = await appsService.readSourceFile(props.appId, candidate)
      if (text === null) continue
      markdown.value = text
      path.value = candidate
      break
    } catch { /* try the next candidate */ }
  }
  // A missing entry page is normal (the app ships no docs); a missing linked page is an error.
  failed.value = markdown.value === null && target !== ENTRY
  loading.value = false
  if (markdown.value !== null) {
    void loadImages(path.value)
    void buildOutline()
  } else {
    outline.value = []
  }
}

const outline = ref<Array<{ id: string; text: string; level: number }>>([])

/** Gives each heading an id and lists them (with At a glance first when shown) for the "On this page" navigation. */
async function buildOutline() {
  await nextTick()
  const headings = Array.from(page.value?.querySelectorAll<HTMLElement>('h1, h2, h3') ?? [])
  const items = headings.map((heading, index) => {
    heading.id = `docs-h-${index}`
    return { id: heading.id, text: heading.textContent ?? '', level: Number(heading.tagName.slice(1)) }
  })
  outline.value = glanceItems.value.length > 0
    ? [{ id: 'docs-at-a-glance', text: 'At a glance', level: 1 }, ...items]
    : items
}

function jump(id: string) {
  root.value?.querySelector<HTMLElement>(`#${id}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

function revokeImages() {
  objectUrls.forEach((url) => URL.revokeObjectURL(url))
  objectUrls = []
}

/** Loads the page's relative images (they need the user's sign-in, so a plain src would not work). */
async function loadImages(from: string) {
  await nextTick()
  const images = Array.from(page.value?.querySelectorAll<HTMLImageElement>('img[data-docs-src]') ?? [])
  await Promise.all(images.map(async (image) => {
    const target = resolveDocsPath(from, image.getAttribute('data-docs-src') ?? '')
    if (!target) return
    try {
      const url = URL.createObjectURL(await appsService.readSourceImage(props.appId, target))
      if (path.value !== from) { URL.revokeObjectURL(url); return }
      objectUrls.push(url)
      image.src = url
    } catch { /* the alt text stays */ }
  }))
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
watch(() => props.appId, () => open(ENTRY))
watch(glanceItems, () => {
  if (markdown.value !== null) void buildOutline()
})
onBeforeUnmount(revokeImages)
</script>

<template>
  <div ref="root" class="docs" data-testid="docs-tab">
    <p v-if="loading" class="hint">Loading…</p>
    <p v-else-if="failed" class="hint" data-testid="docs-missing">This page is not in the app's docs. <button type="button" class="back" @click="open(ENTRY)">Docs home</button></p>
    <div v-else class="docs-body">
      <div class="docs-main">
        <section v-if="glanceItems.length" id="docs-at-a-glance" class="glance" aria-labelledby="glance-title" data-testid="docs-at-a-glance">
          <h2 id="glance-title" class="glance-title">At a glance</h2>
          <div class="glance-grid">
            <div v-for="item in glanceItems" :key="item.key" class="glance-item" :data-testid="`glance-${item.key}`">
              <span class="glance-label">{{ item.label }}</span>
              <span v-for="(line, index) in item.lines" :key="index" class="glance-value">{{ line }}</span>
            </div>
          </div>
        </section>
        <template v-if="markdown !== null">
          <div class="docs-head">
            <span class="mono">{{ path }}<template v-if="version"> · {{ version }}</template></span>
            <button type="button" class="back" data-testid="docs-source" @click="emit('navigate', 'source')">View in Source</button>
          </div>
          <button v-if="path !== ENTRY && !ENTRIES.includes(path)" type="button" class="back" data-testid="docs-home" @click="open(ENTRY)">← Docs home</button>
          <article ref="page" class="prose card" data-testid="docs-page" @click="onClick" v-html="html" />
        </template>
        <div v-else data-testid="docs-fallback">
          <p class="hint">This app ships no docs. Add <code>README.md</code> to its folder and deploy.</p>
          <p v-if="description">{{ description }}</p>
        </div>
      </div>
      <nav v-if="markdown !== null && outline.length > 1" class="outline" aria-label="On this page" data-testid="docs-outline">
        <h2>On this page</h2>
        <button v-for="item in outline" :key="item.id" type="button" :class="`lvl-${item.level}`" @click="jump(item.id)">{{ item.text }}</button>
      </nav>
    </div>
  </div>
</template>

<style scoped>
.hint { margin: 0 0 8px; color: var(--ink-soft); font-size: 13px; }
.mono { font-family: var(--mono); font-size: 12px; color: var(--ink-soft); }
.docs-head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 12px; }
.docs-body { display: flex; flex-wrap: wrap; gap: 32px; align-items: flex-start; }
.docs-main { flex: 1 1 520px; min-width: 0; display: flex; flex-direction: column; gap: 16px; }
.glance {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 22px 26px;
  border-radius: 12px;
  background: color-mix(in srgb, var(--signal) 8%, var(--paper));
  border: 1px solid color-mix(in srgb, var(--signal) 32%, var(--paper));
}
.glance-title {
  margin: 0;
  font-family: inherit;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--signal-ink);
}
.glance-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 16px; font-size: 15px; }
.glance-item { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.glance-label { color: var(--ink-soft); font-size: 13px; }
.glance-value { color: var(--ink); overflow-wrap: anywhere; }
.outline { flex: 0 0 220px; position: sticky; top: 12px; display: flex; flex-direction: column; gap: 4px; align-self: flex-start; }
.outline h2 { margin: 0 0 6px; font-size: 13px; font-weight: 600; color: var(--ink); }
.outline button { padding: 4px 0; border: 0; background: none; color: var(--ink-soft); font: inherit; font-size: 13px; text-align: left; cursor: pointer; }
.outline button:hover { color: var(--signal-ink); }
.outline .lvl-2 { padding-left: 12px; }
.outline .lvl-3 { padding-left: 24px; }
.back { border: 0; background: transparent; color: var(--signal-ink); font: inherit; font-size: 13px; text-decoration: underline; cursor: pointer; padding: 0; align-self: flex-start; }
.docs-head .back { margin: 0; }
.card { padding: 28px clamp(18px, 4vw, 40px); border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.prose { max-width: 75ch; line-height: 1.6; box-sizing: border-box; width: 100%; }
.prose :deep(h1), .prose :deep(h2), .prose :deep(h3) { scroll-margin-top: 12px; }
.prose :deep(img) { max-width: 100%; height: auto; }
.prose :deep(pre) { overflow: auto; padding: 10px 12px; border: 1px solid var(--line); border-radius: 8px; }
.prose :deep(code) { font-family: var(--mono); font-size: 13px; }
.prose :deep(.table-scroll) { overflow-x: auto; max-width: 100%; margin: 12px 0; }
.prose :deep(table) { border-collapse: collapse; font-size: 14px; }
.prose :deep(th), .prose :deep(td) { border: 1px solid var(--line); padding: 6px 10px; vertical-align: top; }
.prose :deep(th) { font-weight: 600; }
</style>
