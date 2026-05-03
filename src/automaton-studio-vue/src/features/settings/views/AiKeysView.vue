<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Message from 'primevue/message'
import ConfirmDialog from 'primevue/confirmdialog'
import {
  getAvailableProviders,
  getUserAiApiKeys,
  createAiApiKey,
  deleteAiApiKey,
} from '@/shared/services/ai/ai-providers-service'
import type { AiApiKey, AiProvider } from '@/shared/types/ai.types'

const CUSTOM_PROVIDER = 'Custom (OpenAI-compatible)'

const toast = useToast()
const confirm = useConfirm()

// ── State ──────────────────────────────────────────────────────────────────
const keys = ref<AiApiKey[]>([])
const providers = ref<AiProvider[]>([])
const loading = ref(false)
const showDialog = ref(false)
const submitting = ref(false)

const form = ref({
  providerName: '',
  customProviderName: '',
  apiKey: '',
  name: '',
  description: '',
  baseUrl: '',
  defaultModel: '',
})
const errors = ref<Record<string, string>>({})

// ── Derived ────────────────────────────────────────────────────────────────
const providerOptions = computed(() => {
  const known = providers.value.map((p) => ({
    label: p.displayName || p.providerName,
    value: p.providerName,
  }))
  return [...known, { label: CUSTOM_PROVIDER, value: CUSTOM_PROVIDER }]
})

const isCustomProvider = computed(() => form.value.providerName === CUSTOM_PROVIDER)

const effectiveProviderName = computed(() =>
  isCustomProvider.value ? form.value.customProviderName.trim() : form.value.providerName,
)

// ── Data loading ───────────────────────────────────────────────────────────
onMounted(async () => {
  await loadAll()
})

async function loadAll() {
  loading.value = true
  try {
    const [keyData, providerData] = await Promise.all([
      getUserAiApiKeys(),
      getAvailableProviders(false, true),
    ])
    keys.value = keyData
    providers.value = providerData
  } catch {
    toast.add({ severity: 'error', summary: 'Load failed', detail: 'Could not load AI keys', life: 3000 })
  } finally {
    loading.value = false
  }
}

// ── Dialog ─────────────────────────────────────────────────────────────────
function openDialog() {
  form.value = {
    providerName: providerOptions.value[0]?.value ?? '',
    customProviderName: '',
    apiKey: '',
    name: '',
    description: '',
    baseUrl: '',
    defaultModel: '',
  }
  errors.value = {}
  showDialog.value = true
}

function validate(): boolean {
  const e: Record<string, string> = {}

  if (!form.value.providerName) e.providerName = 'Select a provider'
  if (isCustomProvider.value && !form.value.customProviderName.trim())
    e.customProviderName = 'Provider name is required'
  if (!form.value.apiKey.trim()) e.apiKey = 'API key is required'
  if (isCustomProvider.value && !form.value.baseUrl.trim()) e.baseUrl = 'Base URL is required'
  if (isCustomProvider.value && !form.value.defaultModel.trim()) e.defaultModel = 'Default model is required'

  errors.value = e
  return Object.keys(e).length === 0
}

async function submit() {
  if (!validate()) return

  submitting.value = true
  try {
    const friendlyName =
      form.value.name.trim() || `${effectiveProviderName.value} key`

    await createAiApiKey({
      name: friendlyName,
      providerName: effectiveProviderName.value,
      apiKey: form.value.apiKey,
      description: form.value.description || undefined,
      baseUrl: isCustomProvider.value ? form.value.baseUrl.trim() : undefined,
      defaultModel: isCustomProvider.value ? form.value.defaultModel.trim() : undefined,
    })

    toast.add({ severity: 'success', summary: 'Key added', detail: `${friendlyName} saved`, life: 3000 })
    showDialog.value = false
    await loadAll()
  } catch (err: any) {
    const detail = err?.response?.data?.detail || err?.message || 'Failed to save key'
    toast.add({ severity: 'error', summary: 'Error', detail, life: 4000 })
  } finally {
    submitting.value = false
  }
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
      <Button label="Add Key" icon="pi pi-plus" @click="openDialog" />
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
      <Button label="Add Your First Key" icon="pi pi-plus" @click="openDialog" />
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
      <Column header="" style="width: 60px; text-align: right">
        <template #body="{ data }">
          <Button
            icon="pi pi-trash"
            size="small"
            severity="danger"
            text
            rounded
            v-tooltip.top="'Delete'"
            @click="confirmDelete(data)"
          />
        </template>
      </Column>
    </DataTable>

    <!-- Add Key dialog -->
    <Dialog
      v-model:visible="showDialog"
      header="Add AI Key"
      :modal="true"
      :style="{ width: '480px' }"
      :closable="!submitting"
    >
      <div class="form-grid">
        <!-- Provider -->
        <div class="field">
          <label class="field-label">Provider <span class="required">*</span></label>
          <Select
            v-model="form.providerName"
            :options="providerOptions"
            option-label="label"
            option-value="value"
            placeholder="Select provider"
            class="w-full"
            :invalid="!!errors.providerName"
          />
          <small v-if="errors.providerName" class="field-error">{{ errors.providerName }}</small>
        </div>

        <!-- Custom provider name (only when Custom selected) -->
        <div v-if="isCustomProvider" class="field">
          <label class="field-label">Provider Name <span class="required">*</span></label>
          <InputText
            v-model="form.customProviderName"
            placeholder="e.g. Kimi, DeepSeek"
            class="w-full"
            :invalid="!!errors.customProviderName"
          />
          <small v-if="errors.customProviderName" class="field-error">{{ errors.customProviderName }}</small>
        </div>

        <!-- API Key -->
        <div class="field">
          <label class="field-label">API Key <span class="required">*</span></label>
          <Password
            v-model="form.apiKey"
            placeholder="sk-..."
            class="w-full"
            :feedback="false"
            toggle-mask
            input-class="w-full"
            :invalid="!!errors.apiKey"
          />
          <small v-if="errors.apiKey" class="field-error">{{ errors.apiKey }}</small>
        </div>

        <!-- Base URL (custom providers only) -->
        <div v-if="isCustomProvider" class="field">
          <label class="field-label">Base URL <span class="required">*</span></label>
          <InputText
            v-model="form.baseUrl"
            placeholder="https://api.moonshot.cn/v1"
            class="w-full"
            :invalid="!!errors.baseUrl"
          />
          <small v-if="errors.baseUrl" class="field-error">{{ errors.baseUrl }}</small>
        </div>

        <!-- Default Model (custom providers only) -->
        <div v-if="isCustomProvider" class="field">
          <label class="field-label">Default Model <span class="required">*</span></label>
          <InputText
            v-model="form.defaultModel"
            placeholder="e.g. kimi-k2.5-turbo"
            class="w-full"
            :invalid="!!errors.defaultModel"
          />
          <small v-if="errors.defaultModel" class="field-error">{{ errors.defaultModel }}</small>
        </div>

        <!-- Friendly name (optional) -->
        <div class="field">
          <label class="field-label">Friendly Name</label>
          <InputText
            v-model="form.name"
            :placeholder="`${effectiveProviderName || 'My'} key`"
            class="w-full"
          />
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" text :disabled="submitting" @click="showDialog = false" />
        <Button label="Save" icon="pi pi-check" :loading="submitting" @click="submit" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.ai-keys-view {
  padding: 1.5rem;
  max-width: 900px;
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

.form-grid {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.field-label {
  font-size: 0.875rem;
  font-weight: 500;
}

.required {
  color: var(--p-red-500);
}

.field-error {
  color: var(--p-red-500);
  font-size: 0.8rem;
}

.monospace {
  font-family: monospace;
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
