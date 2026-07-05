<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Drawer from 'primevue/drawer'
import { marked } from 'marked'
import { useDocumentationDrawer } from '@/shared/documentation/services/documentationDrawer'
import { documentationService, type AdminDocumentationTopic } from '@/shared/documentation/services/documentation.service'

const { state, closeDocumentation } = useDocumentationDrawer()

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

const drawerTitle = computed(() => topic.value?.title || state.fallbackTitle || 'Help')
const renderedMarkdown = computed(() => (topic.value ? marked.parse(topic.value.contentMarkdown) : ''))

watch(
  () => [state.visible, state.topicKey] as const,
  async ([visible, topicKey]) => {
    if (!visible || !topicKey) {
      return
    }

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
  },
  { immediate: true },
)

watch(
  () => state.visible,
  (visible) => {
    if (!visible) {
      loading.value = false
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
      <div v-if="loading" class="documentation-state">
        <i class="pi pi-spin pi-spinner"></i>
        <span>Loading documentation...</span>
      </div>

      <div v-else-if="errorMessage" class="documentation-state documentation-state-error">
        <i class="pi pi-exclamation-circle"></i>
        <span>{{ errorMessage }}</span>
      </div>

      <article
        v-else-if="topic"
        class="documentation-content"
        v-html="renderedMarkdown"
      />
    </div>
  </Drawer>
</template>

<style scoped>
.documentation-shell {
  display: flex;
  flex-direction: column;
  min-height: 16rem;
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

.documentation-content :deep(h1),
.documentation-content :deep(h2),
.documentation-content :deep(h3) {
  color: #17325b;
  margin-top: 1.75rem;
}

.documentation-content :deep(h1:first-child) {
  margin-top: 0;
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
</style>
