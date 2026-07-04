<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import type { AiGuidanceAdmin, UpsertAiGuidanceRequest } from '../types/admin.types'
import { adminAiGuidanceService } from '../services/adminAiGuidanceService'

type GuidanceForm = UpsertAiGuidanceRequest

const toast = useToast()
const confirm = useConfirm()

const documents = ref<AiGuidanceAdmin[]>([])
const selectedId = ref<string | null>(null)
const loading = ref(false)
const saving = ref(false)

const form = ref<GuidanceForm>(createEmptyForm())

const selectedDocument = computed(() =>
  documents.value.find(item => item.id === selectedId.value) ?? null
)

const isNewDocument = computed(() => selectedId.value === null)

const sortedDocuments = computed(() =>
  [...documents.value].sort((a, b) => {
    if (a.surface !== b.surface) return a.surface.localeCompare(b.surface)
    if (a.category !== b.category) return a.category.localeCompare(b.category)
    return a.title.localeCompare(b.title)
  })
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
}

function startCreate() {
  selectedId.value = null
  form.value = createEmptyForm()
}

async function saveDocument() {
  saving.value = true
  try {
    const payload: UpsertAiGuidanceRequest = {
      key: form.value.key.trim(),
      relativePath: form.value.relativePath.trim(),
      title: form.value.title.trim(),
      surface: form.value.surface.trim(),
      category: form.value.category.trim(),
      description: normalizeOptional(form.value.description),
      tagsJson: normalizeOptional(form.value.tagsJson),
      contentMarkdown: form.value.contentMarkdown,
      isEnabled: form.value.isEnabled,
    }

    const saved = selectedId.value
      ? await adminAiGuidanceService.update(selectedId.value, payload)
      : await adminAiGuidanceService.create(payload)

    selectedId.value = saved.id
    applyDocumentToForm(saved)
    await loadDocuments()

    toast.add({
      severity: 'success',
      summary: selectedId.value ? 'Saved' : 'Created',
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
    relativePath: '',
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
    <div class="page-header mb-4">
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

    <div class="guidance-layout">
      <Card class="guidance-list-card">
        <template #title>Documents</template>
        <template #content>
          <DataTable
            :value="sortedDocuments"
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

            <Column field="title" header="Document" style="min-width: 220px">
              <template #body="{ data }">
                <div class="font-medium">{{ data.title }}</div>
                <div class="text-sm text-color-secondary">{{ data.relativePath }}</div>
              </template>
            </Column>

            <Column header="Surface" style="width: 140px">
              <template #body="{ data }">
                <Tag :value="data.surface" severity="info" />
              </template>
            </Column>

            <Column header="State" style="width: 140px">
              <template #body="{ data }">
                <Tag :value="data.isEnabled ? 'Enabled' : 'Disabled'" :severity="data.isEnabled ? 'success' : 'secondary'" />
              </template>
            </Column>

            <Column field="version" header="Version" style="width: 110px" />
          </DataTable>
        </template>
      </Card>

      <Card class="guidance-editor-card">
        <template #title>
          {{ isNewDocument ? 'New Guidance Document' : 'Edit Guidance Document' }}
        </template>
        <template #content>
          <div class="form-grid">
            <div class="field">
              <label for="guidance-title">Title</label>
              <InputText id="guidance-title" v-model="form.title" class="w-full" />
            </div>

            <div class="field">
              <label for="guidance-key">Key</label>
              <InputText id="guidance-key" v-model="form.key" class="w-full" />
            </div>

            <div class="field">
              <label for="guidance-path">Relative Path</label>
              <InputText id="guidance-path" v-model="form.relativePath" class="w-full" />
            </div>

            <div class="field">
              <label for="guidance-surface">Surface</label>
              <InputText id="guidance-surface" v-model="form.surface" class="w-full" />
            </div>

            <div class="field">
              <label for="guidance-category">Category</label>
              <InputText id="guidance-category" v-model="form.category" class="w-full" />
            </div>

            <div class="field">
              <label for="guidance-tags">Tags JSON</label>
              <InputText id="guidance-tags" v-model="form.tagsJson" class="w-full" />
            </div>

            <div class="field field-full">
              <label for="guidance-description">Description</label>
              <InputText id="guidance-description" v-model="form.description" class="w-full" />
            </div>

            <div class="field field-full">
              <label for="guidance-content">Content Markdown</label>
              <Textarea id="guidance-content" v-model="form.contentMarkdown" rows="20" class="w-full guidance-content" auto-resize />
            </div>

            <div class="field field-full flex align-items-center gap-2">
              <Checkbox v-model="form.isEnabled" input-id="guidance-enabled" binary />
              <label for="guidance-enabled" class="m-0">Enabled at runtime</label>
            </div>

            <div v-if="selectedDocument" class="field field-full meta-row text-sm text-color-secondary">
              <span>Version {{ selectedDocument.version }}</span>
              <span>Created {{ new Date(selectedDocument.createdAt).toLocaleString() }}</span>
              <span v-if="selectedDocument.updatedAt">Updated {{ new Date(selectedDocument.updatedAt).toLocaleString() }}</span>
            </div>

            <div class="field field-full action-row">
              <Button label="Save" icon="pi pi-save" :loading="saving" @click="saveDocument" />
              <Button
                v-if="selectedDocument"
                :label="selectedDocument.isEnabled ? 'Disable' : 'Enable'"
                :icon="selectedDocument.isEnabled ? 'pi pi-eye-slash' : 'pi pi-eye'"
                severity="secondary"
                outlined
                @click="confirmToggleEnabled(selectedDocument)"
              />
              <Button
                v-if="selectedDocument?.hasEmbeddedDefault"
                label="Reset To Default"
                icon="pi pi-refresh"
                severity="contrast"
                outlined
                @click="confirmReset(selectedDocument)"
              />
            </div>
          </div>
        </template>
      </Card>
    </div>
  </div>
</template>

<style scoped>
.admin-ai-guidance-view {
  max-width: 1600px;
}

.guidance-layout {
  display: grid;
  grid-template-columns: minmax(320px, 460px) minmax(0, 1fr);
  gap: 1.5rem;
  align-items: start;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.field-full {
  grid-column: 1 / -1;
}

.guidance-content {
  min-height: 28rem;
  font-family: var(--font-family-monospace, monospace);
}

.meta-row {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
}

.action-row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

@media (max-width: 1024px) {
  .guidance-layout {
    grid-template-columns: 1fr;
  }

  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>
