<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import SelectButton from 'primevue/selectbutton'
import Dropdown from 'primevue/dropdown'
import Drawer from 'primevue/drawer'
import type { AiGuidanceAdmin, UpsertAiGuidanceRequest } from '../types/admin.types'
import { adminAiGuidanceService } from '../services/adminAiGuidanceService'
import { renderMarkdown } from '@/shared/utils/markdown'

type GuidanceForm = UpsertAiGuidanceRequest

interface GuidanceGroup {
  surface: string
  label: string
  items: AiGuidanceAdmin[]
}

const SURFACE_OPTIONS = [
  { label: 'Studio', value: 'studio' },
  { label: 'Shared', value: 'shared' },
  { label: 'CLI', value: 'cli' },
  { label: 'MCP', value: 'mcp' },
]

const EDITOR_TABS = [
  { label: 'Markdown', value: 'markdown' },
  { label: 'Preview', value: 'preview' },
]

const SURFACE_LABELS: Record<string, string> = {
  studio: 'Studio',
  shared: 'Shared',
  cli: 'CLI',
  mcp: 'MCP',
}

const toast = useToast()
const confirm = useConfirm()

const documents = ref<AiGuidanceAdmin[]>([])
const selectedId = ref<string | null>(null)
const loading = ref(false)
const saving = ref(false)
const query = ref('')
const activeTab = ref<'markdown' | 'preview'>('markdown')
const propertiesOpen = ref(false)
const editingTitle = ref(false)

const form = ref<GuidanceForm>(createEmptyForm())

const selectedDocument = computed(() =>
  documents.value.find(item => item.id === selectedId.value) ?? null
)

const isNewDocument = computed(() => selectedId.value === null)

const filteredDocuments = computed(() => {
  const normalizedQuery = query.value.trim().toLowerCase()
  const base = [...documents.value].sort((a, b) => {
    if (a.surface !== b.surface) return a.surface.localeCompare(b.surface)
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
    (item.tagsJson ?? '').toLowerCase().includes(normalizedQuery)
  )
})

const groupedDocuments = computed<GuidanceGroup[]>(() => {
  const groups = new Map<string, AiGuidanceAdmin[]>()

  for (const document of filteredDocuments.value) {
    const existing = groups.get(document.surface) ?? []
    existing.push(document)
    groups.set(document.surface, existing)
  }

  return Array.from(groups.entries()).map(([surface, items]) => ({
    surface,
    label: SURFACE_LABELS[surface] ?? surface,
    items,
  }))
})

const documentTitle = computed(() => form.value.title.trim() || 'Untitled guidance document')
const renderedPreview = computed(() => renderMarkdown(form.value.contentMarkdown || ''))

const hasUnsavedChanges = computed(() => {
  if (isNewDocument.value) {
    return !!(form.value.title.trim() || form.value.contentMarkdown.trim())
  }

  if (!selectedDocument.value) {
    return false
  }

  return (
    form.value.key !== selectedDocument.value.key ||
    form.value.title !== selectedDocument.value.title ||
    form.value.surface !== selectedDocument.value.surface ||
    (form.value.description ?? '') !== (selectedDocument.value.description ?? '') ||
    (form.value.tagsJson ?? '') !== (selectedDocument.value.tagsJson ?? '') ||
    form.value.contentMarkdown !== selectedDocument.value.contentMarkdown ||
    form.value.isEnabled !== selectedDocument.value.isEnabled
  )
})

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
  propertiesOpen.value = false
  editingTitle.value = false
}

function startCreate() {
  selectedId.value = null
  form.value = createEmptyForm()
  form.value.relativePath = generatedRelativePath.value
  activeTab.value = 'markdown'
  propertiesOpen.value = true
  editingTitle.value = true
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
    description: source.description ?? '',
    tagsJson: source.tagsJson ?? '',
    contentMarkdown: source.contentMarkdown,
    isEnabled: false,
  }
  form.value.relativePath = generatedRelativePath.value
  activeTab.value = 'markdown'
  propertiesOpen.value = true
  editingTitle.value = true
}

async function saveDocument() {
  saving.value = true
  try {
    const payload: UpsertAiGuidanceRequest = {
      key: form.value.key.trim(),
      relativePath: generatedRelativePath.value.trim(),
      title: form.value.title.trim(),
      surface: form.value.surface.trim(),
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
      <div class="page-header-main">
        <div>
          <h1 class="page-title">AI Guidance</h1>
          <p class="page-subtitle">
            Manage runtime prompt documents used by Studio and other Automaton AI surfaces.
          </p>
        </div>
        <Button label="New Document" icon="pi pi-plus" @click="startCreate" />
      </div>
    </div>

    <div class="workspace-shell">
      <Card class="documents-card">
        <template #content>
          <div class="documents-shell">
            <div class="documents-header">
              <div>
                <h2 class="panel-title">Documents</h2>
                <p class="panel-subtitle">Browse guidance like a prompt library.</p>
              </div>
              <InputText v-model="query" placeholder="Search documents..." class="documents-search" />
            </div>

            <div class="documents-scroll">
              <div v-if="loading" class="documents-empty">
                <i class="pi pi-spin pi-spinner"></i>
                <span>Loading AI guidance documents...</span>
              </div>

              <div v-else-if="groupedDocuments.length === 0" class="documents-empty">
                <i class="pi pi-file"></i>
                <span>No AI guidance documents found.</span>
              </div>

              <div v-else class="document-groups">
                <section v-for="group in groupedDocuments" :key="group.surface" class="document-group">
                  <div class="document-group-label">{{ group.label }} <span class="document-group-count">({{ group.items.length }})</span></div>

                  <button
                    v-for="document in group.items"
                    :key="document.id"
                    type="button"
                    class="document-item"
                    :class="{ 'document-item-active': selectedId === document.id }"
                    @click="selectDocument(document)"
                  >
                    <span class="document-item-icon">📄</span>
                    <span class="document-item-title">{{ document.title }}</span>
                    <span
                      class="document-item-state-inline"
                      :class="{ 'document-item-state-inline-disabled': !document.isEnabled }"
                    >
                      {{ document.isEnabled ? '✓' : '○' }}
                    </span>
                  </button>
                </section>
              </div>
            </div>
          </div>
        </template>
      </Card>

      <Card class="editor-card">
        <template #content>
          <div class="editor-shell">
            <div class="editor-header">
              <div class="editor-header-text">
                <div class="editor-header-title-row">
                  <button
                    v-if="!editingTitle"
                    type="button"
                    class="editor-title-display"
                    @click="editingTitle = true"
                  >
                    {{ documentTitle }}
                  </button>
                  <InputText
                    v-else
                    id="guidance-title"
                    v-model="form.title"
                    class="editor-title-input"
                    placeholder="Untitled guidance document"
                    @blur="editingTitle = false"
                    @keyup.enter="editingTitle = false"
                    autofocus
                  />
                </div>
                <div class="editor-meta">
                  <span>{{ form.key || 'new-guidance' }}</span>
                  <span>Version {{ selectedDocument?.version ?? 1 }}</span>
                  <span v-if="selectedDocument?.updatedAt">Updated {{ new Date(selectedDocument.updatedAt).toLocaleString() }}</span>
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
                <div class="editor-primary-action">
                  <Button label="Save" icon="pi pi-save" :loading="saving" @click="saveDocument" />
                </div>
              </div>
            </div>

            <div class="editor-body">
              <section class="content-section">
                <div class="content-section-header">
                  <div class="content-toolbar">
                    <SelectButton
                      v-model="activeTab"
                      :options="EDITOR_TABS"
                      option-label="label"
                      option-value="value"
                      aria-label="Editor mode"
                    />
                    <Button
                      label="Properties"
                      icon="pi pi-cog"
                      severity="secondary"
                      outlined
                      @click="propertiesOpen = true"
                    />
                  </div>
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
              </section>
            </div>
          </div>
        </template>
      </Card>
    </div>

    <Drawer v-model:visible="propertiesOpen" position="right" header="Properties" class="guidance-properties-drawer">
      <div class="properties-shell">
        <div class="field">
          <label class="property-section-label">General</label>
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

        <div class="field">
          <label for="guidance-tags">Tags</label>
          <InputText id="guidance-tags" v-model="form.tagsJson" class="w-full" placeholder='["studio","editor"]' />
        </div>

        <div class="field">
          <label for="guidance-description">Description</label>
          <InputText
            id="guidance-description"
            v-model="form.description"
            class="w-full"
            placeholder="Add a short description"
          />
        </div>

        <div class="field">
          <label class="property-section-label">Storage</label>
        </div>

        <div class="field">
          <label for="guidance-path">Relative Path</label>
          <InputText id="guidance-path" :model-value="generatedRelativePath" readonly class="w-full" />
        </div>

        <div class="field">
          <label class="property-section-label">Runtime</label>
        </div>

        <div class="property-facts">
          <div class="property-fact">
            <span class="property-fact-label">Enabled</span>
            <span class="property-fact-value">{{ form.isEnabled ? 'Yes' : 'No' }}</span>
          </div>
        </div>

        <div class="field">
          <label class="property-section-label">History</label>
        </div>

        <div class="property-facts property-facts-tight">
          <div class="property-fact">
            <span class="property-fact-label">Version</span>
            <span class="property-fact-value">{{ selectedDocument?.version ?? 1 }}</span>
          </div>
          <div class="property-fact">
            <span class="property-fact-label">Created</span>
            <span class="property-fact-value">{{ selectedDocument ? new Date(selectedDocument.createdAt).toLocaleString() : 'Not saved yet' }}</span>
          </div>
          <div class="property-fact">
            <span class="property-fact-label">Updated</span>
            <span class="property-fact-value">{{ selectedDocument?.updatedAt ? new Date(selectedDocument.updatedAt).toLocaleString() : 'Not saved yet' }}</span>
          </div>
        </div>
      </div>
    </Drawer>
  </div>
</template>

<style scoped>
.admin-ai-guidance-view {
  flex: 1;
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.page-header {
  flex-shrink: 0;
  padding-bottom: 1rem;
}

.page-header-main {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.page-title {
  margin: 0;
  font-size: 2rem;
  color: #14315d;
}

.page-subtitle {
  margin: 0.35rem 0 0;
  color: var(--text-color-secondary);
}

.workspace-shell {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: minmax(18rem, 21rem) minmax(0, 1fr);
  gap: 1rem;
  overflow: hidden;
}

.documents-card,
.editor-card,
.documents-card :deep(.p-card),
.editor-card :deep(.p-card) {
  height: 100%;
  min-height: 0;
}

.documents-card :deep(.p-card-body),
.editor-card :deep(.p-card-body),
.documents-card :deep(.p-card-content),
.editor-card :deep(.p-card-content) {
  height: 100%;
  min-height: 0;
}

.documents-card :deep(.p-card-content),
.editor-card :deep(.p-card-content) {
  padding-top: 0;
}

.documents-shell,
.editor-shell {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.documents-header {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--surface-border);
  flex-shrink: 0;
}

.panel-title {
  margin: 0;
  font-size: 1.35rem;
  color: #17325b;
}

.panel-subtitle {
  margin: 0.25rem 0 0;
  color: var(--text-color-secondary);
  font-size: 0.92rem;
}

.documents-search {
  width: 100%;
}

.documents-scroll {
  flex: 1;
  min-height: 0;
  overflow: auto;
  padding: 1rem 0.1rem 0 0;
  overscroll-behavior: contain;
}

.documents-empty {
  height: 100%;
  min-height: 18rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.85rem;
  color: var(--text-color-secondary);
  text-align: center;
}

.document-groups {
  display: flex;
  flex-direction: column;
  gap: 1.4rem;
}

.document-group {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.document-group-label {
  padding: 0 0.25rem;
  letter-spacing: 0.03em;
  font-size: 0.88rem;
  font-weight: 800;
  color: #5f7598;
}

.document-group-count {
  font-weight: 600;
}

.document-item {
  width: 100%;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 0.55rem;
  padding: 0.4rem 0.55rem 0.4rem 0.85rem;
  border: 1px solid transparent;
  border-radius: 8px;
  background: transparent;
  color: inherit;
  text-align: left;
  cursor: pointer;
  transition: background 0.18s ease, border-color 0.18s ease, transform 0.18s ease;
}

.document-item:hover {
  background: #f7fbff;
  border-color: #e2ebf8;
}

.document-item-active {
  background: linear-gradient(180deg, #f2fbf7 0%, #f7fbff 100%);
  border-color: #cfe4d8;
}

.document-item-icon {
  line-height: 1;
  font-size: 0.95rem;
}

.document-item-title {
  font-weight: 600;
  color: #19325f;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.document-item-state-inline {
  font-size: 0.85rem;
  font-weight: 800;
  color: #127247;
}

.document-item-state-inline-disabled {
  color: #7b8796;
}

.editor-header {
  flex-shrink: 0;
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--surface-border);
}

.editor-header-text {
  min-width: 0;
}

.editor-header-title-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.editor-title-display {
  padding: 0;
  border: 0;
  background: transparent;
  font-size: 1.95rem;
  font-weight: 800;
  color: #14315d;
  text-align: left;
  cursor: text;
}

.editor-title-input {
  flex: 1;
  min-width: 18rem;
}

.editor-title-input :deep(.p-inputtext) {
  padding: 0;
  border: 0;
  border-radius: 0;
  background: transparent;
  font-size: 1.8rem;
  font-weight: 800;
  color: #14315d;
  box-shadow: none;
}

.editor-version-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.25rem 0.65rem;
  border-radius: 999px;
  background: #eef3fb;
  color: #35527f;
  font-size: 0.82rem;
  font-weight: 700;
}

.editor-meta {
  margin-top: 0.35rem;
  display: flex;
  gap: 0.9rem;
  flex-wrap: wrap;
  color: var(--text-color-secondary);
  font-size: 0.88rem;
}

.editor-primary-action {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.editor-actions {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
  justify-content: flex-end;
  flex-shrink: 0;
}

.editor-body {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding-top: 1rem;
  overflow: hidden;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.content-section {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 0.8rem;
}

.content-section-header {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 1rem;
  flex-wrap: wrap;
  flex-shrink: 0;
}

.content-toolbar {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.editor-pane {
  flex: 1;
  min-height: 22rem;
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

.properties-shell {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.property-section-label {
  font-size: 0.8rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #5f7598;
}

.property-facts {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-top: 0.5rem;
  padding-top: 1rem;
  border-top: 1px solid var(--surface-border);
}

.property-facts-tight {
  margin-top: 0;
}

.property-fact {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.property-fact-label {
  font-size: 0.8rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: #5f7598;
}

.property-fact-value {
  color: #19325f;
  word-break: break-word;
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

.guidance-properties-drawer :deep(.p-drawer-header) {
  padding-bottom: 0.5rem;
}

.guidance-properties-drawer :deep(.p-drawer-content) {
  padding-top: 0.5rem;
}

@media (max-width: 1180px) {
  .admin-ai-guidance-view {
    height: auto;
    overflow: visible;
  }

  .workspace-shell {
    grid-template-columns: 1fr;
    overflow: visible;
  }

  .documents-scroll {
    max-height: 20rem;
  }

  .editor-body {
    overflow: visible;
  }

  .editor-pane {
    min-height: 28rem;
  }

  .editor-header,
  .content-section-header {
    flex-direction: column;
    align-items: stretch;
  }

  .editor-actions {
    justify-content: flex-start;
  }

  .content-toolbar {
    justify-content: space-between;
  }
}
</style>
