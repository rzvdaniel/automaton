<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
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
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { apiKeysService } from '../services/apikeys.service'
import {
  ApiKeyScope,
  getScopeDisplayName,
  getScopeSeverity,
  getSourceDisplayName,
  getUsageInstructions,
  type ApiKey,
  type CreateApiKeyRequest,
  type CreateApiKeyResponse,
} from '../types/apikeys.types'

const toast = useToast()
const confirm = useConfirm()
const router = useRouter()
const authStore = useAuthStore()

// State
const loading = ref(true)
const apiKeys = ref<ApiKey[]>([])
const showCreateDialog = ref(false)
const showSuccessDialog = ref(false)
const createLoading = ref(false)
const newApiKeyData = ref<CreateApiKeyResponse | null>(null)
const showAdvancedScopes = ref(false)
const isEmailConfirmed = computed(() => authStore.user?.emailConfirmed ?? false)
const lockedFeatureHint = 'Confirm your email to unlock API key creation.'

// Form
const createForm = ref<CreateApiKeyRequest>({
  name: '',
  description: '',
  scope: ApiKeyScope.Developer,
})

// Advanced key types. Developer / CLI is the default and does not require a choice.
const isSystemAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
const permissionsAdminDays = 30
const advancedScopeOptions = computed(() => [
  { label: 'Developer / CLI — Recommended', value: ApiKeyScope.Developer },
  { label: 'Read Only', value: ApiKeyScope.ReadOnly },
  { label: 'Workflow Execution', value: ApiKeyScope.WorkflowExecution },
  { label: 'Web App (Experimental)', value: ApiKeyScope.WebApp },
  ...(isSystemAdministrator.value
    ? [{ label: `Admin: approve app permissions (expires in ${permissionsAdminDays} days)`, value: ApiKeyScope.PermissionsAdmin }]
    : []),
])

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
  if (!isEmailConfirmed.value) {
    toast.add({
      severity: 'warn',
      summary: 'Email Confirmation Required',
      detail: lockedFeatureHint,
      life: 4000,
    })
    return
  }

  createForm.value = {
    name: '',
    description: '',
    scope: ApiKeyScope.Developer,
  }
  showAdvancedScopes.value = false
  showCreateDialog.value = true
}

function toggleAdvancedScopes() {
  showAdvancedScopes.value = !showAdvancedScopes.value
  if (!showAdvancedScopes.value) {
    createForm.value.scope = ApiKeyScope.Developer
  }
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
    const request: CreateApiKeyRequest = { ...createForm.value }
    if (request.scope === ApiKeyScope.PermissionsAdmin) {
      request.expiresAt = new Date(Date.now() + permissionsAdminDays * 86_400_000).toISOString()
    }
    const response = await apiKeysService.create(request)
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
      <p class="page-subtitle">Use a Developer key with the Automaton CLI, or create an advanced key for a specific integration.</p>
    </div>
    <div class="page-actions">
      <span v-tooltip.left="!isEmailConfirmed ? lockedFeatureHint : undefined">
        <Button
          label="Generate New Key"
          :icon="!isEmailConfirmed ? 'pi pi-lock' : 'pi pi-plus'"
          :disabled="!isEmailConfirmed"
          @click="openCreateDialog"
        />
      </span>
    </div>
  </div>

  <Message v-if="!isEmailConfirmed" severity="warn" :closable="false" class="mb-4">
    <div class="confirmation-guard">
      <div>
        Confirm your email to create API keys for runners, published apps, and external
        integrations.
      </div>
      <Button
        label="Change Email"
        size="small"
        severity="secondary"
        outlined
        @click="router.push('/account')"
      />
    </div>
  </Message>

  <div v-if="!loading && apiKeys.length === 0" class="api-keys-empty mb-4">
    <i class="pi pi-key api-keys-empty-icon"></i>
    <h3 class="mt-0 mb-2">No API keys created yet</h3>
    <p class="text-color-secondary mt-0 mb-3">
      API keys let runners, scripts, and published apps authenticate with Automaton.
      Create a scoped key for the job instead of reusing one broad credential everywhere.
    </p>
    <div class="api-keys-scopes mb-4">
      <span class="scope-chip">Developer / CLI</span>
      <span class="scope-chip">Web App</span>
      <span class="scope-chip">Read Only</span>
    </div>
    <span v-tooltip.bottom="!isEmailConfirmed ? lockedFeatureHint : undefined">
      <Button
        label="Generate Your First Key"
        :icon="!isEmailConfirmed ? 'pi pi-lock' : 'pi pi-plus'"
        :disabled="!isEmailConfirmed"
        @click="openCreateDialog"
      />
    </span>
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

    <Column field="name" header="Name" body-class="cell-name" :sortable="true" />

    <Column field="keyPrefix" header="Key" body-class="cell-key">
      <template #body="{ data }">
        <code class="monospace text-sm text-color-secondary">{{ data.keyPrefix }}</code>
      </template>
    </Column>

    <Column field="scope" header="Key type" body-class="cell-scope" :sortable="true">
      <template #body="{ data }">
        <Tag :value="getScopeDisplayName(data.scope)" :severity="getScopeSeverity(data.scope)" />
      </template>
    </Column>

    <Column field="source" header="Created by" body-class="cell-source" :sortable="true">
      <template #body="{ data }">
        <span class="text-sm text-color-secondary">{{ getSourceDisplayName(data.source) }}</span>
      </template>
    </Column>

    <Column field="createdAt" header="Created" body-class="cell-created" :sortable="true">
      <template #body="{ data }">
        <span class="text-sm text-color-secondary">{{ formatDate(data.createdAt) }}</span>
      </template>
    </Column>

    <Column field="lastUsedAt" header="Last Used" body-class="cell-used" :sortable="true">
      <template #body="{ data }">
        <span class="text-sm text-color-secondary">{{ formatDate(data.lastUsedAt) }}</span>
      </template>
    </Column>

    <Column field="isActive" header="Status" body-class="cell-status" :sortable="true">
      <template #body="{ data }">
        <Tag
          :value="data.isActive ? 'Active' : 'Revoked'"
          :severity="data.isActive ? 'success' : 'danger'"
        />
      </template>
    </Column>

    <Column header="" body-class="cell-actions" style="width: 100px; text-align: right">
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
    :style="{ width: '500px', maxWidth: '95vw' }"
  >
    <div class="flex flex-column gap-4">
      <div class="flex flex-column gap-2">
        <label for="apiKeyName" class="font-medium">Name *</label>
        <InputText
          id="apiKeyName"
          v-model="createForm.name"
          placeholder="Development laptop, CI deployment, production VM…"
          class="w-full"
        />
      </div>

      <div class="flex flex-column gap-2">
        <label class="font-medium">Key type</label>
        <div v-if="!showAdvancedScopes" class="surface-ground border-round p-3">
          <div class="flex align-items-center gap-2">
            <span class="font-medium">Developer / CLI</span>
            <Tag value="Recommended" severity="success" />
          </div>
          <p class="text-color-secondary text-sm mb-0 mt-2">
            Use with the Automaton CLI to create, deploy, publish, and manage your apps.
          </p>
        </div>
        <Dropdown
          v-else
          id="apiKeyScope"
          v-model="createForm.scope"
          :options="advancedScopeOptions"
          option-label="label"
          option-value="value"
          placeholder="Select key type"
          class="w-full"
        />
        <Button
          :label="showAdvancedScopes ? 'Hide advanced key types' : 'Show advanced key types'"
          link
          class="align-self-start p-0"
          @click="toggleAdvancedScopes"
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
    :style="{ width: '600px', maxWidth: '95vw' }"
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

.page-actions {
  display: flex;
  align-items: center;
}

.confirmation-guard {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
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

@media (max-width: 768px) {
  .confirmation-guard,
  .page-header {
    flex-direction: column;
    align-items: flex-start;
  }
}

/* Phone width: each key is a stacked card (label left, value right) instead of a table wider than the screen. */
@media (max-width: 640px) {
  .keys-table :deep(thead) { display: none; }
  .keys-table :deep(tbody tr:not(.p-datatable-empty-message)) {
    display: block;
    padding: 0.5rem 0;
    border-bottom: 1px solid var(--p-content-border-color);
  }
  .keys-table :deep(tbody tr:not(.p-datatable-empty-message) td) {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    width: auto !important;
    padding: 0.25rem 1rem;
    border: 0;
    text-align: right;
  }
  .keys-table :deep(tbody tr:not(.p-datatable-empty-message) td::before) {
    flex: none;
    color: var(--p-text-muted-color);
    font-size: 0.8125rem;
    text-align: left;
  }
  .keys-table :deep(td.cell-name::before) { content: 'Name'; }
  .keys-table :deep(td.cell-key::before) { content: 'Key'; }
  .keys-table :deep(td.cell-scope::before) { content: 'Key type'; }
  .keys-table :deep(td.cell-source::before) { content: 'Created by'; }
  .keys-table :deep(td.cell-created::before) { content: 'Created'; }
  .keys-table :deep(td.cell-used::before) { content: 'Last used'; }
  .keys-table :deep(td.cell-status::before) { content: 'Status'; }
  .keys-table :deep(td.cell-actions::before) { content: none; }
  .keys-table :deep(td.cell-actions) { justify-content: flex-end; }
  .keys-table :deep(td.cell-name) { font-weight: 600; word-break: break-word; }
}
</style>
