<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { marked } from 'marked'

import homeRaw from '../content/home.md?raw'
import quickstartHtmlRaw from '../content/quickstart-html.md?raw'
import quickstartReactRaw from '../content/quickstart-react.md?raw'
import quickstartVueRaw from '../content/quickstart-vue.md?raw'
import appEditorRaw from '../content/app-editor.md?raw'
import cliAiToolsRaw from '../content/cli-ai-tools.md?raw'
import mcpRaw from '../content/mcp.md?raw'
import templatesRaw from '../content/templates.md?raw'
import troubleshootingRaw from '../content/troubleshooting.md?raw'
import automatonCliRaw from '../content/automaton-cli.md?raw'

type DocItem = {
  slug: string
  label: string
  summary: string
  group: string
  content: string
}

const router = useRouter()
const route = useRoute()
const toast = useToast()

const docs: DocItem[] = [
  { slug: 'home', label: 'Home', summary: 'Start here', group: 'Overview', content: homeRaw },
  { slug: 'quickstart-html', label: 'HTML/CSS/JS', summary: 'Plain static apps', group: 'Quick Starts', content: quickstartHtmlRaw },
  { slug: 'quickstart-react', label: 'React', summary: 'React + dist output', group: 'Quick Starts', content: quickstartReactRaw },
  { slug: 'quickstart-vue', label: 'Vue', summary: 'Vue + dist output', group: 'Quick Starts', content: quickstartVueRaw },
  { slug: 'app-editor', label: 'App Editor', summary: 'Browser-first workflow', group: 'Workflows', content: appEditorRaw },
  { slug: 'cli-ai-tools', label: 'CLI + AI tools', summary: 'Cursor, Claude, Copilot', group: 'Workflows', content: cliAiToolsRaw },
  { slug: 'mcp', label: 'MCP Server', summary: 'Recommended when supported', group: 'Workflows', content: mcpRaw },
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

const currentDoc = computed(() => {
  return docs.find(doc => doc.slug === currentSlug.value) || docs[0]!
})

const currentHtml = computed(() => {
  const html = marked.parse(currentDoc.value.content) as string
  const template = document.createElement('template')
  template.innerHTML = html

  const preBlocks = template.content.querySelectorAll('pre')
  preBlocks.forEach((pre) => {
    const code = pre.querySelector('code')
    if (!code) {
      return
    }

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
  if (!copyButton) {
    return
  }

  const code = copyButton.closest('.docs-code-block')?.querySelector('pre code')
  const text = code?.textContent ?? ''
  if (!text) {
    return
  }

  try {
    await navigator.clipboard.writeText(text)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'Code block copied to clipboard',
      life: 2000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Copy failed',
      detail: 'Could not copy the code block',
      life: 3000,
    })
  }
}

function openDoc(slug: string) {
  router.push(slug === 'home' ? '/docs' : `/docs/${slug}`)
}
</script>

<template>
  <div class="docs-center-view">
    <div class="docs-center-header mb-4">
      <div>
        <h1 class="text-3xl font-bold m-0">Documentation</h1>
        <p class="text-color-secondary mt-2 mb-0">
          Quick starts, workflows, prompts, and structure guidance for building apps with Automaton.
        </p>
      </div>
    </div>

    <div class="docs-center-layout">
      <aside class="docs-nav">
        <Card>
          <template #content>
            <div class="flex flex-column gap-4">
              <div v-for="section in docsByGroup" :key="section.group">
                <div class="docs-nav-group mb-2">{{ section.group }}</div>
                <div class="flex flex-column gap-2">
                  <button
                    v-for="doc in section.items"
                    :key="doc.slug"
                    :class="['docs-nav-item', currentDoc.slug === doc.slug && 'docs-nav-item--active']"
                    @click="openDoc(doc.slug)"
                  >
                    <span class="font-medium">{{ doc.label }}</span>
                    <span class="docs-nav-summary">{{ doc.summary }}</span>
                  </button>
                </div>
              </div>
            </div>
          </template>
        </Card>
      </aside>

      <section class="docs-content">
        <Card>
          <template #content>
            <div class="docs-actions mb-3">
              <Button
                v-if="currentDoc.slug !== 'home'"
                label="Back to docs home"
                icon="pi pi-arrow-left"
                text
                size="small"
                @click="openDoc('home')"
              />
            </div>
            <article class="docs-markdown" v-html="currentHtml" @click="handleDocsClick" />
          </template>
        </Card>
      </section>
    </div>
  </div>
</template>

<style scoped>
.docs-center-view {
  max-width: 1400px;
  margin: 0 auto;
}

.docs-center-layout {
  display: grid;
  grid-template-columns: 280px minmax(0, 1fr);
  gap: 1.5rem;
  align-items: start;
}

.docs-nav {
  position: sticky;
  top: 1rem;
}

.docs-nav-group {
  font-size: 0.8rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--p-text-muted-color);
}

.docs-nav-item {
  display: flex;
  flex-direction: column;
  width: 100%;
  text-align: left;
  border: 1px solid var(--p-surface-border);
  border-radius: 10px;
  background: transparent;
  color: var(--p-text-color);
  padding: 0.75rem 0.9rem;
  cursor: pointer;
  transition: border-color 0.15s ease, background-color 0.15s ease, color 0.15s ease;
}

.docs-nav-item:hover {
  border-color: var(--p-primary-400);
}

.docs-nav-item--active {
  border-color: var(--p-primary-500);
  background: color-mix(in srgb, var(--p-primary-500) 10%, transparent);
}

.docs-nav-summary {
  margin-top: 0.25rem;
  font-size: 0.85rem;
  color: var(--p-text-muted-color);
}

.docs-content :deep(h1) {
  font-size: 2rem;
  margin-top: 0;
  margin-bottom: 1rem;
}

.docs-content :deep(h2) {
  font-size: 1.3rem;
  margin-top: 1.75rem;
  margin-bottom: 0.75rem;
}

.docs-content :deep(h3) {
  font-size: 1.05rem;
  margin-top: 1.25rem;
  margin-bottom: 0.5rem;
}

.docs-content :deep(p),
.docs-content :deep(li) {
  line-height: 1.7;
}

.docs-content :deep(ul),
.docs-content :deep(ol) {
  padding-left: 1.25rem;
}

.docs-content :deep(code) {
  background: color-mix(in srgb, var(--p-primary-500) 10%, var(--p-surface-ground));
  color: var(--p-primary-400);
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
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
  border: 1px solid #30363d;
  border-radius: 8px;
  background: rgba(22, 27, 34, 0.9);
  color: #e6edf3;
  cursor: pointer;
  transition: border-color 0.15s ease, background-color 0.15s ease;
}

.docs-content :deep(.docs-code-copy:hover) {
  border-color: #58a6ff;
  background: rgba(30, 41, 59, 0.95);
}

.docs-content :deep(pre) {
  background: #161b22;
  color: #e6edf3;
  border: 1px solid #30363d;
  border-radius: 10px;
  padding: 1rem;
  overflow-x: auto;
}

.docs-content :deep(.docs-code-block pre) {
  margin: 0;
  width: 100%;
  min-width: 0;
}

.docs-content :deep(pre code) {
  background: transparent;
  color: inherit;
  padding: 0;
}

.docs-content :deep(.docs-code-block--wrap pre) {
  overflow-x: visible;
}

.docs-content :deep(.docs-code-block--wrap pre code) {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  word-break: break-word;
}

@media (max-width: 640px) {
  .docs-content :deep(.docs-code-block) {
    padding-right: 0;
    padding-top: 2.5rem;
  }

  .docs-content :deep(.docs-code-actions) {
    top: 0;
    right: 0;
  }
}

@media (max-width: 960px) {
  .docs-center-layout {
    grid-template-columns: 1fr;
  }

  .docs-nav {
    position: static;
  }
}
</style>