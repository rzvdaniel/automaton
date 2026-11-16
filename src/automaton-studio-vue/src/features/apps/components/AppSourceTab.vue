<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Select from 'primevue/select'
import Splitter from 'primevue/splitter'
import SplitterPanel from 'primevue/splitterpanel'
import { useToast } from 'primevue/usetoast'
import CodeEditor from './CodeEditor.vue'
import FileTree from './FileTree.vue'
import { appsService } from '../services/apps.service'
import { getLanguageFromExtension, type FileNode } from '../types/apps.types'

const props = defineProps<{ appId: string; definition?: unknown }>()

const DEFINITION = '\0definition'
const TEXT = ['html', 'htm', 'css', 'js', 'ts', 'json', 'md', 'markdown', 'xml', 'svg', 'py', 'txt', 'bat', 'vue', 'scss', 'sass', 'less', 'yml', 'yaml']

const toast = useToast()
const tree = ref<FileNode[]>([])
const active = ref<string>(DEFINITION)
const content = ref('')

const definitionJson = computed(() => JSON.stringify(props.definition ?? {}, null, 2))
const isDefinition = computed(() => active.value === DEFINITION)
const shownPath = computed(() => (isDefinition.value ? 'automaton.json' : active.value))
const shownContent = computed(() => (isDefinition.value ? definitionJson.value : content.value))

function flatten(nodes: FileNode[]): string[] {
  return nodes.flatMap((node) => (node.type === 'file' ? [node.path] : flatten(node.children ?? [])))
}
const options = computed(() => [
  { label: '★ Definition (automaton.json)', value: DEFINITION },
  ...flatten(tree.value).map((path) => ({ label: path, value: path })),
])

async function open(path: string) {
  active.value = path
  if (path === DEFINITION) return
  content.value = ''
  if (!TEXT.includes(path.split('.').pop()?.toLowerCase() ?? '')) {
    content.value = 'Binary or non-text file preview is not available.'
    return
  }
  try { content.value = await appsService.readSourceFile(props.appId, path) } catch { content.value = 'Failed to load file.' }
}

onMounted(async () => {
  try { tree.value = await appsService.getSourceFileTree(props.appId) } catch {
    toast.add({ severity: 'error', summary: 'Failed to load the stored files', life: 3000 })
  }
})
</script>

<template>
  <div class="source" data-testid="source-tab">
    <p class="hint">Read-only view of what is deployed, including the definition. Change it with your CLI.</p>
    <Select
      class="picker"
      :model-value="active"
      :options="options"
      option-label="label"
      option-value="value"
      aria-label="File"
      data-testid="source-picker"
      @update:model-value="open"
    />
    <Splitter class="viewer">
      <SplitterPanel class="side" :size="28" :min-size="20">
        <button type="button" class="pinned" :class="{ on: isDefinition }" data-testid="source-definition" @click="open(DEFINITION)">★ Definition (automaton.json)</button>
        <FileTree title="Files" :nodes="tree" :active-path="isDefinition ? null : active" empty-message="No files available for this app." @open-file="open" />
      </SplitterPanel>
      <SplitterPanel :size="72" :min-size="40">
        <div class="code-path">{{ shownPath }}</div>
        <div class="code"><CodeEditor :file-path="shownPath" :content="shownContent" :language="getLanguageFromExtension(shownPath)" :read-only="true" /></div>
      </SplitterPanel>
    </Splitter>
  </div>
</template>

<style scoped>
.source { display: flex; flex-direction: column; gap: 12px; }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.picker { display: none; width: 100%; }
.viewer { height: 65vh; border: 1px solid var(--line); border-radius: 10px; overflow: hidden; }
.side { display: flex; flex-direction: column; overflow: auto; }
.pinned { padding: 10px 12px; border: 0; border-bottom: 1px solid var(--line); background: transparent; color: inherit; font: inherit; font-size: 13px; font-weight: 600; text-align: left; cursor: pointer; }
.pinned.on { background: var(--row-line); }
.code-path { padding: 8px 12px; border-bottom: 1px solid var(--line); font-family: var(--mono); font-size: 13px; }
.code { height: calc(100% - 38px); min-height: 20rem; }
@media (max-width: 767px) {
  .picker { display: inline-flex; }
  .viewer :deep(.p-splitter-gutter), .viewer .side { display: none; }
  .viewer { height: 60vh; }
}
</style>
