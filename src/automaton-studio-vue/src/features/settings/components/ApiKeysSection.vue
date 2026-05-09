<script setup lang="ts">
import { ref, onMounted } from 'vue'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Dropdown from 'primevue/dropdown'
import Message from 'primevue/message'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { apiKeysService } from '../services/apikeys.service'
import {
  ApiKeyScope,
  getScopeDisplayName,
  getScopeSeverity,
  getUsageInstructions,
  type ApiKey,
  type CreateApiKeyRequest,
  type CreateApiKeyResponse,
} from '../types/apikeys.types'

const toast = useToast()
const confirm = useConfirm()

// State
const loading = ref(true)
const apiKeys = ref<ApiKey[]>([])
const showCreateDialog = ref(false)
const showSuccessDialog = ref(false)
const createLoading = ref(false)
const newApiKeyData = ref<CreateApiKeyResponse | null>(null)

// Form
const createForm = ref<CreateApiKeyRequest>({
  name: '',
  description: '',
  scope: ApiKeyScope.Runner,
})

// Scope options for dropdown
const scopeOptions = [
  { label: 'Read Only', value: ApiKeyScope.ReadOnly },
  { label: 'Execute', value: ApiKeyScope.Execute },
  { label: 'Runner', value: ApiKeyScope.Runner },
  { label: 'Full Access', value: ApiKeyScope.FullAccess },
  { label: 'Web App', value: ApiKeyScope.WebApp },
]

// Load API keys
async function loadApiKeys() {
  loading.value = true
  try {
    apiKeys.value = await apiKeysService.list()
  } catch (err: unknown) {
    console.error('Failed to load API keys:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load API keys',
      life: 5000,
    })
  } finally {
    loading.value = false
  }
}

// Open create dialog
function openCreateDialog() {
  createForm.value = {
    name: '',
    description: '',
    scope: ApiKeyScope.Runner,
  }
  showCreateDialog.value = true
}

// Create API key
async function createApiKey() {
  if (!createForm.value.name.trim()) {
    toast.add({
      severity: 'warn',
      summary: 'Validation Error',
      detail: 'Please enter a name for the API key',
      life: 3000,
    })
    return
  }

  createLoading.value = true
  try {
    const response = await apiKeysService.create(createForm.value)
    newApiKeyData.value = response
    showCreateDialog.value = false
    showSuccessDialog.value = true
    await loadApiKeys()
  } catch (err: unknown) {
    console.error('Failed to create API key:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to create API key',
      life: 5000,
    })
  } finally {
    createLoading.value = false
  }
}

// Permanently delete API key
async function deleteApiKey(id: string) {
  try {
    await apiKeysService.delete(id)
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'API key deleted successfully',
      life: 3000,
    })
    await loadApiKeys()
  } catch (err: unknown) {
    console.error('Failed to revoke API key:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to revoke API key',
      life: 5000,
    })
  }
}

function confirmDeleteApiKey(id: string, name: string) {
  confirm.require({
    message: `Delete the API key "${name}" permanently? This cannot be undone.`,
    header: 'Delete API Key',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptClass: 'p-button-danger',
    accept: () => {
      void deleteApiKey(id)
    },
  })
}

// Copy to clipboard
async function copyToClipboard(text: string) {
  try {
    await navigator.clipboard.writeText(text)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'API key copied to clipboard',
      life: 2000,
    })
  } catch (err) {
    console.error('Failed to copy to clipboard:', err)
    toast.add({
      severity: 'warn',
      summary: 'Copy Failed',
      detail: 'Could not copy to clipboard. Please copy manually.',
      life: 3000,
    })
  }
}

// Format date
function formatDate(dateStr?: string): string {
  if (!dateStr) return 'Never'
  const date = new Date(dateStr)
  return date.toLocaleDateString() + ' ' + date.toLocaleTimeString()
}

onMounted(() => {
  loadApiKeys()
})
</script>

<template>
  <div class="page-header">
    <div>
      <h2 class="page-title">API Keys</h2>
      <p class="page-subtitle">Create keys for runners, workflow execution, web apps, and external integrations.</p>
    </div>
    <Button
      label="Generate New Key"
      icon="pi pi-plus"
      @click="openCreateDialog"
    />
  </div>

  <div v-if="!loading && apiKeys.length === 0" class="api-keys-empty mb-4">
    <i class="pi pi-key api-keys-empty-icon"></i>
    <h3 class="mt-0 mb-2">No API keys created yet</h3>
    <p class="text-color-secondary mt-0 mb-3">
      API keys let runners, scripts, and published apps authenticate with Automaton.
      Create a scoped key for the job instead of reusing one broad credential everywhere.
    </p>
    <div class="api-keys-scopes mb-4">
      <span class="scope-chip">Runner</span>
      <span class="scope-chip">Execute</span>
      <span class="scope-chip">Web App</span>
      <span class="scope-chip">Full Access</span>
    </div>
    <Button label="Generate Your First Key" icon="pi pi-plus" @click="openCreateDialog" />
  </div>

  <DataTable
    v-else
    :value="apiKeys"
    :loading="loading"
    data-key="id"
    class="keys-table"
    :paginator="apiKeys.length > 10"
    :rows="10"
  >
    <template #empty>
      <div class="text-center py-5">
        <i class="pi pi-key text-4xl text-color-secondary mb-3"></i>
        <p class="text-color-secondary">No API keys found. Create one to get started.</p>
      </div>
    </template>

    <Column field="name" header="Name" :sortable="true" />

    <Column field="keyPrefix" header="Key">
      <template #body="{ data }">
        <code class="monospace text-sm text-color-secondary">{{ data.keyPrefix }}</code>
      </template>
    </Column>

    <Column field="scope" header="Scope" :sortable="true">
      <template #body="{ data }">
        <Tag :value="getScopeDisplayName(data.scope)" :severity="getScopeSeverity(data.scope)" />
      </template>
    </Column>

    <Column field="createdAt" header="Created" :sortable="true">
      <template #body="{ data }">
        <span class="text-sm text-color-secondary">{{ formatDate(data.createdAt) }}</span>
      </template>
    </Column>

    <Column field="lastUsedAt" header="Last Used" :sortable="true">
      <template #body="{ data }">
        <span class="text-sm text-color-secondary">{{ formatDate(data.lastUsedAt) }}</span>
      </template>
    </Column>

    <Column field="isActive" header="Status" :sortable="true">
      <template #body="{ data }">
        <Tag
          :value="data.isActive ? 'Active' : 'Revoked'"
          :severity="data.isActive ? 'success' : 'danger'"
        />
      </template>
    </Column>

    <Column header="" style="width: 100px; text-align: right">
      <template #body="{ data }">
        <div class="row-actions">
          <Button
            icon="pi pi-trash"
            size="small"
            severity="danger"
            text
            rounded
            v-tooltip.top="'Delete'"
            @click="confirmDeleteApiKey(data.id, data.name)"
          />
        </div>
      </template>
    </Column>
  </DataTable>

  <!-- Create API Key Dialog -->
  <Dialog
    v-model:visible="showCreateDialog"
    header="Create API Key"
    :modal="true"
    :style="{ width: '500px' }"
  >
    <div class="flex flex-column gap-4">
      <div class="flex flex-column gap-2">
        <label for="apiKeyName" class="font-medium">Name *</label>
        <InputText
          id="apiKeyName"
          v-model="createForm.name"
          placeholder="My API Key"
          class="w-full"
        />
      </div>

      <div class="flex flex-column gap-2">
        <label for="apiKeyScope" class="font-medium">Scope *</label>
        <Dropdown
          id="apiKeyScope"
          v-model="createForm.scope"
          :options="scopeOptions"
          option-label="label"
          option-value="value"
          placeholder="Select scope"
          class="w-full"
        />
      </div>

      <div class="flex flex-column gap-2">
        <label for="apiKeyDescription" class="font-medium">Description</label>
        <Textarea
          id="apiKeyDescription"
          v-model="createForm.description"
          placeholder="Optional description"
          rows="3"
          class="w-full"
        />
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" text @click="showCreateDialog = false" />
      <Button label="Create" :loading="createLoading" @click="createApiKey" />
    </template>
  </Dialog>

  <!-- Success Dialog (shows API key once) -->
  <Dialog
    v-model:visible="showSuccessDialog"
    header="API Key Created"
    :modal="true"
    :closable="true"
    :style="{ width: '600px' }"
  >
    <div v-if="newApiKeyData" class="flex flex-column gap-4">
      <Message severity="warn" :closable="false">
        <strong>Important:</strong> This is the only time you will see the full API key. Please copy
        it now.
      </Message>

      <div class="flex flex-column gap-2">
        <label class="font-medium">API Key</label>
        <div class="flex gap-2">
          <InputText
            :value="newApiKeyData.apiKey"
            readonly
            class="flex-1"
            style="font-family: monospace"
          />
          <Button
            v-tooltip.top="'Copy to clipboard'"
            icon="pi pi-copy"
            @click="copyToClipboard(newApiKeyData.apiKey)"
          />
        </div>
      </div>

      <div class="flex flex-column gap-2">
        <label class="font-medium">Usage Instructions</label>
        <pre
          class="surface-ground p-3 border-round overflow-auto"
          style="font-size: 0.875rem"
        >{{ getUsageInstructions(newApiKeyData.apiKey, newApiKeyData.scope) }}</pre>
      </div>
    </div>

    <template #footer>
      <Button
        label="Done"
        @click="
          () => {
            showSuccessDialog = false
            newApiKeyData = null
          }
        "
      />
    </template>
  </Dialog>
</template>

<style scoped>
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

.api-keys-empty {
  text-align: center;
  padding: 2.5rem 1rem;
  border: 1px dashed var(--p-surface-300);
  border-radius: 8px;
}

:global(.app-dark) .api-keys-empty {
  border-color: var(--p-surface-600);
}

.api-keys-empty-icon {
  font-size: 3rem;
  color: var(--p-text-muted-color);
  display: block;
  margin-bottom: 1rem;
}

.api-keys-scopes {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: center;
}

.scope-chip {
  font-size: 0.8rem;
  padding: 0.25rem 0.75rem;
  border-radius: 999px;
  background: var(--p-surface-100);
  color: var(--p-text-muted-color);
  border: 1px solid var(--p-surface-200);
}

:global(.app-dark) .scope-chip {
  background: var(--p-surface-800);
  border-color: var(--p-surface-700);
}
</style>
