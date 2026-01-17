<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Dropdown from 'primevue/dropdown'
import Password from 'primevue/password'
import Message from 'primevue/message'
import { useToast } from 'primevue/usetoast'
import { aiApiKeysService } from '../services/aikeys.service'
import {
  getProviderIcon,
  getProviderColor,
  type AiApiKey,
  type AiProviderInfo,
  type CreateAiApiKeyRequest,
} from '../types/aikeys.types'

const toast = useToast()

// State
const loading = ref(true)
const aiKeys = ref<AiApiKey[]>([])
const providers = ref<AiProviderInfo[]>([])
const showCreateDialog = ref(false)
const createLoading = ref(false)

// Form
const createForm = ref<CreateAiApiKeyRequest>({
  name: '',
  providerName: '',
  apiKey: '',
  description: '',
})

// Provider options for dropdown
const providerOptions = computed(() => {
  return providers.value.map((p) => ({
    label: p.displayName,
    value: p.providerName,
    description: p.description,
  }))
})

// Selected provider info
const selectedProvider = computed(() => {
  return providers.value.find((p) => p.providerName === createForm.value.providerName)
})

// Get label for selected provider value
const selectedProviderLabel = computed(() => {
  return providerOptions.value.find(p => p.value === createForm.value.providerName)?.label ?? ''
})

// Load AI keys and providers
async function loadData() {
  loading.value = true
  try {
    const [keysData, providersData] = await Promise.all([
      aiApiKeysService.list(),
      aiApiKeysService.getProviders(false),
    ])
    aiKeys.value = keysData
    providers.value = providersData
  } catch (err: unknown) {
    console.error('Failed to load AI API keys:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load AI API keys',
      life: 5000,
    })
  } finally {
    loading.value = false
  }
}

// Open create dialog
function openCreateDialog() {
  const defaultProvider = providers.value[0]?.providerName ?? ''
  createForm.value = {
    name: '',
    providerName: defaultProvider,
    apiKey: '',
    description: '',
  }
  showCreateDialog.value = true
}

// Create AI API key
async function createAiApiKey() {
  if (!createForm.value.name.trim()) {
    toast.add({
      severity: 'warn',
      summary: 'Validation Error',
      detail: 'Please enter a name',
      life: 3000,
    })
    return
  }

  if (!createForm.value.apiKey.trim()) {
    toast.add({
      severity: 'warn',
      summary: 'Validation Error',
      detail: 'Please enter an API key',
      life: 3000,
    })
    return
  }

  if (!createForm.value.providerName) {
    toast.add({
      severity: 'warn',
      summary: 'Validation Error',
      detail: 'Please select a provider',
      life: 3000,
    })
    return
  }

  createLoading.value = true
  try {
    await aiApiKeysService.create(createForm.value)
    showCreateDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'AI API key added successfully',
      life: 3000,
    })
    await loadData()
  } catch (err: unknown) {
    console.error('Failed to create AI API key:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to create AI API key',
      life: 5000,
    })
  } finally {
    createLoading.value = false
  }
}

// Delete AI API key
async function deleteAiApiKey(id: string, name: string) {
  if (
    !confirm(`Are you sure you want to delete the AI API key "${name}"? This action cannot be undone.`)
  ) {
    return
  }

  try {
    await aiApiKeysService.delete(id)
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'AI API key deleted successfully',
      life: 3000,
    })
    await loadData()
  } catch (err: unknown) {
    console.error('Failed to delete AI API key:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to delete AI API key',
      life: 5000,
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
  loadData()
})
</script>

<template>
  <Card>
    <template #title>
      <div class="flex align-items-center justify-content-between">
        <span>AI Provider Keys</span>
        <Button
          label="Add AI Key"
          icon="pi pi-plus"
          size="small"
          @click="openCreateDialog"
        />
      </div>
    </template>
    <template #subtitle> Configure API keys for AI-powered features </template>
    <template #content>
      <DataTable
        :value="aiKeys"
        :loading="loading"
        class="p-datatable-sm"
        :paginator="aiKeys.length > 10"
        :rows="10"
      >
        <template #empty>
          <div class="text-center py-5">
            <i class="pi pi-sparkles text-4xl text-color-secondary mb-3"></i>
            <p class="text-color-secondary">
              No AI API keys found. Add one to enable AI-powered features.
            </p>
          </div>
        </template>

        <Column field="name" header="Name" :sortable="true">
          <template #body="{ data }">
            <div class="flex align-items-center gap-2">
              <i :class="getProviderIcon(data.providerName)" :style="{ color: getProviderColor(data.providerName) }"></i>
              <span>{{ data.name }}</span>
            </div>
          </template>
        </Column>

        <Column field="providerName" header="Provider" :sortable="true">
          <template #body="{ data }">
            <Tag :value="data.providerName" severity="secondary" />
          </template>
        </Column>

        <Column field="description" header="Description">
          <template #body="{ data }">
            {{ data.description || '-' }}
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
              :value="data.isActive ? 'Active' : 'Inactive'"
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
                @click="deleteAiApiKey(data.id, data.name)"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <!-- Create AI API Key Dialog -->
  <Dialog
    v-model:visible="showCreateDialog"
    header="Add AI API Key"
    :modal="true"
    :style="{ width: '600px' }"
  >
    <div class="flex flex-column gap-4">
      <div class="flex flex-column gap-2">
        <label for="aiKeyProvider" class="font-medium">Provider *</label>
        <Dropdown
          id="aiKeyProvider"
          v-model="createForm.providerName"
          :options="providerOptions"
          option-label="label"
          option-value="value"
          placeholder="Select AI provider"
          class="w-full"
        >
          <template #value="slotProps">
            <div v-if="slotProps.value" class="flex align-items-center gap-2">
              <i :class="getProviderIcon(slotProps.value)" :style="{ color: getProviderColor(slotProps.value) }"></i>
              <span>{{ selectedProviderLabel }}</span>
            </div>
            <span v-else>{{ slotProps.placeholder }}</span>
          </template>
          <template #option="slotProps">
            <div class="flex align-items-center gap-2">
              <i :class="getProviderIcon(slotProps.option.value)" :style="{ color: getProviderColor(slotProps.option.value) }"></i>
              <div>
                <div>{{ slotProps.option.label }}</div>
                <small class="text-color-secondary">{{ slotProps.option.description }}</small>
              </div>
            </div>
          </template>
        </Dropdown>
        <small v-if="selectedProvider?.apiKeyHelpUrl" class="text-color-secondary">
          <a :href="selectedProvider.apiKeyHelpUrl" target="_blank" class="text-primary cursor-pointer">
            Get your {{ selectedProvider.displayName }} API key
            <i class="pi pi-external-link ml-1" style="font-size: 0.75rem"></i>
          </a>
        </small>
      </div>

      <div class="flex flex-column gap-2">
        <label for="aiKeyName" class="font-medium">Name *</label>
        <InputText
          id="aiKeyName"
          v-model="createForm.name"
          placeholder="e.g., OpenAI Production Key"
          class="w-full"
        />
      </div>

      <div class="flex flex-column gap-2">
        <label for="aiKeyApiKey" class="font-medium">API Key *</label>
        <Password
          id="aiKeyApiKey"
          v-model="createForm.apiKey"
          placeholder="sk-..."
          :feedback="false"
          toggle-mask
          class="w-full"
          input-class="w-full"
        />
        <small class="text-color-secondary">
          Your API key is encrypted and stored securely
        </small>
      </div>

      <div class="flex flex-column gap-2">
        <label for="aiKeyDescription" class="font-medium">Description</label>
        <Textarea
          id="aiKeyDescription"
          v-model="createForm.description"
          placeholder="Optional description"
          rows="2"
          class="w-full"
        />
      </div>

      <Message severity="info" :closable="false">
        This key will be used for AI-powered features like flow generation and code completion.
      </Message>
    </div>

    <template #footer>
      <Button label="Cancel" text @click="showCreateDialog = false" />
      <Button label="Add Key" :loading="createLoading" @click="createAiApiKey" />
    </template>
  </Dialog>
</template>

<style scoped>
code {
  font-size: 0.875rem;
}
</style>
