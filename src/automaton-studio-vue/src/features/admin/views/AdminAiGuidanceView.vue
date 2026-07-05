<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import SelectButton from 'primevue/selectbutton'
import Dropdown from 'primevue/dropdown'
import Drawer from 'primevue/drawer'
import type { AiGuidanceAdmin, UpsertAiGuidanceRequest } from '../types/admin.types'
import { adminAiGuidanceService } from '../services/adminAiGuidanceService'
import { renderMarkdown } from '@/shared/utils/markdown'

const CodeEditor = defineAsyncComponent(() => import('@/features/apps/components/CodeEditor.vue'))

type GuidanceForm = UpsertAiGuidanceRequest

interface GuidanceGroup {
  id: string
  surfaceLabel: string
  sectionLabel: string
  items: AiGuidanceAdmin[]
}

interface DocumentBadge {
  label: string
  className: string
}

type DocumentFilter = 'active' | 'disabled' | 'all'

const SURFACE_OPTIONS = [
  { label: 'Studio', value: 'studio' },
  { label: 'Shared', value: 'shared' },
  { label: 'CLI', value: 'cli' },
  { label: 'MCP', value: 'mcp' },
]

const DOCUMENT_KIND_OPTIONS = [
  { label: 'Base Prompt', value: 'BasePrompt' },
  { label: 'System Prompt', value: 'SystemPrompt' },
  { label: 'Guidance', value: 'Guidance' },
]

const EDITOR_TABS = [
  { label: 'Markdown', value: 'markdown' },
  { label: 'Preview', value: 'preview' },
]

const INSPECTOR_TABS = [
  { label: 'Properties', value: 'properties' },
  { label: 'History', value: 'history' },
]

const DOCUMENT_FILTER_OPTIONS = [
  { label: 'Active', value: 'active' },
  { label: 'Disabled', value: 'disabled' },
  { label: 'All', value: 'all' },
]

const SURFACE_LABELS: Record<string, string> = {
  studio: 'Studio',
  shared: 'Shared',
  cli: 'CLI',
  mcp: 'MCP',
}

const DOCUMENT_KIND_LABELS: Record<string, string> = {
  BasePrompt: 'Base Prompt',
  SystemPrompt: 'System Prompt',
  Guidance: 'Guidance',
}

const toast = useToast()
const confirm = useConfirm()

const documents = ref<AiGuidanceAdmin[]>([])
const selectedId = ref<string | null>(null)
const loading = ref(false)
const saving = ref(false)
const query = ref('')
const createTitle = ref('')
const documentFilter = ref<DocumentFilter>('active')
const deleteConfirmKey = ref('')
const activeTab = ref<'markdown' | 'preview'>('markdown')
const propertiesOpen = ref(false)
const inspectorTab = ref<'properties' | 'history'>('properties')
const editingTitle = ref(false)

const form = ref<GuidanceForm>(createEmptyForm())

const selectedDocument = computed(() =>
  documents.value.find(item => item.id === selectedId.value) ?? null
)

const isNewDocument = computed(() => selectedId.value === null)
const isCreateFormValid = computed(() => createTitle.value.trim().length > 0)
const activeDocumentCount = computed(() => documents.value.filter(item => item.isEnabled).length)
const disabledDocumentCount = computed(() => documents.value.filter(item => !item.isEnabled).length)
const canDeleteSelectedDocument = computed(() =>
  !!selectedDocument.value &&
  !selectedDocument.value.isEnabled &&
  deleteConfirmKey.value.trim() === selectedDocument.value.key
)

const filteredDocuments = computed(() => {
  const normalizedQuery = query.value.trim().toLowerCase()
  const base = documents.value
    .filter(item => {
      if (documentFilter.value === 'active') return item.isEnabled
      if (documentFilter.value === 'disabled') return !item.isEnabled
      return true
    })
    .sort((a, b) => {
    if (a.surface !== b.surface) return a.surface.localeCompare(b.surface)
    if (a.documentKind !== b.documentKind) return getDocumentKindRank(a.documentKind) - getDocumentKindRank(b.documentKind)
    if (a.requiredByDefault !== b.requiredByDefault) return Number(b.requiredByDefault) - Number(a.requiredByDefault)
    return a.title.localeCompare(b.title)
    })

  if (!normalizedQuery) {
    return base
  }

  return base.filter(item =>
    item.title.toLowerCase().includes(normalizedQuery) ||
    item.key.toLowerCase().includes(normalizedQuery) ||
    item.surface.toLowerCase().includes(normalizedQuery) ||
    (item.tagsJson ?? '').toLowerCase().includes(normalizedQuery)
  )
})

const groupedDocuments = computed<GuidanceGroup[]>(() => {
  const groups = new Map<string, GuidanceGroup>()

  for (const document of filteredDocuments.value) {
    const groupKey = `${document.surface}:${getTreeSectionKey(document)}`
    const existing = groups.get(groupKey)
    if (existing) {
      existing.items.push(document)
      continue
    }

    groups.set(groupKey, {
      id: groupKey,
      surfaceLabel: SURFACE_LABELS[document.surface] ?? document.surface,
      sectionLabel: getTreeSectionLabel(document),
      items: [document],
    })
  }

  return Array.from(groups.values())
})

const documentTitle = computed(() => form.value.title.trim() || 'Untitled guidance document')
const renderedPreview = computed(() => renderMarkdown(form.value.contentMarkdown || ''))
const inspectorTitle = computed(() => inspectorTab.value === 'history' ? 'History' : 'Properties')
const headerBadges = computed(() => selectedDocument.value ? buildDocumentBadges(selectedDocument.value) : [])
const editorFilePath = computed(() => {
  const key = form.value.key.trim() || selectedDocument.value?.key || 'untitled-guidance-document'
  return `admin-ai-guidance/${key}.md`
})

onMounted(loadDocuments)

onMounted(() => {
  document.querySelector('.app-main')?.classList.add('app-main--fullscreen')
})

onUnmounted(() => {
  document.querySelector('.app-main')?.classList.remove('app-main--fullscreen')
})

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
  deleteConfirmKey.value = ''
  applyDocumentToForm(document)
  activeTab.value = 'markdown'
  propertiesOpen.value = false
  inspectorTab.value = 'properties'
  editingTitle.value = false
}

function startCreate() {
  selectedId.value = null
  createTitle.value = ''
  deleteConfirmKey.value = ''
  form.value = createEmptyForm()
  activeTab.value = 'markdown'
  propertiesOpen.value = false
  inspectorTab.value = 'properties'
  editingTitle.value = false
}

function cloneDocument() {
  if (!selectedDocument.value) {
    return
  }

  const source = selectedDocument.value
  selectedId.value = null
  createTitle.value = `${source.title} Copy`
  deleteConfirmKey.value = ''
  form.value = createEmptyForm()
  activeTab.value = 'markdown'
  propertiesOpen.value = false
  inspectorTab.value = 'properties'
  editingTitle.value = false
}

function openInspector(tab: 'properties' | 'history') {
  inspectorTab.value = tab
  propertiesOpen.value = true
}

async function saveDocument() {
  if (saving.value) {
    return
  }

  saving.value = true
  let saved: AiGuidanceAdmin
  try {
    const payload: UpsertAiGuidanceRequest = {
      key: form.value.key.trim(),
      title: form.value.title.trim(),
      surface: form.value.surface.trim(),
      documentKind: form.value.documentKind.trim(),
      description: normalizeOptional(form.value.description),
      tagsJson: normalizeOptional(form.value.tagsJson),
      contentMarkdown: form.value.contentMarkdown,
      isEnabled: form.value.isEnabled,
      requiredByDefault: form.value.requiredByDefault,
      isModelLoadable: form.value.isModelLoadable,
      sortOrder: form.value.sortOrder,
    }

    saved = selectedId.value
      ? await adminAiGuidanceService.update(selectedId.value, payload)
      : await adminAiGuidanceService.create(payload)
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Save Failed',
      detail: extractErrorMessage(error, 'Failed to save AI guidance document'),
      life: 4000,
    })
    return
  } finally {
    saving.value = false
  }

  const wasEditingExisting = !!selectedId.value
  selectedId.value = saved.id
  applyDocumentToForm(saved)
  upsertDocument(saved)

  toast.add({
    severity: 'success',
    summary: wasEditingExisting ? 'Saved' : 'Created',
    detail: 'AI guidance document saved',
    life: 3000,
  })
}

async function createDocumentFromTitle() {
  if (saving.value) {
    return
  }

  if (!isCreateFormValid.value) {
    return
  }

  const title = createTitle.value.trim()
  const key = slugifyKey(title)
  if (!key) {
    toast.add({
      severity: 'warn',
      summary: 'Invalid Title',
      detail: 'Enter a title that can generate a valid document key.',
      life: 3500,
    })
    return
  }

  saving.value = true
  let saved: AiGuidanceAdmin
  try {
    saved = await adminAiGuidanceService.create({
      key,
      title,
      surface: 'studio',
      documentKind: 'Guidance',
      description: undefined,
      tagsJson: undefined,
      contentMarkdown: `# ${title}\n`,
      isEnabled: true,
      requiredByDefault: false,
      isModelLoadable: true,
      sortOrder: 100,
    })
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Create Failed',
      detail: extractErrorMessage(error, 'Failed to create AI guidance document'),
      life: 4000,
    })
    return
  } finally {
    saving.value = false
  }

  selectedId.value = saved.id
  createTitle.value = ''
  applyDocumentToForm(saved)
  upsertDocument(saved)

  toast.add({
    severity: 'success',
    summary: 'Created',
    detail: 'AI guidance document created',
    life: 3000,
  })
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

async function applyMutationResult(document: AiGuidanceAdmin) {
  selectedId.value = document.id
  deleteConfirmKey.value = ''
  applyDocumentToForm(document)
  upsertDocument(document)
}

function upsertDocument(document: AiGuidanceAdmin) {
  const next = [...documents.value]
  const index = next.findIndex(item => item.id === document.id)
  if (index >= 0) {
    next[index] = document
  } else {
    next.unshift(document)
  }

  documents.value = next
}

function applyDocumentToForm(document: AiGuidanceAdmin) {
  form.value = {
    key: document.key,
    title: document.title,
    surface: document.surface,
    documentKind: document.documentKind,
    description: document.description ?? '',
    tagsJson: document.tagsJson ?? '',
    contentMarkdown: document.contentMarkdown,
    isEnabled: document.isEnabled,
    requiredByDefault: document.requiredByDefault,
    isModelLoadable: document.isModelLoadable,
    sortOrder: document.sortOrder,
  }
}

function createEmptyForm(): GuidanceForm {
  return {
    key: '',
    title: '',
    surface: 'studio',
    documentKind: 'Guidance',
    description: '',
    tagsJson: '',
    contentMarkdown: '',
    isEnabled: true,
    requiredByDefault: false,
    isModelLoadable: true,
    sortOrder: 100,
  }
}

function cancelCreate() {
  const fallback = documents.value[0] ?? null
  if (fallback) {
    selectDocument(fallback)
    return
  }

  createTitle.value = ''
  deleteConfirmKey.value = ''
  selectedId.value = null
  form.value = createEmptyForm()
  activeTab.value = 'markdown'
  propertiesOpen.value = false
  inspectorTab.value = 'properties'
  editingTitle.value = false
}

function slugifyKey(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

function getDocumentKindLabel(kind: string) {
  return DOCUMENT_KIND_LABELS[kind] ?? kind
}

function getDocumentKindRank(kind: string) {
  switch (kind) {
    case 'BasePrompt':
      return 0
    case 'SystemPrompt':
      return 1
    default:
      return 2
  }
}

function getTreeSectionKey(document: AiGuidanceAdmin) {
  return document.documentKind === 'BasePrompt' ? 'base' : 'guidance'
}

function getTreeSectionLabel(document: AiGuidanceAdmin) {
  return document.documentKind === 'BasePrompt' ? 'Base Prompt' : 'Guidance'
}

function buildDocumentBadges(document: AiGuidanceAdmin): DocumentBadge[] {
  const badges: DocumentBadge[] = [
    { label: getDocumentKindLabel(document.documentKind), className: `doc-badge-kind-${document.documentKind.toLowerCase()}` },
    { label: SURFACE_LABELS[document.surface] ?? document.surface, className: 'doc-badge-surface' },
  ]

  if (document.requiredByDefault) {
    badges.push({ label: 'Always Loaded', className: 'doc-badge-required' })
  }

  if (document.isModelLoadable) {
    badges.push({ label: 'Discoverable', className: 'doc-badge-loadable' })
  }

  return badges
}

function normalizeOptional(value?: string) {
  return value && value.trim().length > 0 ? value.trim() : undefined
}

function extractErrorMessage(error: unknown, fallback: string) {
  const axiosError = error as {
    response?: {
      data?: {
        message?: string
        title?: string
        errors?: Record<string, string[] | string>
      } | string
    }
  }

  const responseData = axiosError?.response?.data
  if (!responseData) {
    return fallback
  }

  if (typeof responseData === 'string' && responseData.trim().length > 0) {
    return responseData
  }

  if (typeof responseData === 'object') {
    if (responseData.message && responseData.message.trim().length > 0) {
      return responseData.message
    }

    if (responseData.title && responseData.title.trim().length > 0) {
      return responseData.title
    }

    if (responseData.errors) {
      const messages = Object.entries(responseData.errors)
        .flatMap(([field, value]) => {
          const entries = Array.isArray(value) ? value : [value]
          return entries.map(entry => `${field}: ${entry}`)
        })
        .filter(entry => entry.trim().length > 0)

      if (messages.length > 0) {
        return messages.join(' | ')
      }
    }
  }

  return fallback
}

async function deleteSelectedDocument() {
  if (!selectedDocument.value || saving.value || !canDeleteSelectedDocument.value) {
    return
  }

  saving.value = true
  try {
    await adminAiGuidanceService.delete(selectedDocument.value.id)
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Delete Failed',
      detail: extractErrorMessage(error, 'Failed to delete AI guidance document'),
      life: 4000,
    })
    return
  } finally {
    saving.value = false
  }

  const deletedId = selectedDocument.value.id
  documents.value = documents.value.filter(item => item.id !== deletedId)
  deleteConfirmKey.value = ''

  const fallback = documents.value.find(item =>
    documentFilter.value === 'disabled' ? !item.isEnabled : documentFilter.value === 'active' ? item.isEnabled : true)
  if (fallback) {
    selectDocument(fallback)
  } else {
    startCreate()
  }

  toast.add({
    severity: 'success',
    summary: 'Deleted',
    detail: 'AI guidance document deleted permanently',
    life: 3000,
  })
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
      </div>
    </div>

    <div class="workspace-shell">
      <Card class="documents-card">
        <template #content>
          <div class="documents-shell">
            <div class="documents-header">
              <div class="documents-heading">
                <div>
                <h2 class="panel-title">Documents</h2>
                </div>
                <Button label="New" icon="pi pi-plus" size="small" @click="startCreate" />
              </div>
              <SelectButton
                v-model="documentFilter"
                :options="[
                  { label: `Active (${activeDocumentCount})`, value: 'active' },
                  { label: `Disabled (${disabledDocumentCount})`, value: 'disabled' },
                  { label: 'All', value: 'all' },
                ]"
                option-label="label"
                option-value="value"
                aria-label="Document filter"
              />
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
                  <section v-for="group in groupedDocuments" :key="group.id" class="document-group">
                    <div class="document-group-label">{{ group.surfaceLabel }}</div>
                    <div class="document-group-sublabel">{{ group.sectionLabel }} <span class="document-group-count">({{ group.items.length }})</span></div>

                    <button
                      v-for="document in group.items"
                      :key="document.id"
                    type="button"
                    class="document-item"
                    :class="{ 'document-item-active': selectedId === document.id, 'document-item-disabled': !document.isEnabled }"
                      @click="selectDocument(document)"
                    >
                      <span class="document-item-icon">📄</span>
                      <span class="document-item-copy">
                        <span class="document-item-title">{{ document.title }}</span>
                        <span class="document-item-badges">
                          <span v-if="document.requiredByDefault" class="document-mini-badge">Always Loaded</span>
                          <span v-if="document.isModelLoadable" class="document-mini-badge document-mini-badge-outline">Discoverable</span>
                          <span v-if="!document.isEnabled" class="document-mini-badge document-mini-badge-disabled">Disabled</span>
                        </span>
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
            <div v-if="isNewDocument" class="new-document-state">
              <h2 class="new-document-title">New Document</h2>
              <div class="new-document-form">
                <InputText
                  v-model="createTitle"
                  class="new-document-input"
                  placeholder="Document title"
                  @keyup.enter="createDocumentFromTitle"
                  autofocus
                />
                <p class="new-document-helper">
                  A key will be generated automatically. You can change metadata later in Properties.
                </p>
                <div class="new-document-actions">
                  <Button label="Cancel" severity="secondary" outlined @click="cancelCreate" />
                  <Button label="Create" icon="pi pi-arrow-right" :loading="saving" :disabled="!isCreateFormValid" @click="createDocumentFromTitle" />
                </div>
              </div>
            </div>

            <div v-else class="editor-header">
              <div class="editor-header-text">
                <div class="editor-header-topline">
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
                  <div class="editor-actions editor-actions-inline">
                    <Button
                      v-if="selectedDocument"
                      label="Clone"
                      icon="pi pi-copy"
                      severity="secondary"
                      outlined
                      @click="cloneDocument"
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
                <div class="editor-meta">
                    <span>{{ form.key || 'new-guidance' }}</span>
                  <button type="button" class="editor-meta-link" @click="openInspector('history')">
                    Version {{ selectedDocument?.version ?? 1 }} ▼
                  </button>
                  <span v-if="selectedDocument?.updatedAt">Updated {{ new Date(selectedDocument.updatedAt).toLocaleString() }}</span>
                </div>
                <div class="editor-badges">
                  <span
                    v-for="badge in headerBadges"
                    :key="badge.label"
                    class="doc-badge"
                    :class="badge.className"
                  >
                    {{ badge.label }}
                  </span>
                </div>
              </div>
            </div>

              <div v-if="!isNewDocument" class="editor-body">
                <section class="content-section">
                <div class="editor-surface-header">
                  <div class="editor-surface-tabs">
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
                      @click="openInspector('properties')"
                    />
                  </div>
                </div>

                <div class="editor-pane">
                  <CodeEditor
                    v-if="activeTab === 'markdown'"
                    :file-path="editorFilePath"
                    :content="form.contentMarkdown"
                    language="markdown"
                    @change="value => (form.contentMarkdown = value)"
                    @save="saveDocument"
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

    <Drawer
      v-model:visible="propertiesOpen"
      position="right"
      :header="inspectorTitle"
      class="guidance-properties-drawer"
      :style="{ width: '480px', maxWidth: '100vw' }"
    >
      <div class="properties-shell">
        <div class="inspector-tabs">
          <SelectButton
            v-model="inspectorTab"
            :options="INSPECTOR_TABS"
            option-label="label"
            option-value="value"
            aria-label="Inspector section"
          />
        </div>

        <template v-if="inspectorTab === 'properties'">
          <div class="property-section">General</div>

          <div class="properties-grid">
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
              <label for="guidance-document-kind">Type</label>
              <Dropdown
                id="guidance-document-kind"
                v-model="form.documentKind"
                :options="DOCUMENT_KIND_OPTIONS"
                option-label="label"
                option-value="value"
                class="w-full"
              />
            </div>
          </div>

          <div class="properties-grid properties-grid-flags">
            <label class="checkbox-field" for="guidance-required-by-default">
              <Checkbox
                input-id="guidance-required-by-default"
                v-model="form.requiredByDefault"
                binary
              />
              <span>Always loaded</span>
            </label>

            <label class="checkbox-field" for="guidance-model-loadable">
              <Checkbox
                input-id="guidance-model-loadable"
                v-model="form.isModelLoadable"
                binary
              />
              <span>Discoverable</span>
            </label>
          </div>

          <div class="field">
            <label for="guidance-description">Description</label>
            <Textarea
              id="guidance-description"
              v-model="form.description"
              class="w-full"
              auto-resize
              rows="2"
              placeholder="Short summary used for discovery and tooling."
            />
          </div>

          <div class="field">
            <label for="guidance-tags">Tags</label>
            <InputText id="guidance-tags" v-model="form.tagsJson" class="w-full" placeholder='["studio","editor"]' />
          </div>

          <div class="property-section">Information</div>

          <div class="property-facts property-facts-tight">
            <div class="property-fact property-fact-full">
              <span class="property-fact-label">Key</span>
              <span class="property-fact-value">{{ form.key || 'Not set' }}</span>
            </div>
            <div class="property-fact">
              <span class="property-fact-label">Version</span>
              <span class="property-fact-value">{{ selectedDocument?.version ?? 1 }}</span>
            </div>
            <div class="property-fact property-fact-full">
              <span class="property-fact-label">Updated</span>
              <span class="property-fact-value">{{ selectedDocument?.updatedAt ? new Date(selectedDocument.updatedAt).toLocaleString() : 'Not saved yet' }}</span>
            </div>
          </div>

          <template v-if="selectedDocument">
            <div class="property-section danger-section">Danger Zone</div>
            <div class="inspector-empty-state danger-zone">
              <strong>Delete permanently</strong>
              <span v-if="selectedDocument.isEnabled">
                Disable this document before deleting it permanently.
              </span>
              <template v-else>
                <span>Type the document key to confirm permanent deletion.</span>
                <InputText
                  v-model="deleteConfirmKey"
                  class="w-full"
                  :placeholder="selectedDocument.key"
                />
                <Button
                  label="Delete Permanently"
                  icon="pi pi-trash"
                  severity="danger"
                  :loading="saving"
                  :disabled="!canDeleteSelectedDocument"
                  @click="deleteSelectedDocument"
                />
              </template>
            </div>
          </template>
        </template>

        <template v-else-if="inspectorTab === 'history'">
          <div class="property-section">History</div>

          <div class="property-facts property-facts-tight">
            <div class="property-fact">
              <span class="property-fact-label">Current Version</span>
              <span class="property-fact-value">Version {{ selectedDocument?.version ?? 1 }}</span>
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

          <div class="inspector-empty-state">
            <strong>Version history is not available yet.</strong>
            <span>Future versions will show timeline entries, diff, and restore actions here.</span>
          </div>
        </template>

      </div>
    </Drawer>
  </div>
</template>

<style scoped>
.admin-ai-guidance-view {
  box-sizing: border-box;
  flex: 1;
  height: 100%;
  max-height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 1.5rem;
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

.documents-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
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
  overflow-y: scroll;
  overflow-x: hidden;
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
  font-size: 0.82rem;
  font-weight: 800;
  color: #5f7598;
  text-transform: uppercase;
}

.document-group-sublabel {
  padding: 0 0.25rem;
  font-size: 0.96rem;
  font-weight: 700;
  color: #17325b;
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

.document-item-disabled {
  opacity: 0.72;
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

.document-item-copy {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.document-item-badges {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}

.document-mini-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.1rem 0.45rem;
  border-radius: 999px;
  background: #e9f4ec;
  color: #21583b;
  font-size: 0.7rem;
  font-weight: 700;
}

.document-mini-badge-outline {
  background: #edf4fb;
  color: #35527f;
}

.document-mini-badge-disabled {
  background: #f1f3f6;
  color: #6f7b8b;
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
  flex: 1;
}

.editor-header-topline {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.editor-header-title-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  min-width: 0;
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

.editor-badges {
  margin-top: 0.55rem;
  display: flex;
  gap: 0.45rem;
  flex-wrap: wrap;
}

.doc-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.28rem 0.7rem;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.02em;
}

.doc-badge-kind-baseprompt {
  background: #fff3d9;
  color: #8a5a00;
}

.doc-badge-kind-systemprompt {
  background: #f3e8ff;
  color: #6f3eb2;
}

.doc-badge-kind-guidance {
  background: #e8f0ff;
  color: #234f8e;
}

.doc-badge-surface {
  background: #eef3fb;
  color: #35527f;
}

.doc-badge-required {
  background: #e9f4ec;
  color: #21583b;
}

.doc-badge-loadable {
  background: #edf4fb;
  color: #35527f;
}

.editor-meta-link {
  padding: 0;
  border: 0;
  background: transparent;
  color: inherit;
  font: inherit;
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 0.15em;
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
  flex-shrink: 0;
}

.editor-actions-inline {
  justify-content: flex-end;
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

.new-document-state {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  justify-content: flex-start;
  gap: 1rem;
  padding: 4rem 2rem 2rem;
}

.new-document-title {
  margin: 0;
  font-size: 1.35rem;
  font-weight: 800;
  color: #17325b;
}

.new-document-form {
  max-width: 38rem;
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
}

.new-document-input {
  width: 100%;
}

.new-document-helper {
  margin: -0.15rem 0 0;
  color: var(--text-color-secondary);
  font-size: 0.92rem;
}

.new-document-actions {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.field-span-3 {
  grid-column: 1 / -1;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.inline-toggle {
  display: flex;
  align-items: center;
  gap: 0.65rem;
  min-height: 2.5rem;
  padding: 0.2rem 0;
  color: #19325f;
}

.inline-toggle input {
  width: 1rem;
  height: 1rem;
}

.content-section {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 0;
}

.editor-surface-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  flex-shrink: 0;
  padding: 0 0 0.9rem;
  border-bottom: 1px solid #d7e2f0;
  background: transparent;
  box-shadow: none;
}

.editor-surface-tabs {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.editor-pane {
  flex: 1;
  min-height: 22rem;
  background: transparent;
  overflow: auto;
  box-sizing: border-box;
}

.guidance-content {
  width: 100%;
  height: 100%;
  box-sizing: border-box;
}

.guidance-content :deep(.code-editor) {
  min-height: 100%;
}

.guidance-content :deep(.cm-editor) {
  height: 100%;
  background: #fff;
  border-radius: 0;
}

.guidance-content :deep(.cm-scroller) {
  background: #fff;
}

.preview-pane {
  height: 100%;
  overflow: visible;
  padding: 1.5rem 0 0;
  background: #fff;
  box-sizing: border-box;
}

.properties-shell {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.properties-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.85rem;
}

.properties-grid-flags {
  gap: 0.65rem 1rem;
}

.checkbox-field {
  display: flex;
  align-items: center;
  gap: 0.65rem;
  min-height: 2.75rem;
  padding: 0.15rem 0;
  color: #19325f;
  font-weight: 600;
}

.checkbox-field :deep(.p-checkbox) {
  flex-shrink: 0;
}

.property-section {
  margin: 0.15rem 0 0;
  font-size: 0.8rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #5f7598;
}

.danger-section {
  color: #9f2f2f;
}

.inspector-tabs {
  margin-bottom: 0.15rem;
}

.property-section-label {
  font-size: 0.8rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #5f7598;
}

.property-facts {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.65rem;
  margin-top: 0.15rem;
  padding-top: 0.6rem;
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

.property-fact-full {
  grid-column: 1 / -1;
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

.inspector-empty-state {
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
  padding: 1rem;
  border: 1px dashed var(--surface-border);
  border-radius: 12px;
  background: color-mix(in srgb, var(--surface-card) 94%, #eef3fb 6%);
  color: var(--text-color-secondary);
}

.danger-zone {
  border-color: #f2d3d3;
  background: #fff8f8;
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
    padding: 1rem;
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
  .editor-surface-header,
  .editor-header-topline {
    flex-direction: column;
    align-items: stretch;
  }

  .editor-actions {
    justify-content: flex-start;
  }

  .editor-surface-tabs {
    justify-content: space-between;
  }

  .properties-grid,
  .property-facts {
    grid-template-columns: 1fr;
  }
}
</style>
