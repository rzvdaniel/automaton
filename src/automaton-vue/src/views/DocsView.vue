<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { marked } from 'marked'
import AppFooter from '@/components/AppFooter.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'

import homeRaw from '@/content/docs/home.md?raw'
import stackSelectionRaw from '@/content/docs/stack-selection.md?raw'
import appEditorRaw from '@/content/docs/app-editor.md?raw'
import cliAiToolsRaw from '@/content/docs/cli-ai-tools.md?raw'
import templatesRaw from '@/content/docs/templates.md?raw'
import troubleshootingRaw from '@/content/docs/troubleshooting.md?raw'
import automatonCliRaw from '@/content/docs/automaton-cli.md?raw'

type DocItem = {
  slug: string
  label: string
  summary: string
  group: string
  content: string
}

const router = useRouter()
const route = useRoute()
const copyNotice = ref(false)

const docs: DocItem[] = [
  { slug: 'home', label: 'Home', summary: 'Start here', group: 'Overview', content: homeRaw },
  { slug: 'stack-selection', label: 'Choose a stack', summary: 'HTML, React, or Vue', group: 'Quick Starts', content: stackSelectionRaw },
  { slug: 'app-editor', label: 'App Editor', summary: 'Browser-first workflow', group: 'Workflows', content: appEditorRaw },
  { slug: 'cli-ai-tools', label: 'CLI + AI tools', summary: 'Cursor, Claude, Copilot', group: 'Workflows', content: cliAiToolsRaw },
  { slug: 'templates', label: 'Templates', summary: 'Start from examples', group: 'Workflows', content: templatesRaw },
  { slug: 'troubleshooting', label: 'Troubleshooting', summary: 'Common errors and fixes', group: 'Reference', content: troubleshootingRaw },
  { slug: 'automaton-cli', label: 'Automaton CLI', summary: 'All commands and options', group: 'Reference', content: automatonCliRaw },
]

const docsByGroup = computed(() => {
  const groups = new Map<string, DocItem[]>()
  for (const doc of docs) {
    const current = groups.get(doc.group) || []
    current.push(doc)
    groups.set(doc.group, current)
  }
  return Array.from(groups.entries()).map(([group, items]) => ({ group, items }))
})

const currentSlug = computed(() => String(route.params.slug || 'home'))

const currentDoc = computed(() => docs.find((doc) => doc.slug === currentSlug.value) || docs[0]!)

const currentHtml = computed(() => {
  const html = marked.parse(currentDoc.value.content) as string
  const template = document.createElement('template')
  template.innerHTML = html

  const preBlocks = template.content.querySelectorAll('pre')
  preBlocks.forEach((pre) => {
    const code = pre.querySelector('code')
    if (!code) return

    const wrapper = document.createElement('div')
    wrapper.className = 'docs-code-block'

    if (code.classList.contains('language-text')) {
      wrapper.classList.add('docs-code-block--wrap')
    }

    const actions = document.createElement('div')
    actions.className = 'docs-code-actions'

    const copyButton = document.createElement('button')
    copyButton.type = 'button'
    copyButton.className = 'docs-code-copy'
    copyButton.setAttribute('data-copy-code', 'true')
    copyButton.setAttribute('aria-label', 'Copy code block')
    copyButton.innerHTML = '<i class="pi pi-copy" aria-hidden="true"></i>'

    actions.appendChild(copyButton)
    pre.parentNode?.insertBefore(wrapper, pre)
    wrapper.appendChild(actions)
    wrapper.appendChild(pre)
  })

  return template.innerHTML
})

async function handleDocsClick(event: MouseEvent) {
  const target = event.target as HTMLElement | null
  const copyButton = target?.closest('[data-copy-code="true"]') as HTMLElement | null
  if (!copyButton) return

  const code = copyButton.closest('.docs-code-block')?.querySelector('pre code')
  const text = code?.textContent ?? ''
  if (!text) return

  try {
    await navigator.clipboard.writeText(text)
    copyNotice.value = true
    window.setTimeout(() => {
      copyNotice.value = false
    }, 1800)
  } catch {
    copyNotice.value = false
  }
}

function openDoc(slug: string) {
  void router.push(slug === 'home' ? '/docs' : `/docs/${slug}`)
}
</script>

<template>
  <div class="docs-page">
    <PublicSiteHeader />
    <main class="docs-main">
      <div class="shell docs-shell">
        <div class="docs-page-header">
          <div>
            <span class="docs-eyebrow">Public Docs</span>
            <h1>Documentation</h1>
            <p>Quick starts, workflows, prompts, and reference docs for building apps with Automaton Monster.</p>
          </div>
        </div>

        <div class="docs-layout">
          <aside class="docs-nav">
            <div class="docs-nav-card">
              <div v-for="section in docsByGroup" :key="section.group" class="docs-nav-section">
                <div class="docs-nav-group">{{ section.group }}</div>
                <button
                  v-for="doc in section.items"
                  :key="doc.slug"
                  :class="['docs-nav-item', currentDoc.slug === doc.slug && 'docs-nav-item--active']"
                  type="button"
                  @click="openDoc(doc.slug)"
                >
                  <span class="docs-nav-label">{{ doc.label }}</span>
                  <span class="docs-nav-summary">{{ doc.summary }}</span>
                </button>
              </div>
            </div>
          </aside>

          <section class="docs-content-wrap">
            <div class="docs-content-card">
              <div class="docs-actions">
                <button
                  v-if="currentDoc.slug !== 'home'"
                  class="docs-back"
                  type="button"
                  @click="openDoc('home')"
                >
                  <i class="pi pi-arrow-left" />
                  Back to docs home
                </button>
              </div>
              <article class="docs-content" v-html="currentHtml" @click="handleDocsClick" />
            </div>
          </section>
        </div>
      </div>
    </main>

    <div v-if="copyNotice" class="copy-notice" role="status" aria-live="polite">
      Code block copied to clipboard
    </div>

    <AppFooter />
  </div>
</template>

<style scoped>
.docs-page {
  min-height: 100vh;
  background:
    radial-gradient(circle at 24% 0%, rgba(80, 215, 255, 0.12), transparent 30rem),
    radial-gradient(circle at 86% 10%, rgba(69, 230, 138, 0.1), transparent 28rem),
    linear-gradient(180deg, #090b10 0%, #07080b 44%, #050608 100%);
  color: #f5f7fb;
}

.shell {
  width: min(1260px, calc(100% - 40px));
  margin: 0 auto;
}

.docs-main {
  padding: 36px 0 80px;
}

.docs-page-header {
  margin-bottom: 28px;
}

.docs-eyebrow {
  display: inline-flex;
  width: fit-content;
  max-width: 100%;
  gap: 8px;
  padding: 5px 10px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.82rem;
  font-weight: 700;
}

.docs-page-header h1 {
  width: max-content;
  max-width: 100%;
  margin: 18px 0 10px;
  font-size: clamp(3rem, 6.1vw, 6rem);
  line-height: 0.92;
  letter-spacing: -0.06em;
}

.docs-page-header p {
  max-width: 760px;
  margin: 0;
  color: #9ba3b4;
  font-size: 1rem;
  line-height: 1.6;
}

@media (max-width: 820px) {
  .docs-page-header h1 {
    width: 100%;
  }
}

.docs-layout {
  display: grid;
  grid-template-columns: 290px minmax(0, 1fr);
  gap: 18px;
  align-items: start;
}

.docs-nav {
  position: sticky;
  top: 150px;
}

.docs-nav-card,
.docs-content-card {
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.26);
}

.docs-nav-card {
  padding: 16px;
}

.docs-nav-section + .docs-nav-section {
  margin-top: 18px;
}

.docs-nav-group {
  margin-bottom: 10px;
  color: #9ba3b4;
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.docs-nav-item {
  display: grid;
  gap: 3px;
  width: 100%;
  margin-bottom: 8px;
  padding: 11px 12px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  background: transparent;
  color: #f5f7fb;
  text-align: left;
  cursor: pointer;
}

.docs-nav-item:hover {
  border-color: rgba(80, 215, 255, 0.3);
  background: rgba(255, 255, 255, 0.03);
}

.docs-nav-item--active {
  border-color: rgba(69, 230, 138, 0.4);
  background: rgba(69, 230, 138, 0.1);
}

.docs-nav-label {
  font-size: 0.95rem;
  font-weight: 700;
}

.docs-nav-summary {
  color: #9ba3b4;
  font-size: 0.82rem;
}

.docs-content-card {
  padding: 18px 22px 26px;
}

.docs-actions {
  min-height: 24px;
  margin-bottom: 8px;
}

.docs-back {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 0;
  border: 0;
  background: transparent;
  color: #50d7ff;
  font-size: 0.9rem;
  font-weight: 700;
  cursor: pointer;
}

.docs-content :deep(h1) {
  margin-top: 0;
  margin-bottom: 1rem;
  font-size: 2rem;
  line-height: 1.05;
}

.docs-content :deep(h2) {
  margin-top: 1.8rem;
  margin-bottom: 0.75rem;
  font-size: 1.35rem;
}

.docs-content :deep(h3) {
  margin-top: 1.2rem;
  margin-bottom: 0.5rem;
  font-size: 1.05rem;
}

.docs-content :deep(p),
.docs-content :deep(li) {
  color: #d4d9e3;
  line-height: 1.72;
}

.docs-content :deep(ul),
.docs-content :deep(ol) {
  padding-left: 1.25rem;
}

.docs-content :deep(a) {
  color: #8ab4ff;
}

.docs-content :deep(code) {
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
  background: rgba(80, 215, 255, 0.1);
  color: #bdefff;
}

.docs-content :deep(.docs-code-block) {
  position: relative;
  margin: 1rem 0;
  padding-right: 3rem;
}

.docs-content :deep(.docs-code-actions) {
  position: absolute;
  top: 0.5rem;
  right: 0;
  z-index: 1;
}

.docs-content :deep(.docs-code-copy) {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: 8px;
  background: rgba(22, 27, 34, 0.9);
  color: #e6edf3;
  cursor: pointer;
}

.docs-content :deep(pre) {
  overflow-x: auto;
  margin: 0;
  padding: 1rem;
  border: 1px solid #30363d;
  border-radius: 10px;
  background: #161b22;
  color: #e6edf3;
}

.docs-content :deep(pre code) {
  padding: 0;
  background: transparent;
  color: inherit;
}

.docs-content :deep(.docs-code-block--wrap pre) {
  overflow-x: visible;
}

.docs-content :deep(.docs-code-block--wrap pre code) {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  word-break: break-word;
}

.copy-notice {
  position: fixed;
  right: 22px;
  bottom: 22px;
  z-index: 200;
  padding: 10px 12px;
  border: 1px solid rgba(69, 230, 138, 0.28);
  border-radius: 8px;
  background: rgba(7, 8, 11, 0.96);
  color: #d9ffe8;
  font-size: 0.84rem;
  font-weight: 700;
  box-shadow: 0 16px 44px rgba(0, 0, 0, 0.28);
}

@media (max-width: 960px) {
  .docs-layout {
    grid-template-columns: 1fr;
  }

  .docs-nav {
    position: static;
  }
}

@media (max-width: 640px) {
  .shell {
    width: min(100% - 28px, 1180px);
  }

  .docs-main {
    padding-top: 24px;
  }

  .docs-content :deep(.docs-code-block) {
    padding-right: 0;
    padding-top: 2.5rem;
  }

  .docs-content :deep(.docs-code-actions) {
    top: 0;
    right: 0;
  }
}
</style>
