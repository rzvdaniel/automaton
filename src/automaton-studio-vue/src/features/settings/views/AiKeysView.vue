<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import ConfirmDialog from 'primevue/confirmdialog'
import { getUserAiApiKeys, deleteAiApiKey } from '@/shared/services/ai/ai-providers-service'
import type { AiApiKey } from '@/shared/types/ai.types'

const router = useRouter()
const toast = useToast()
const confirm = useConfirm()

// ── State ──────────────────────────────────────────────────────────────────
const keys = ref<AiApiKey[]>([])
const loading = ref(false)

// ── Load ───────────────────────────────────────────────────────────────────
onMounted(loadKeys)

async function loadKeys() {
  loading.value = true
  try {
    keys.value = await getUserAiApiKeys()
  } catch {
    toast.add({ severity: 'error', summary: 'Load failed', detail: 'Could not load AI keys', life: 3000 })
  } finally {
    loading.value = false
  }
}

// ── Navigation ─────────────────────────────────────────────────────────────
function addKey() {
  router.push({ name: 'ai-keys-add' })
}

function editKey(key: AiApiKey) {
  router.push({ name: 'ai-keys-edit', params: { id: key.id } })
}


// ── Delete ─────────────────────────────────────────────────────────────────
function confirmDelete(key: AiApiKey) {
  confirm.require({
    message: `Delete "${key.name}"? This cannot be undone.`,
    header: 'Delete API Key',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptClass: 'p-button-danger',
    accept: () => doDelete(key.id),
  })
}

async function doDelete(id: string) {
  try {
    await deleteAiApiKey(id)
    keys.value = keys.value.filter((k) => k.id !== id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: 'API key removed', life: 3000 })
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Could not delete key', life: 3000 })
  }
}

function formatDate(dateStr: string) {
  return new Date(dateStr).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}
</script>

<template>
  <div class="ai-keys-view">
    <ConfirmDialog />

    <!-- Header -->
    <div class="page-header">
      <div>
        <h2 class="page-title">AI Keys</h2>
        <p class="page-subtitle">Add your own API keys for OpenAI, Anthropic, Google, or custom OpenAI-compatible providers.</p>
      </div>
      <Button label="Add Key" icon="pi pi-plus" @click="addKey" />
    </div>

    <!-- Empty state -->
    <div v-if="!loading && keys.length === 0" class="ai-keys-empty mb-4">
      <i class="pi pi-key ai-keys-empty-icon"></i>
      <h3 class="mt-0 mb-2">No AI keys configured</h3>
      <p class="text-color-secondary mt-0 mb-3">
        An AI key lets Automaton call an AI provider on your behalf to generate and edit app code.
        Without one, the <strong>AI chat in the App Editor</strong> will be disabled.
      </p>
      <div class="ai-keys-providers mb-4">
        <span class="provider-chip">OpenAI</span>
        <span class="provider-chip">Anthropic</span>
        <span class="provider-chip">Google Gemini</span>
        <span class="provider-chip">Any OpenAI-compatible API</span>
      </div>
      <Button label="Add Your First Key" icon="pi pi-plus" @click="addKey" />
    </div>

    <!-- Keys table -->
    <DataTable
      v-else
      :value="keys"
      :loading="loading"
      data-key="id"
      class="keys-table"
      striped-rows
    >
      <Column field="providerName" header="Provider" style="width: 160px">
        <template #body="{ data }">
          <Tag :value="data.providerName" severity="secondary" />
        </template>
      </Column>
      <Column field="name" header="Name" />
      <Column field="baseUrl" header="Base URL">
        <template #body="{ data }">
          <span v-if="data.baseUrl" class="text-sm text-color-secondary monospace">{{ data.baseUrl }}</span>
          <span v-else class="text-color-secondary">—</span>
        </template>
      </Column>
      <Column field="createdAt" header="Added" style="width: 140px">
        <template #body="{ data }">
          <span class="text-sm text-color-secondary">{{ formatDate(data.createdAt) }}</span>
        </template>
      </Column>
      <Column header="" style="width: 100px; text-align: right">
        <template #body="{ data }">
          <div class="row-actions">
            <Button
              icon="pi pi-pencil"
              size="small"
              severity="secondary"
              text
              rounded
              v-tooltip.top="'Edit'"
              @click="editKey(data)"
            />
            <Button
              icon="pi pi-trash"
              size="small"
              severity="danger"
              text
              rounded
              v-tooltip.top="'Delete'"
              @click="confirmDelete(data)"
            />
          </div>
        </template>
      </Column>
    </DataTable>
  </div>
</template>

<style scoped>
.ai-keys-view {
  padding: 1.5rem;
  max-width: 1400px;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 1.5rem;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 600;
  margin: 0 0 0.25rem 0;
}

.page-subtitle {
  color: var(--p-text-muted-color);
  margin: 0;
  font-size: 0.9rem;
}

.keys-table {
  width: 100%;
}

.monospace {
  font-family: monospace;
}

.row-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.25rem;
}

/* Empty state */
.ai-keys-empty {
  text-align: center;
  padding: 2.5rem 1rem;
  border: 1px dashed var(--p-surface-300);
  border-radius: 8px;
}

:global(.app-dark) .ai-keys-empty {
  border-color: var(--p-surface-600);
}

.ai-keys-empty-icon {
  font-size: 3rem;
  color: var(--p-text-muted-color);
  display: block;
  margin-bottom: 1rem;
}

.ai-keys-providers {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: center;
}

.provider-chip {
  font-size: 0.8rem;
  padding: 0.25rem 0.75rem;
  border-radius: 999px;
  background: var(--p-surface-100);
  color: var(--p-text-muted-color);
  border: 1px solid var(--p-surface-200);
}

:global(.app-dark) .provider-chip {
  background: var(--p-surface-800);
  border-color: var(--p-surface-700);
}
</style>
