<script setup lang="ts">
import { ref, onMounted } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Dropdown from 'primevue/dropdown'
import Message from 'primevue/message'
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

// Revoke API key
async function revokeApiKey(id: string, name: string) {
  if (!confirm(`Are you sure you want to revoke the API key "${name}"? This action cannot be undone.`)) {
    return
  }

  try {
    await apiKeysService.revoke(id)
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'API key revoked successfully',
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
  <Card>
    <template #title>
      <div class="flex align-items-center justify-content-between">
        <span>API Keys</span>
        <Button
          label="Generate New Key"
          icon="pi pi-plus"
          size="small"
          @click="openCreateDialog"
        />
      </div>
    </template>
    <template #subtitle> Manage API keys for runners, webhooks, and integrations </template>
    <template #content>
      <DataTable
        :value="apiKeys"
        :loading="loading"
        class="p-datatable-sm"
        :paginator="apiKeys.length > 10"
        :rows="10"
      >
        <template #empty>
          <div class="text-center py-5">
            <i class="pi pi-key text-4xl text-color-secondary mb-3"></i>
            <p class="text-color-secondary">No API keys found. Create one to get started.</p>
          </div>
        </template>

        <Column field="name" header="Name" :sortable="true"></Column>

        <Column field="keyPrefix" header="Key">
          <template #body="{ data }">
            <code class="surface-ground p-1 border-round">{{ data.keyPrefix }}</code>
          </template>
        </Column>

        <Column field="scope" header="Scope" :sortable="true">
          <template #body="{ data }">
            <Tag :value="getScopeDisplayName(data.scope)" :severity="getScopeSeverity(data.scope)" />
          </template>
        </Column>

        <Column field="createdAt" header="Created" :sortable="true">
          <template #body="{ data }">
            {{ formatDate(data.createdAt) }}
          </template>
        </Column>

        <Column field="lastUsedAt" header="Last Used" :sortable="true">
          <template #body="{ data }">
            {{ formatDate(data.lastUsedAt) }}
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

        <Column header="Actions">
          <template #body="{ data }">
            <div class="flex gap-1">
              <Button
                v-if="data.isActive"
                icon="pi pi-trash"
                text
                rounded
                size="small"
                severity="danger"
                @click="revokeApiKey(data.id, data.name)"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

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
code {
  font-size: 0.875rem;
}
</style>
