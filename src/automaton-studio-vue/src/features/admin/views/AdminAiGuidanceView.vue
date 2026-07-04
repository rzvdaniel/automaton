<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import SelectButton from 'primevue/selectbutton'
import Dropdown from 'primevue/dropdown'
import type { AiGuidanceAdmin, UpsertAiGuidanceRequest } from '../types/admin.types'
import { adminAiGuidanceService } from '../services/adminAiGuidanceService'
import { renderMarkdown } from '@/shared/utils/markdown'

type GuidanceForm = UpsertAiGuidanceRequest

const SURFACE_OPTIONS = [
  { label: 'Studio', value: 'studio' },
  { label: 'Shared', value: 'shared' },
  { label: 'CLI', value: 'cli' },
  { label: 'MCP', value: 'mcp' },
]

const CATEGORY_OPTIONS = [
  { label: 'Editor', value: 'editor' },
  { label: 'Contract', value: 'contract' },
  { label: 'Backend', value: 'backend' },
  { label: 'Frontend', value: 'frontend' },
  { label: 'Media', value: 'media' },
  { label: 'Deploy', value: 'deploy' },
  { label: 'Starter', value: 'starter' },
  { label: 'Examples', value: 'examples' },
  { label: 'Protocol', value: 'protocol' },
  { label: 'Reference', value: 'reference' },
]

const EDITOR_TABS = [
  { label: 'Markdown', value: 'markdown' },
  { label: 'Preview', value: 'preview' },
]

const toast = useToast()
const confirm = useConfirm()

const documents = ref<AiGuidanceAdmin[]>([])
const selectedId = ref<string | null>(null)
const loading = ref(false)
const saving = ref(false)
const query = ref('')
const activeTab = ref<'markdown' | 'preview'>('markdown')
const advancedOpen = ref(false)

const form = ref<GuidanceForm>(createEmptyForm())

const selectedDocument = computed(() =>
  documents.value.find(item => item.id === selectedId.value) ?? null
)

const isNewDocument = computed(() => selectedId.value === null)

const filteredDocuments = computed(() => {
  const normalizedQuery = query.value.trim().toLowerCase()
  const base = [...documents.value].sort((a, b) => {
    if (a.surface !== b.surface) return a.surface.localeCompare(b.surface)
    if (a.category !== b.category) return a.category.localeCompare(b.category)
    return a.title.localeCompare(b.title)
  })

  if (!normalizedQuery) {
    return base
  }

  return base.filter(item =>
    item.title.toLowerCase().includes(normalizedQuery) ||
    item.key.toLowerCase().includes(normalizedQuery) ||
    item.relativePath.toLowerCase().includes(normalizedQuery) ||
    item.surface.toLowerCase().includes(normalizedQuery) ||
    item.category.toLowerCase().includes(normalizedQuery)
  )
})

const renderedPreview = computed(() => renderMarkdown(form.value.contentMarkdown || ''))

const generatedRelativePath = computed(() => {
  if (selectedDocument.value) {
    return selectedDocument.value.relativePath
  }

  const slug = form.value.key
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')

  return slug ? `llms/${slug}.md` : 'llms/new-guidance.md'
})

watch(
  () => form.value.key,
  () => {
    if (isNewDocument.value) {
      form.value.relativePath = generatedRelativePath.value
    }
  }
)

onMounted(loadDocuments)

async function loadDocuments() {
  loading.value = true
  try {
    documents.value = await adminAiGuidanceService.getAll()
    if (selectedId.value) {
      const refreshed = documents.value.find(item => item.id === selectedId.value)
      if (refreshed) {
        applyDocumentToForm(refreshed)
      }
    }
  } catch {
    toast.add({ severity: 'error', summary: 'Load Failed', detail: 'Failed to load AI guidance documents', life: 3000 })
  } finally {
    loading.value = false
  }
}

function selectDocument(document: AiGuidanceAdmin) {
  selectedId.value = document.id
  applyDocumentToForm(document)
  activeTab.value = 'markdown'
  advancedOpen.value = false
}

function startCreate() {
  selectedId.value = null
  form.value = createEmptyForm()
  form.value.relativePath = generatedRelativePath.value
  activeTab.value = 'markdown'
  advancedOpen.value = true
}

function cloneDocument() {
  if (!selectedDocument.value) {
    return
  }

  const source = selectedDocument.value
  selectedId.value = null
  form.value = {
    key: `${source.key}-copy`,
    relativePath: '',
    title: `${source.title} Copy`,
    surface: source.surface,
    category: source.category,
    description: source.description ?? '',
    tagsJson: source.tagsJson ?? '',
    contentMarkdown: source.contentMarkdown,
    isEnabled: false,
  }
  form.value.relativePath = generatedRelativePath.value
  activeTab.value = 'markdown'
  advancedOpen.value = true
}

async function saveDocument() {
  saving.value = true
  try {
    const payload: UpsertAiGuidanceRequest = {
      key: form.value.key.trim(),
      relativePath: generatedRelativePath.value.trim(),
      title: form.value.title.trim(),
      surface: form.value.surface.trim(),
      category: form.value.category.trim(),
      description: normalizeOptional(form.value.description),
      tagsJson: normalizeOptional(form.value.tagsJson),
      contentMarkdown: form.value.contentMarkdown,
      isEnabled: form.value.isEnabled,
    }

    const wasEditingExisting = !!selectedId.value
    const saved = selectedId.value
      ? await adminAiGuidanceService.update(selectedId.value, payload)
      : await adminAiGuidanceService.create(payload)

    selectedId.value = saved.id
    applyDocumentToForm(saved)
    await loadDocuments()

    toast.add({
      severity: 'success',
      summary: wasEditingExisting ? 'Saved' : 'Created',
      detail: 'AI guidance document saved',
      life: 3000,
    })
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Save Failed',
      detail: extractErrorMessage(error, 'Failed to save AI guidance document'),
      life: 4000,
    })
  } finally {
    saving.value = false
  }
}

function confirmToggleEnabled(document: AiGuidanceAdmin) {
  const enable = !document.isEnabled
  confirm.require({
    message: enable
      ? `Enable "${document.title}" for runtime use?`
      : `Disable "${document.title}" for runtime use?`,
    header: enable ? 'Enable Guidance' : 'Disable Guidance',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: enable ? 'Enable' : 'Disable',
    accept: async () => {
      try {
        const updated = enable
          ? await adminAiGuidanceService.enable(document.id)
          : await adminAiGuidanceService.disable(document.id)
        await applyMutationResult(updated)
        toast.add({
          severity: 'success',
          summary: enable ? 'Enabled' : 'Disabled',
          detail: `Document ${enable ? 'enabled' : 'disabled'}`,
          life: 3000,
        })
      } catch (error: unknown) {
        toast.add({
          severity: 'error',
          summary: enable ? 'Enable Failed' : 'Disable Failed',
          detail: extractErrorMessage(error, 'Failed to update document state'),
          life: 4000,
        })
      }
    },
  })
}

function confirmReset(document: AiGuidanceAdmin) {
  confirm.require({
    message: `Reset "${document.title}" back to its embedded default content?`,
    header: 'Reset Guidance',
    icon: 'pi pi-refresh',
    rejectLabel: 'Cancel',
    acceptLabel: 'Reset',
    accept: async () => {
      try {
        const updated = await adminAiGuidanceService.reset(document.id)
        await applyMutationResult(updated)
        toast.add({
          severity: 'success',
          summary: 'Reset',
          detail: 'Document restored from embedded default',
          life: 3000,
        })
      } catch (error: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Reset Failed',
          detail: extractErrorMessage(error, 'Failed to reset document'),
          life: 4000,
        })
      }
    },
  })
}

async function applyMutationResult(document: AiGuidanceAdmin) {
  selectedId.value = document.id
  applyDocumentToForm(document)
  await loadDocuments()
}

function applyDocumentToForm(document: AiGuidanceAdmin) {
  form.value = {
    key: document.key,
    relativePath: document.relativePath,
    title: document.title,
    surface: document.surface,
    category: document.category,
    description: document.description ?? '',
    tagsJson: document.tagsJson ?? '',
    contentMarkdown: document.contentMarkdown,
    isEnabled: document.isEnabled,
  }
}

function createEmptyForm(): GuidanceForm {
  return {
    key: '',
    relativePath: 'llms/new-guidance.md',
    title: '',
    surface: 'studio',
    category: 'editor',
    description: '',
    tagsJson: '',
    contentMarkdown: '',
    isEnabled: true,
  }
}

function normalizeOptional(value?: string) {
  return value && value.trim().length > 0 ? value.trim() : undefined
}

function extractErrorMessage(error: unknown, fallback: string) {
  const axiosError = error as { response?: { data?: { message?: string } } }
  return axiosError?.response?.data?.message || fallback
}
</script>

<template>
  <div class="admin-ai-guidance-view">
    <div class="page-header">
      <div class="flex align-items-center gap-2 mb-2">
        <i class="pi pi-file-edit text-color-secondary"></i>
        <span class="text-color-secondary text-sm">Administration</span>
      </div>
      <div class="flex align-items-center justify-content-between gap-3 flex-wrap">
        <div>
          <h1 class="text-2xl font-bold m-0">AI Guidance</h1>
          <p class="text-color-secondary mt-1 mb-0">
            Manage runtime prompt documents used by Studio and other Automaton AI surfaces.
          </p>
        </div>
        <Button label="New Document" icon="pi pi-plus" @click="startCreate" />
      </div>
    </div>

    <div class="workspace-shell">
      <Card class="documents-card">
        <template #title>Documents</template>
        <template #content>
          <div class="documents-toolbar">
            <InputText v-model="query" placeholder="Search documents..." class="w-full" />
          </div>

          <div class="documents-scroll">
            <DataTable
              :value="filteredDocuments"
              :loading="loading"
              data-key="id"
              selection-mode="single"
              :selection="selectedDocument"
              responsive-layout="scroll"
              @row-select="selectDocument($event.data)"
              @row-click="selectDocument($event.data)"
            >
              <template #empty>
                <div class="text-center py-5 text-color-secondary">
                  <i class="pi pi-file text-4xl mb-3 block"></i>
                  <p class="m-0">No AI guidance documents found.</p>
                </div>
              </template>

              <Column field="title" header="Document">
                <template #body="{ data }">
                  <div class="document-row" :class="{ 'document-row-active': selectedId === data.id }">
                    <div class="document-row-title">{{ data.title }}</div>
                    <div class="document-row-path">{{ data.relativePath }}</div>
                    <div class="document-row-tags">
                      <Tag :value="data.surface" severity="info" />
                      <Tag :value="data.category" severity="contrast" />
                      <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'secondary'" />
                    </div>
                  </div>
                </template>
              </Column>
            </DataTable>
          </div>
        </template>
      </Card>

      <Card class="editor-card">
        <template #content>
          <div class="editor-shell">
            <div class="editor-header">
              <div class="editor-header-text">
                <h2 class="editor-title">{{ isNewDocument ? 'New Guidance Document' : 'Edit Guidance Document' }}</h2>
                <div v-if="selectedDocument" class="editor-meta">
                  <span>Version {{ selectedDocument.version }}</span>
                  <span v-if="selectedDocument.updatedAt">Updated {{ new Date(selectedDocument.updatedAt).toLocaleString() }}</span>
                </div>
              </div>

              <div class="editor-actions">
                <Button
                  v-if="selectedDocument"
                  label="Clone"
                  icon="pi pi-copy"
                  severity="secondary"
                  outlined
                  @click="cloneDocument"
                />
                <Button
                  v-if="selectedDocument?.hasEmbeddedDefault"
                  label="Reset"
                  icon="pi pi-refresh"
                  severity="danger"
                  outlined
                  @click="confirmReset(selectedDocument)"
                />
                <Button
                  v-if="selectedDocument"
                  :label="selectedDocument.isEnabled ? 'Disable' : 'Enable'"
                  :icon="selectedDocument.isEnabled ? 'pi pi-eye-slash' : 'pi pi-eye'"
                  severity="secondary"
                  outlined
                  @click="confirmToggleEnabled(selectedDocument)"
                />
                <Button label="Save" icon="pi pi-save" :loading="saving" @click="saveDocument" />
              </div>
            </div>

            <div class="editor-scroll">
              <div class="primary-fields">
                <div class="field">
                  <label for="guidance-title">Title</label>
                  <InputText id="guidance-title" v-model="form.title" class="w-full" />
                </div>

                <div class="field">
                  <label for="guidance-key">Key</label>
                  <InputText id="guidance-key" v-model="form.key" class="w-full" />
                </div>

                <div class="field">
                  <label for="guidance-surface">Surface</label>
                  <Dropdown
                    id="guidance-surface"
                    v-model="form.surface"
                    :options="SURFACE_OPTIONS"
                    option-label="label"
                    option-value="value"
                    class="w-full"
                  />
                </div>
              </div>

              <div class="field field-block">
                <label for="guidance-description">Description</label>
                <InputText id="guidance-description" v-model="form.description" class="w-full" />
              </div>

              <details :open="advancedOpen" class="advanced-panel" @toggle="advancedOpen = ($event.target as HTMLDetailsElement).open">
                <summary class="advanced-summary">Metadata / Advanced</summary>
                <div class="advanced-grid">
                  <div class="field">
                    <label for="guidance-category">Category</label>
                    <Dropdown
                      id="guidance-category"
                      v-model="form.category"
                      :options="CATEGORY_OPTIONS"
                      option-label="label"
                      option-value="value"
                      class="w-full"
                    />
                  </div>

                  <div class="field">
                    <label for="guidance-path">Relative Path</label>
                    <InputText id="guidance-path" :model-value="generatedRelativePath" readonly class="w-full" />
                  </div>

                  <div class="field field-span-2">
                    <label for="guidance-tags">Tags</label>
                    <InputText id="guidance-tags" v-model="form.tagsJson" class="w-full" placeholder='["studio","editor"]' />
                  </div>
                </div>
              </details>

              <div class="content-section">
                <div class="content-section-header">
                  <div>
                    <h3 class="content-title">Content Markdown</h3>
                    <p class="content-subtitle">The guidance content is the primary artifact on this screen.</p>
                  </div>

                  <SelectButton
                    v-model="activeTab"
                    :options="EDITOR_TABS"
                    option-label="label"
                    option-value="value"
                    aria-label="Editor mode"
                  />
                </div>

                <div class="editor-pane">
                  <Textarea
                    v-if="activeTab === 'markdown'"
                    id="guidance-content"
                    v-model="form.contentMarkdown"
                    class="guidance-content"
                  />

                  <div v-else class="preview-pane">
                    <div v-if="form.contentMarkdown.trim()" class="markdown-preview" v-html="renderedPreview" />
                    <div v-else class="preview-empty">
                      <i class="pi pi-file-o"></i>
                      <span>Preview appears here once markdown content exists.</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </template>
      </Card>
    </div>
  </div>
</template>

<style scoped>
.admin-ai-guidance-view {
  min-height: calc(100vh - 10rem);
  display: flex;
  flex-direction: column;
}

.page-header {
  margin-bottom: 1rem;
  flex-shrink: 0;
}

.workspace-shell {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: minmax(18rem, 22rem) minmax(0, 1fr);
  gap: 1rem;
}

.documents-card,
.editor-card,
.documents-card :deep(.p-card),
.editor-card :deep(.p-card) {
  height: 100%;
}

.documents-card :deep(.p-card-body),
.editor-card :deep(.p-card-body) {
  height: 100%;
}

.documents-card :deep(.p-card-content),
.editor-card :deep(.p-card-content) {
  height: 100%;
  padding-top: 0;
}

.documents-toolbar {
  margin-bottom: 0.75rem;
}

.documents-scroll {
  height: calc(100% - 3.25rem);
  overflow: auto;
}

.document-row {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 0.15rem 0;
}

.document-row-title {
  font-weight: 700;
  color: #19325f;
}

.document-row-path {
  font-size: 0.9rem;
  color: var(--text-color-secondary);
}

.document-row-tags {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}

.editor-shell {
  height: 100%;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.editor-header {
  position: sticky;
  top: 0;
  z-index: 3;
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding-bottom: 0.9rem;
  margin-bottom: 1rem;
  background: var(--surface-card);
  border-bottom: 1px solid var(--surface-border);
}

.editor-header-text {
  min-width: 0;
}

.editor-title {
  margin: 0;
  font-size: 1.85rem;
  line-height: 1.1;
  color: #14315d;
}

.editor-meta {
  display: flex;
  gap: 0.9rem;
  flex-wrap: wrap;
  margin-top: 0.45rem;
  font-size: 0.9rem;
  color: var(--text-color-secondary);
}

.editor-actions {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
  justify-content: flex-end;
  flex-shrink: 0;
}

.editor-scroll {
  flex: 1;
  min-height: 0;
  overflow: auto;
  padding-right: 0.25rem;
}

.primary-fields {
  display: grid;
  grid-template-columns: 1.3fr 1fr 0.8fr;
  gap: 1rem;
  margin-bottom: 1rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.field-block {
  margin-bottom: 1rem;
}

.advanced-panel {
  margin-bottom: 1.2rem;
  border: 1px solid var(--surface-border);
  border-radius: 12px;
  background: color-mix(in srgb, var(--surface-card) 92%, #eef3fb 8%);
}

.advanced-summary {
  cursor: pointer;
  list-style: none;
  padding: 0.9rem 1rem;
  font-weight: 700;
  color: #36517e;
}

.advanced-summary::-webkit-details-marker {
  display: none;
}

.advanced-summary::before {
  content: '▸';
  display: inline-block;
  margin-right: 0.5rem;
  transition: transform 0.18s ease;
}

.advanced-panel[open] .advanced-summary::before {
  transform: rotate(90deg);
}

.advanced-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
  padding: 0 1rem 1rem;
}

.field-span-2 {
  grid-column: 1 / -1;
}

.content-section {
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.content-section-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.9rem;
}

.content-title {
  margin: 0;
  font-size: 1.1rem;
  color: #19325f;
}

.content-subtitle {
  margin: 0.2rem 0 0;
  color: var(--text-color-secondary);
  font-size: 0.92rem;
}

.editor-pane {
  min-height: 34rem;
  height: calc(100vh - 24rem);
  border: 1px solid var(--surface-border);
  border-radius: 14px;
  background: var(--surface-ground);
  overflow: hidden;
}

.guidance-content {
  width: 100%;
  height: 100%;
  border: 0;
  border-radius: 0;
  resize: none;
  font-family: var(--font-family-monospace, monospace);
  font-size: 0.95rem;
  line-height: 1.55;
}

.guidance-content:enabled {
  background: #fff;
}

.preview-pane {
  height: 100%;
  overflow: auto;
  padding: 1.25rem;
  background: #fff;
}

.markdown-preview :deep(h1),
.markdown-preview :deep(h2),
.markdown-preview :deep(h3) {
  color: #17325b;
}

.markdown-preview :deep(pre) {
  overflow: auto;
  padding: 1rem;
  border-radius: 10px;
  background: #f6f8fc;
}

.markdown-preview :deep(code) {
  font-family: var(--font-family-monospace, monospace);
}

.preview-empty {
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  color: var(--text-color-secondary);
}

@media (max-width: 1180px) {
  .workspace-shell {
    grid-template-columns: 1fr;
  }

  .documents-scroll {
    height: 18rem;
  }

  .primary-fields {
    grid-template-columns: 1fr;
  }

  .advanced-grid {
    grid-template-columns: 1fr;
  }

  .field-span-2 {
    grid-column: auto;
  }

  .editor-header,
  .content-section-header {
    flex-direction: column;
    align-items: stretch;
  }

  .editor-actions {
    justify-content: flex-start;
  }

  .editor-pane {
    height: 28rem;
  }
}
</style>
