<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Drawer from 'primevue/drawer'
import { marked } from 'marked'
import { useDocumentationDrawer } from '@/shared/documentation/services/documentationDrawer'
import {
  documentationService,
  type AdminDocumentationTopic,
  type AdminDocumentationTopicListItem,
} from '@/shared/documentation/services/documentation.service'

interface DocumentationTopicGroup {
  key: string
  category: string
  items: AdminDocumentationTopicListItem[]
}

const { state, closeDocumentation, setDocumentationTopic } = useDocumentationDrawer()

const topics = ref<AdminDocumentationTopicListItem[]>([])
const topicsLoading = ref(false)
const loading = ref(false)
const errorMessage = ref('')
const topic = ref<AdminDocumentationTopic | null>(null)

const drawerVisible = computed({
  get: () => state.visible,
  set: (value: boolean) => {
    if (!value) {
      closeDocumentation()
    }
  },
})

const drawerTitle = computed(() => state.fallbackTitle || 'Documentation')
const renderedMarkdown = computed(() => (topic.value ? marked.parse(topic.value.contentMarkdown) : ''))
const groupedTopics = computed<DocumentationTopicGroup[]>(() => {
  const groups = new Map<string, DocumentationTopicGroup>()

  for (const item of [...topics.value].sort((a, b) => a.sortOrder - b.sortOrder || a.title.localeCompare(b.title))) {
    const existing = groups.get(item.category)
    if (existing) {
      existing.items.push(item)
      continue
    }

    groups.set(item.category, {
      key: item.category,
      category: item.category,
      items: [item],
    })
  }

  return Array.from(groups.values())
})

const selectedTopicKey = computed(() => state.topicKey)

async function loadTopics() {
  topicsLoading.value = true

  try {
    topics.value = await documentationService.listAdminTopics()

    const firstTopic = topics.value[0]
    if (firstTopic && (!state.topicKey || !topics.value.some(item => item.key === state.topicKey))) {
      setDocumentationTopic(firstTopic.key)
    }
  } catch {
    topics.value = []
    errorMessage.value = 'Failed to load documentation.'
  } finally {
    topicsLoading.value = false
  }
}

async function loadCurrentTopic(topicKey: string) {
  loading.value = true
  errorMessage.value = ''

  try {
    topic.value = await documentationService.getAdminTopic(topicKey)
  } catch {
    topic.value = null
    errorMessage.value = 'Failed to load documentation.'
  } finally {
    loading.value = false
  }
}

function selectTopic(topicKey: string) {
  setDocumentationTopic(topicKey)
}

watch(
  () => state.visible,
  async (visible) => {
    if (!visible) {
      return
    }

    if (topics.value.length === 0) {
      await loadTopics()
    }
  },
  { immediate: true },
)

watch(
  () => [state.visible, state.topicKey] as const,
  async ([visible, topicKey]) => {
    if (!visible || !topicKey) {
      return
    }

    await loadCurrentTopic(topicKey)
  },
  { immediate: true },
)

watch(
  () => state.visible,
  visible => {
    if (!visible) {
      loading.value = false
      topicsLoading.value = false
      errorMessage.value = ''
    }
  },
)
</script>

<template>
  <Drawer
    v-model:visible="drawerVisible"
    position="right"
    :header="drawerTitle"
    class="documentation-drawer"
    :style="{ width: '56rem', maxWidth: '100vw' }"
  >
    <div class="documentation-shell">
      <aside class="documentation-nav">
        <div class="documentation-nav-header">Documentation</div>

        <div v-if="topicsLoading" class="documentation-nav-state">
          <i class="pi pi-spin pi-spinner"></i>
          <span>Loading topics...</span>
        </div>

        <div v-else class="documentation-nav-groups">
          <section v-for="group in groupedTopics" :key="group.key" class="documentation-nav-group">
            <div class="documentation-nav-group-title">{{ group.category }}</div>

            <button
              v-for="item in group.items"
              :key="item.key"
              type="button"
              class="documentation-nav-item"
              :class="{ 'documentation-nav-item-active': selectedTopicKey === item.key }"
              @click="selectTopic(item.key)"
            >
              {{ item.title }}
            </button>
          </section>
        </div>
      </aside>

      <section class="documentation-body">
        <div v-if="loading" class="documentation-state">
          <i class="pi pi-spin pi-spinner"></i>
          <span>Loading documentation...</span>
        </div>

        <div v-else-if="errorMessage" class="documentation-state documentation-state-error">
          <i class="pi pi-exclamation-circle"></i>
          <span>{{ errorMessage }}</span>
        </div>

        <article v-else-if="topic" class="documentation-content-wrap">
          <div class="documentation-topic-meta">
            <div class="documentation-topic-category">{{ topic.category }}</div>
            <h1 class="documentation-topic-title">{{ topic.title }}</h1>
          </div>

          <div class="documentation-content" v-html="renderedMarkdown" />
        </article>
      </section>
    </div>
  </Drawer>
</template>

<style scoped>
.documentation-shell {
  display: grid;
  grid-template-columns: 16rem minmax(0, 1fr);
  min-height: 32rem;
  height: 100%;
  margin: -0.5rem -1.5rem -1rem;
}

.documentation-nav {
  border-right: 1px solid var(--surface-border);
  background: #fbfcfe;
  padding: 1rem 0.85rem 1rem 1rem;
  overflow-y: auto;
}

.documentation-nav-header {
  margin-bottom: 1rem;
  font-size: 0.82rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #5f7598;
}

.documentation-nav-state {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  color: var(--text-color-secondary);
}

.documentation-nav-groups {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.documentation-nav-group {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.documentation-nav-group-title {
  padding: 0 0.35rem;
  font-size: 0.95rem;
  font-weight: 700;
  color: #17325b;
}

.documentation-nav-item {
  width: 100%;
  padding: 0.55rem 0.65rem;
  border: 1px solid transparent;
  border-radius: 10px;
  background: transparent;
  color: #35527f;
  text-align: left;
  cursor: pointer;
  transition: background 0.18s ease, border-color 0.18s ease;
}

.documentation-nav-item:hover {
  background: #f2f7fd;
  border-color: #dbe7f6;
}

.documentation-nav-item-active {
  background: #eef5ff;
  border-color: #cfe0f5;
  color: #17325b;
  font-weight: 700;
}

.documentation-body {
  min-width: 0;
  padding: 1.25rem 1.5rem 1.5rem;
  overflow-y: auto;
}

.documentation-content-wrap {
  min-width: 0;
}

.documentation-topic-meta {
  margin-bottom: 1.25rem;
}

.documentation-topic-category {
  margin-bottom: 0.4rem;
  font-size: 0.82rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #5f7598;
}

.documentation-topic-title {
  margin: 0;
  font-size: 2rem;
  color: #14315d;
}

.documentation-state {
  min-height: 12rem;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  color: var(--text-color-secondary);
}

.documentation-state-error {
  color: #b42318;
}

.documentation-content {
  color: var(--text-color);
  line-height: 1.7;
}

.documentation-content :deep(h2),
.documentation-content :deep(h3) {
  color: #17325b;
  margin-top: 1.75rem;
}

.documentation-content :deep(p),
.documentation-content :deep(li) {
  line-height: 1.7;
}

.documentation-content :deep(pre) {
  overflow: auto;
  padding: 1rem;
  border-radius: 12px;
  background: #f6f8fc;
}

.documentation-content :deep(code) {
  font-family: var(--font-family-monospace, monospace);
}

.documentation-content :deep(table) {
  width: 100%;
  border-collapse: collapse;
  margin: 1rem 0;
}

.documentation-content :deep(th),
.documentation-content :deep(td) {
  padding: 0.65rem 0.75rem;
  border: 1px solid #dbe4f0;
  text-align: left;
  vertical-align: top;
}

.documentation-content :deep(th) {
  background: #f5f8fc;
  font-weight: 700;
}

.documentation-content :deep(blockquote) {
  margin: 1rem 0;
  padding: 0.85rem 1rem;
  border-left: 4px solid #c9d8ec;
  background: #f8fbff;
  color: #35527f;
}

@media (max-width: 960px) {
  .documentation-shell {
    grid-template-columns: 1fr;
    min-height: 0;
  }

  .documentation-nav {
    border-right: 0;
    border-bottom: 1px solid var(--surface-border);
  }

  .documentation-body {
    padding: 1rem;
  }
}
</style>
