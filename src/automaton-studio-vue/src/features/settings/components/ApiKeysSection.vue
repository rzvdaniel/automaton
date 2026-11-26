<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
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
      detail: 'API key revoked',
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
    message: `Revoke the API key "${name}"? Anything that uses it stops working. This cannot be undone.`,
    header: 'Revoke API key',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Revoke',
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
  <section class="api-keys" data-testid="api-keys">
  <div class="head">
    <p class="studio-muted lede">Keys are for scripts and CI jobs. To connect an AI or the CLI, use Connect your AI instead.</p>
    <span v-tooltip.left="!isEmailConfirmed ? lockedFeatureHint : undefined">
      <Button
        label="Create key"
        :icon="!isEmailConfirmed ? 'pi pi-lock' : 'pi pi-plus'"
        :disabled="!isEmailConfirmed"
        @click="openCreateDialog"
      />
    </span>
  </div>

  <Message v-if="!isEmailConfirmed" severity="warn" :closable="false" class="mb-3">
    <div class="confirmation-guard">
      <div>Confirm your email to create API keys.</div>
      <Button label="Change email" size="small" severity="secondary" outlined @click="router.push('/account')" />
    </div>
  </Message>

  <p v-if="!loading && apiKeys.length === 0" class="studio-muted empty" data-testid="keys-empty">
    No keys. Create one only for a script or CI job.
  </p>

  <div v-else-if="apiKeys.length > 0" class="table-wrap">
    <table class="keys" data-testid="keys-table">
      <thead>
        <tr><th>Name</th><th>Key</th><th>Type</th><th>Created by</th><th>Created</th><th>Last used</th><th>State</th><th></th></tr>
      </thead>
      <tbody>
        <tr v-for="key in apiKeys" :key="key.id">
          <td class="name" data-label="Name">{{ key.name }}</td>
          <td data-label="Key"><code>{{ key.keyPrefix }}</code></td>
          <td data-label="Type">{{ getScopeDisplayName(key.scope) }}</td>
          <td data-label="Created by">{{ getSourceDisplayName(key.source) }}</td>
          <td data-label="Created">{{ formatDate(key.createdAt) }}</td>
          <td data-label="Last used">{{ formatDate(key.lastUsedAt) }}</td>
          <td data-label="State"><span class="state"><span class="dot" :class="{ off: !key.isActive }" aria-hidden="true"></span>{{ key.isActive ? 'Active' : 'Revoked' }}</span></td>
          <td class="end">
            <Button label="Revoke" size="small" outlined class="revoke" @click="confirmDeleteApiKey(key.id, key.name)" />
          </td>
        </tr>
      </tbody>
    </table>
  </div>

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
            <Tag value="Recommended" severity="secondary" />
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
  </section>
</template>

<style scoped>
.head { display: flex; align-items: center; justify-content: space-between; gap: 16px; flex-wrap: wrap; margin-bottom: 12px; }
.lede { margin: 0; flex: 1 1 280px; }
.empty { margin: 0; padding: 16px 0; }
.confirmation-guard { width: 100%; display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
.table-wrap { overflow-x: auto; }
.keys { width: 100%; border-collapse: collapse; font-size: 14px; color: var(--ink); }
.keys th { text-align: left; font-weight: 500; font-size: 12px; color: var(--ink-soft); padding: 8px 12px 8px 0; border-bottom: 1px solid var(--line); white-space: nowrap; }
.keys td { padding: 10px 12px 10px 0; border-bottom: 1px solid var(--row-line); vertical-align: middle; }
.keys td.name { font-weight: 600; overflow-wrap: anywhere; }
.keys td.end { text-align: right; padding-right: 0; }
.keys code { white-space: nowrap; font-family: var(--mono); font-size: 13px; color: var(--ink-soft); }
.state { display: inline-flex; align-items: center; gap: 6px; white-space: nowrap; }
.dot { width: 8px; height: 8px; border-radius: 50%; background: var(--ink); }
.dot.off { background: transparent; border: 1px solid var(--ink-soft); }
.revoke, .revoke :deep(.p-button-label) { color: var(--danger, #b42318); }
.revoke { border-color: var(--danger, #b42318); }

@media (max-width: 768px) {
  .confirmation-guard { flex-direction: column; align-items: flex-start; }
}

/* Phone width: each key is a stacked row (label left, value right) instead of a table wider than the screen. */
@media (max-width: 640px) {
  .keys thead { display: none; }
  .keys, .keys tbody, .keys tr, .keys td { display: block; }
  .keys tr { padding: 8px 0; border-bottom: 1px solid var(--row-line); }
  .keys td { display: flex; justify-content: space-between; gap: 1rem; border: 0; padding: 4px 0; text-align: right; }
  .keys td[data-label]::before { content: attr(data-label); color: var(--ink-soft); font-size: 13px; text-align: left; }
  .keys td.end { justify-content: flex-end; }
}
</style>
