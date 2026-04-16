<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import InputNumber from 'primevue/inputnumber'
import Tag from 'primevue/tag'
import { adminAiProvidersService } from '../services/adminAiProvidersService'
import type {
  AiProviderAdmin,
  CreateAiProviderRequest,
  UpdateAiProviderRequest,
} from '../types/admin.types'

const router = useRouter()
const toast = useToast()
const confirm = useConfirm()

// State
const providers = ref<AiProviderAdmin[]>([])
const loading = ref(false)
const showCreateDialog = ref(false)
const showEditDialog = ref(false)
const selectedProvider = ref<AiProviderAdmin | null>(null)
const isSubmitting = ref(false)

// Form
const formData = ref<CreateAiProviderRequest>({
  name: '',
  displayName: '',
  description: '',
  baseUrl: '',
  isActive: true,
  requiresApiKey: true,
  environmentKeyName: '',
  apiKeyHelpUrl: '',
  iconName: '',
  sortOrder: 0,
})

const formErrors = ref<Record<string, string>>({})

onMounted(async () => {
  await loadProviders()
})

async function loadProviders() {
  loading.value = true
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
  } finally {
    loading.value = false
  }
}

function openCreateDialog() {
  formData.value = {
    name: '',
    displayName: '',
    description: '',
    baseUrl: '',
    isActive: true,
    requiresApiKey: true,
    environmentKeyName: '',
    apiKeyHelpUrl: '',
    iconName: '',
    sortOrder: 0,
  }
  formErrors.value = {}
  showCreateDialog.value = true
}

function openEditDialog(provider: AiProviderAdmin) {
  selectedProvider.value = provider
  formData.value = {
    name: provider.name,
    displayName: provider.displayName,
    description: provider.description,
    baseUrl: provider.baseUrl,
    isActive: provider.isActive,
    requiresApiKey: provider.requiresApiKey,
    environmentKeyName: provider.environmentKeyName,
    apiKeyHelpUrl: provider.apiKeyHelpUrl,
    iconName: provider.iconName,
    sortOrder: provider.sortOrder,
  }
  formErrors.value = {}
  showEditDialog.value = true
}

function validateForm(): boolean {
  formErrors.value = {}

  if (!formData.value.name?.trim()) {
    formErrors.value.name = 'Name is required'
  }
  if (!formData.value.displayName?.trim()) {
    formErrors.value.displayName = 'Display name is required'
  }

  return Object.keys(formErrors.value).length === 0
}

async function handleCreate() {
  if (!validateForm()) return

  isSubmitting.value = true
  try {
    await adminAiProvidersService.createProvider(formData.value)
    toast.add({
      severity: 'success',
      summary: 'Created',
      detail: 'AI provider created successfully',
      life: 3000,
    })
    showCreateDialog.value = false
    await loadProviders()
  } catch (error: unknown) {
    console.error('Error creating provider:', error)
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({
      severity: 'error',
      summary: 'Creation Failed',
      detail: axiosError?.response?.data?.message || 'Failed to create provider',
      life: 3000,
    })
  } finally {
    isSubmitting.value = false
  }
}

async function handleUpdate() {
  if (!selectedProvider.value || !validateForm()) return

  isSubmitting.value = true
  try {
    const updateRequest: UpdateAiProviderRequest = {
      displayName: formData.value.displayName,
      description: formData.value.description,
      baseUrl: formData.value.baseUrl,
      isActive: formData.value.isActive,
      requiresApiKey: formData.value.requiresApiKey,
      environmentKeyName: formData.value.environmentKeyName,
      apiKeyHelpUrl: formData.value.apiKeyHelpUrl,
      iconName: formData.value.iconName,
      sortOrder: formData.value.sortOrder,
    }

    await adminAiProvidersService.updateProvider(selectedProvider.value.id, updateRequest)
    toast.add({
      severity: 'success',
      summary: 'Updated',
      detail: 'AI provider updated successfully',
      life: 3000,
    })
    showEditDialog.value = false
    await loadProviders()
  } catch (error: unknown) {
    console.error('Error updating provider:', error)
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({
      severity: 'error',
      summary: 'Update Failed',
      detail: axiosError?.response?.data?.message || 'Failed to update provider',
      life: 3000,
    })
  } finally {
    isSubmitting.value = false
  }
}

function confirmDelete(provider: AiProviderAdmin) {
  confirm.require({
    message: `Are you sure you want to delete "${provider.displayName}"? This will also delete all its models.`,
    header: 'Delete Provider',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await adminAiProvidersService.deleteProvider(provider.id)
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: 'AI provider deleted successfully',
          life: 3000,
        })
        await loadProviders()
      } catch (error: unknown) {
        console.error('Error deleting provider:', error)
        const axiosError = error as { response?: { data?: { message?: string } } }
        toast.add({
          severity: 'error',
          summary: 'Deletion Failed',
          detail: axiosError?.response?.data?.message || 'Failed to delete provider',
          life: 3000,
        })
      }
    },
  })
}

function viewModels(provider: AiProviderAdmin) {
  router.push({
    name: 'admin-models',
    params: { providerId: provider.id },
  })
}

function getStatusSeverity(isActive: boolean) {
  return isActive ? 'success' : 'warning'
}

async function toggleProviderVisibility(provider: AiProviderAdmin) {
  try {
    const updateRequest: UpdateAiProviderRequest = {
      isActive: !provider.isActive,
    }

    await adminAiProvidersService.updateProvider(provider.id, updateRequest)
    toast.add({
      severity: 'success',
      summary: provider.isActive ? 'Provider hidden' : 'Provider shown',
      detail: `${provider.displayName} is now ${provider.isActive ? 'hidden' : 'visible'} in chat selectors.`,
      life: 3000,
    })
    await loadProviders()
  } catch (error: unknown) {
    console.error('Error toggling provider visibility:', error)
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({
      severity: 'error',
      summary: 'Update Failed',
      detail: axiosError?.response?.data?.message || 'Failed to update provider visibility',
      life: 3000,
    })
  }
}
</script>

<template>
  <div class="admin-providers-view">
    <!-- Page Header -->
    <div class="page-header mb-4">
      <div class="flex align-items-center gap-2 mb-2">
        <i class="pi pi-shield text-color-secondary"></i>
        <span class="text-color-secondary text-sm">Administration</span>
      </div>
      <div class="flex align-items-center justify-content-between">
        <div>
          <h1 class="text-2xl font-bold m-0">AI Providers</h1>
          <p class="text-color-secondary mt-1 mb-0">
            Configure AI providers and manage their models
          </p>
        </div>
        <Button label="Add Provider" icon="pi pi-plus" @click="openCreateDialog" />
      </div>
    </div>

    <!-- Providers Table -->
    <Card>
      <template #content>
        <DataTable
          :value="providers"
          :loading="loading"
          striped-rows
          responsive-layout="scroll"
          sort-field="sortOrder"
          :sort-order="1"
        >
          <template #empty>
            <div class="text-center py-5 text-color-secondary">
              <i class="pi pi-box text-4xl mb-3 block"></i>
              <p class="m-0">No AI providers configured yet.</p>
            </div>
          </template>

          <Column field="displayName" header="Provider" style="min-width: 200px">
            <template #body="{ data }">
              <div class="flex align-items-center gap-3">
                <div>
                  <div class="font-medium">{{ data.displayName }}</div>
                  <div class="text-sm text-color-secondary">{{ data.name }}</div>
                </div>
              </div>
            </template>
          </Column>

          <Column field="description" header="Description" style="min-width: 200px">
            <template #body="{ data }">
              <span class="text-color-secondary text-sm">{{ data.description || '—' }}</span>
            </template>
          </Column>

          <Column field="isActive" header="Status" style="width: 100px">
            <template #body="{ data }">
                <Tag
                  :value="data.isActive ? 'Visible' : 'Hidden'"
                  :severity="getStatusSeverity(data.isActive)"
                />
              </template>
            </Column>

          <Column field="modelCount" header="Models" style="width: 120px">
            <template #body="{ data }">
              <Button
                :label="`${data.activeModelCount} / ${data.modelCount}`"
                text
                size="small"
                class="p-0"
                title="Manage models"
                @click="viewModels(data)"
              />
            </template>
          </Column>

          <Column header="Actions" style="width: 150px" align="center">
            <template #body="{ data }">
              <div class="flex gap-1 justify-content-center">
                <Button
                  icon="pi pi-pencil"
                  text
                  rounded
                  severity="warning"
                  size="small"
                  title="Edit provider"
                  @click="openEditDialog(data)"
                />
                <Button
                  icon="pi pi-list"
                  text
                  rounded
                  severity="info"
                  size="small"
                  title="Manage models"
                  @click="viewModels(data)"
                />
                <Button
                  :icon="data.isActive ? 'pi pi-eye' : 'pi pi-eye-slash'"
                  text
                  rounded
                  :severity="data.isActive ? 'success' : 'secondary'"
                  size="small"
                  :title="data.isActive ? 'Hide provider from chat selectors' : 'Show provider in chat selectors'"
                  @click="toggleProviderVisibility(data)"
                />
                <Button
                  icon="pi pi-trash"
                  text
                  rounded
                  severity="danger"
                  size="small"
                  title="Delete provider"
                  @click="confirmDelete(data)"
                />
              </div>
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <!-- Create Provider Dialog -->
    <Dialog
      v-model:visible="showCreateDialog"
      header="Add AI Provider"
      modal
      :style="{ width: '550px' }"
    >
      <div class="flex flex-column gap-4">
        <div class="flex flex-column gap-2">
          <label for="name" class="font-medium">Name *</label>
          <InputText
            id="name"
            v-model="formData.name"
            placeholder="e.g., openai"
            :class="{ 'p-invalid': formErrors.name }"
          />
          <small class="text-color-secondary">Unique identifier, cannot be changed later</small>
          <small v-if="formErrors.name" class="text-red-500">{{ formErrors.name }}</small>
        </div>

        <div class="flex flex-column gap-2">
          <label for="displayName" class="font-medium">Display Name *</label>
          <InputText
            id="displayName"
            v-model="formData.displayName"
            placeholder="e.g., OpenAI"
            :class="{ 'p-invalid': formErrors.displayName }"
          />
          <small v-if="formErrors.displayName" class="text-red-500">{{
            formErrors.displayName
          }}</small>
        </div>

        <div class="flex flex-column gap-2">
          <label for="description" class="font-medium">Description</label>
          <Textarea
            id="description"
            v-model="formData.description"
            placeholder="Brief description"
            rows="2"
          />
        </div>

        <div class="flex flex-column gap-2">
          <label for="baseUrl" class="font-medium">Base URL</label>
          <InputText id="baseUrl" v-model="formData.baseUrl" placeholder="https://api.example.com" />
        </div>

        <div class="flex align-items-center gap-2">
          <Checkbox id="providerIsActive" v-model="formData.isActive" :binary="true" />
          <label for="providerIsActive">Visible in chat selectors</label>
        </div>

        <div class="flex align-items-center gap-2">
          <Checkbox id="requiresApiKey" v-model="formData.requiresApiKey" :binary="true" />
          <label for="requiresApiKey">Requires API Key</label>
        </div>

        <div v-if="formData.requiresApiKey" class="flex flex-column gap-3 pl-4">
          <div class="flex flex-column gap-2">
            <label for="envKeyName" class="font-medium text-sm">Environment Key Name</label>
            <InputText
              id="envKeyName"
              v-model="formData.environmentKeyName"
              placeholder="e.g., OPENAI_API_KEY"
            />
          </div>
          <div class="flex flex-column gap-2">
            <label for="apiKeyHelpUrl" class="font-medium text-sm">API Key Help URL</label>
            <InputText
              id="apiKeyHelpUrl"
              v-model="formData.apiKeyHelpUrl"
              placeholder="Link to API key documentation"
            />
          </div>
        </div>

        <div class="flex gap-4">
          <div class="flex flex-column gap-2 flex-1">
            <label for="iconName" class="font-medium">Icon Name</label>
            <InputText id="iconName" v-model="formData.iconName" placeholder="e.g., openai" />
          </div>
          <div class="flex flex-column gap-2" style="width: 120px">
            <label for="sortOrder" class="font-medium">Sort Order</label>
            <InputNumber id="sortOrder" v-model="formData.sortOrder" :min="0" />
          </div>
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" text :disabled="isSubmitting" @click="showCreateDialog = false" />
        <Button label="Create" :loading="isSubmitting" @click="handleCreate" />
      </template>
    </Dialog>

    <!-- Edit Provider Dialog -->
    <Dialog
      v-model:visible="showEditDialog"
      header="Edit AI Provider"
      modal
      :style="{ width: '550px' }"
    >
      <div class="flex flex-column gap-4">
        <div class="flex flex-column gap-2">
          <label class="font-medium">Name</label>
          <InputText :model-value="formData.name" disabled />
          <small class="text-color-secondary">Cannot be changed after creation</small>
        </div>

        <div class="flex flex-column gap-2">
          <label for="editDisplayName" class="font-medium">Display Name *</label>
          <InputText
            id="editDisplayName"
            v-model="formData.displayName"
            :class="{ 'p-invalid': formErrors.displayName }"
          />
          <small v-if="formErrors.displayName" class="text-red-500">{{
            formErrors.displayName
          }}</small>
        </div>

        <div class="flex flex-column gap-2">
          <label for="editDescription" class="font-medium">Description</label>
          <Textarea id="editDescription" v-model="formData.description" rows="2" />
        </div>

        <div class="flex flex-column gap-2">
          <label for="editBaseUrl" class="font-medium">Base URL</label>
          <InputText id="editBaseUrl" v-model="formData.baseUrl" />
        </div>

        <div class="flex align-items-center gap-2">
          <Checkbox id="editProviderIsActive" v-model="formData.isActive" :binary="true" />
          <label for="editProviderIsActive">Visible in chat selectors</label>
        </div>

        <div class="flex align-items-center gap-2">
          <Checkbox id="editRequiresApiKey" v-model="formData.requiresApiKey" :binary="true" />
          <label for="editRequiresApiKey">Requires API Key</label>
        </div>

        <div v-if="formData.requiresApiKey" class="flex flex-column gap-3 pl-4">
          <div class="flex flex-column gap-2">
            <label for="editEnvKeyName" class="font-medium text-sm">Environment Key Name</label>
            <InputText id="editEnvKeyName" v-model="formData.environmentKeyName" />
          </div>
          <div class="flex flex-column gap-2">
            <label for="editApiKeyHelpUrl" class="font-medium text-sm">API Key Help URL</label>
            <InputText id="editApiKeyHelpUrl" v-model="formData.apiKeyHelpUrl" />
          </div>
        </div>

        <div class="flex gap-4">
          <div class="flex flex-column gap-2 flex-1">
            <label for="editIconName" class="font-medium">Icon Name</label>
            <InputText id="editIconName" v-model="formData.iconName" />
          </div>
          <div class="flex flex-column gap-2" style="width: 120px">
            <label for="editSortOrder" class="font-medium">Sort Order</label>
            <InputNumber id="editSortOrder" v-model="formData.sortOrder" :min="0" />
          </div>
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" text :disabled="isSubmitting" @click="showEditDialog = false" />
        <Button label="Save" :loading="isSubmitting" @click="handleUpdate" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.admin-providers-view {
  max-width: 1200px;
}
</style>
