<script setup lang="ts">
import { ref, onMounted, reactive } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Dropdown from 'primevue/dropdown'
import Tag from 'primevue/tag'
import { adminAiProvidersService } from '../services/adminAiProvidersService'
import type {
  AiProviderAdmin,
  AiApiKeyAdmin,
  CreateAiApiKeyRequest,
} from '../types/admin.types'

const toast = useToast()
const confirm = useConfirm()

// State
const providers = ref<AiProviderAdmin[]>([])
const apiKeys = ref<AiApiKeyAdmin[]>([])
const loading = ref(false)
const showCreateDialog = ref(false)
const isSubmitting = ref(false)

// CreatedKey state - to show copy dialog
const createdKey = ref<{ name: string; apiKey: string } | null>(null)
const showCreatedKeyDialog = ref(false)

// Form - extends CreateAiApiKeyRequest with providerId for UI
interface CreateAiApiKeyForm extends CreateAiApiKeyRequest {
  providerId: string
}

const formData = reactive<CreateAiApiKeyForm>({
  providerId: '',
  name: '',
  apiKey: '',
  description: '',
})

const formErrors = ref<Record<string, string>>({})

onMounted(async () => {
  await loadProviders()
  await loadApiKeys()
})

async function loadProviders() {
  try {
    providers.value = await adminAiProvidersService.getProviders()
  } catch (error) {
    console.error('Error loading providers:', error)
    toast.add({
      severity: 'error',
      summary: 'Load Failed',
      detail: 'Failed to load AI providers',
      life: 3000,
    })
  }
}

async function loadApiKeys() {
  loading.value = true
  try {
    // Load all API keys from all providers
    const allKeys: AiApiKeyAdmin[] = []
    for (const provider of providers.value) {
      const keys = await adminAiProvidersService.getProviderKeys(provider.id)
      allKeys.push(...keys)
    }
    apiKeys.value = allKeys
  } catch (error) {
    console.error('Error loading API keys:', error)
    toast.add({
      severity: 'error',
      summary: 'Load Failed',
      detail: 'Failed to load API keys',
      life: 3000,
    })
  } finally {
    loading.value = false
  }
}

function openCreateDialog() {
  formData.providerId = ''
  formData.name = ''
  formData.apiKey = ''
  formData.description = ''
  formErrors.value = {}
  showCreateDialog.value = true
}

function validateForm(): boolean {
  formErrors.value = {}

  if (!formData.providerId) {
    formErrors.value.providerId = 'Provider is required'
  }
  if (!formData.name?.trim()) {
    formErrors.value.name = 'Name is required'
  }
  if (!formData.apiKey?.trim()) {
    formErrors.value.apiKey = 'API Key is required'
  }

  return Object.keys(formErrors.value).length === 0
}

async function handleCreate() {
  if (!validateForm()) return

  isSubmitting.value = true
  try {
    await adminAiProvidersService.createProviderKey(formData.providerId, {
      name: formData.name,
      apiKey: formData.apiKey,
      description: formData.description,
    })

    // Show the created key dialog with copy button
    createdKey.value = {
      name: formData.name,
      apiKey: formData.apiKey,
    }
    showCreatedKeyDialog.value = true

    showCreateDialog.value = false
    await loadApiKeys()
  } catch (error: unknown) {
    console.error('Error creating API key:', error)
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({
      severity: 'error',
      summary: 'Creation Failed',
      detail: axiosError?.response?.data?.message || 'Failed to create API key',
      life: 3000,
    })
  } finally {
    isSubmitting.value = false
  }
}

function copyToClipboard(text: string) {
  navigator.clipboard.writeText(text)
  toast.add({
    severity: 'success',
    summary: 'Copied',
    detail: 'API key copied to clipboard',
    life: 2000,
  })
}

function getProviderName(providerId: string): string {
  return providers.value.find((p) => p.id === providerId)?.displayName || providerId
}

function getStatusSeverity(isActive: boolean) {
  return isActive ? 'success' : 'warning'
}

function confirmDeleteKey(key: AiApiKeyAdmin) {
  confirm.require({
    message: `Delete API key "${key.name}"? This action cannot be undone.`,
    header: 'Confirm Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await adminAiProvidersService.deleteApiKey(key.id)
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: 'API key deleted successfully',
          life: 3000,
        })
        await loadApiKeys()
      } catch (error) {
        console.error('Error deleting API key:', error)
        toast.add({
          severity: 'error',
          summary: 'Delete Failed',
          detail: 'Failed to delete API key',
          life: 3000,
        })
      }
    },
  })
}
</script>

<template>
  <div class="admin-ai-keys-view">
    <!-- Header -->
    <div class="flex align-items-center justify-content-between mb-4">
      <div>
        <h1 class="m-0 text-3xl font-bold">AI API Keys</h1>
        <p class="text-color-secondary mt-1 mb-0">
          Manage API keys for all AI providers
        </p>
      </div>
      <Button label="Add API Key" icon="pi pi-plus" @click="openCreateDialog" />
    </div>

    <!-- API Keys Table -->
    <Card>
      <template #content>
        <DataTable
          :value="apiKeys"
          :loading="loading"
          striped-rows
          responsive-layout="scroll"
        >
          <template #empty>
            <div class="text-center py-5 text-color-secondary">
              <i class="pi pi-key text-5xl mb-3 block"></i>
              <p class="m-0">No API keys configured yet.</p>
              <p class="text-sm mt-2">Create your first API key to get started.</p>
            </div>
          </template>

          <Column field="name" header="Name" style="min-width: 180px">
            <template #body="{ data }">
              <div class="font-medium">{{ data.name }}</div>
              <div v-if="data.description" class="text-sm text-color-secondary mt-1">
                {{ data.description }}
              </div>
            </template>
          </Column>

          <Column field="providerId" header="Provider" style="min-width: 160px">
            <template #body="{ data }">
              <span class="font-medium">{{ getProviderName(data.providerId) }}</span>
            </template>
          </Column>

          <Column field="createdAt" header="Created" style="width: 130px">
            <template #body="{ data }">
              <span class="text-sm">{{ new Date(data.createdAt).toLocaleDateString() }}</span>
            </template>
          </Column>

          <Column field="lastUsedAt" header="Last Used" style="width: 130px">
            <template #body="{ data }">
              <span v-if="data.lastUsedAt" class="text-sm">
                {{ new Date(data.lastUsedAt).toLocaleDateString() }}
              </span>
              <span v-else class="text-sm text-color-secondary">Never</span>
            </template>
          </Column>

          <Column field="isActive" header="Status" style="width: 90px">
            <template #body="{ data }">
              <Tag
                :value="data.isActive ? 'Active' : 'Inactive'"
                :severity="getStatusSeverity(data.isActive)"
              />
            </template>
          </Column>

          <Column header="Actions" style="width: 100px" align="center">
            <template #body="{ data }">
              <div class="flex gap-2 justify-content-center">
                <Button
                  icon="pi pi-trash"
                  text
                  rounded
                  severity="danger"
                  size="small"
                  title="Delete API key"
                  @click="confirmDeleteKey(data)"
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
      header="Add API Key"
      modal
      :style="{ width: '550px' }"
    >
      <div class="flex flex-column gap-4">
        <div class="flex flex-column gap-2">
          <label for="provider" class="font-medium">Provider *</label>
          <Dropdown
            id="provider"
            v-model="formData.providerId"
            :options="providers"
            option-label="displayName"
            option-value="id"
            placeholder="Select a provider"
            :class="{ 'p-invalid': formErrors.providerId }"
          />
          <small v-if="formErrors.providerId" class="text-red-500">
            {{ formErrors.providerId }}
          </small>
        </div>

        <div class="flex flex-column gap-2">
          <label for="keyName" class="font-medium">Name *</label>
          <InputText
            id="keyName"
            v-model="formData.name"
            placeholder="e.g., Production Key"
            :class="{ 'p-invalid': formErrors.name }"
          />
          <small v-if="formErrors.name" class="text-red-500">{{ formErrors.name }}</small>
        </div>

        <div class="flex flex-column gap-2">
          <label for="apiKey" class="font-medium">API Key *</label>
          <InputText
            id="apiKey"
            v-model="formData.apiKey"
            type="password"
            placeholder="Enter API key"
            :class="{ 'p-invalid': formErrors.apiKey }"
          />
          <small class="text-color-secondary">The key will be encrypted and stored securely</small>
          <small v-if="formErrors.apiKey" class="text-red-500">{{ formErrors.apiKey }}</small>
        </div>

        <div class="flex flex-column gap-2">
          <label for="keyDescription" class="font-medium">Description</label>
          <Textarea
            id="keyDescription"
            v-model="formData.description"
            placeholder="Optional description"
            rows="2"
          />
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" text :disabled="isSubmitting" @click="showCreateDialog = false" />
        <Button label="Add Key" :loading="isSubmitting" @click="handleCreate" />
      </template>
    </Dialog>

    <!-- Created Key Dialog - Shows the key with copy button -->
    <Dialog
      v-model:visible="showCreatedKeyDialog"
      header="API Key Created"
      modal
      :style="{ width: '550px' }"
    >
      <div v-if="createdKey" class="flex flex-column gap-4">
        <div class="bg-blue-50 border-left-4 border-left-blue-500 p-4">
          <p class="m-0 text-sm text-color-secondary mb-2">
            <i class="pi pi-info-circle mr-2"></i>
            Save your API key now. You won't be able to view it again.
          </p>
        </div>

        <div class="flex flex-column gap-2">
          <label class="font-medium">Key Name</label>
          <InputText :model-value="createdKey.name" disabled />
        </div>

        <div class="flex flex-column gap-2">
          <label class="font-medium">API Key</label>
          <div class="flex gap-2">
            <InputText
              :model-value="createdKey.apiKey"
              type="password"
              disabled
              class="flex-1"
            />
            <Button
              icon="pi pi-copy"
              text
              @click="copyToClipboard(createdKey.apiKey)"
              title="Copy to clipboard"
            />
          </div>
        </div>
      </div>

      <template #footer>
        <Button label="Done" @click="showCreatedKeyDialog = false" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.admin-ai-keys-view {
  max-width: 1200px;
}

:deep(.bg-blue-50) {
  background-color: var(--blue-50);
}

:deep(.border-left-4) {
  border-left-width: 4px;
}

:deep(.border-left-blue-500) {
  border-left-color: var(--blue-500);
}
</style>
