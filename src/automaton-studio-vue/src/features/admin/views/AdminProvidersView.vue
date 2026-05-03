<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
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
import type { AiProviderAdmin, CreateAiProviderRequest } from '../types/admin.types'

const router = useRouter()
const toast = useToast()
const confirm = useConfirm()

const providers = ref<AiProviderAdmin[]>([])
const loading = ref(false)
const showCreateDialog = ref(false)
const isSubmitting = ref(false)

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

const sortedProviders = computed(() =>
  [...providers.value].sort((a, b) => {
    const aReady = isReady(a)
    const bReady = isReady(b)
    if (aReady !== bReady) return aReady ? 1 : -1
    return a.sortOrder - b.sortOrder
  })
)

function isReady(p: AiProviderAdmin): boolean {
  if (!p.requiresApiKey) return true
  return p.hasSystemKey && p.hasDefaultModel
}

function readinessLabel(p: AiProviderAdmin): string {
  if (!p.requiresApiKey) return 'Ready'
  if (!p.hasSystemKey && !p.hasDefaultModel) return 'No key · No default'
  if (!p.hasSystemKey) return 'No system key'
  if (!p.hasDefaultModel) return 'No default model'
  return 'Ready'
}

function readinessSeverity(p: AiProviderAdmin): 'success' | 'warning' | 'danger' {
  if (!p.requiresApiKey) return 'success'
  if (!p.hasSystemKey && !p.hasDefaultModel) return 'danger'
  if (!p.hasSystemKey || !p.hasDefaultModel) return 'warning'
  return 'success'
}

onMounted(loadProviders)

async function loadProviders() {
  loading.value = true
  try {
    providers.value = await adminAiProvidersService.getProviders()
  } catch {
    toast.add({ severity: 'error', summary: 'Load Failed', detail: 'Failed to load AI providers', life: 3000 })
  } finally {
    loading.value = false
  }
}

function openCreateDialog() {
  formData.value = { name: '', displayName: '', description: '', baseUrl: '', isActive: true, requiresApiKey: true, environmentKeyName: '', apiKeyHelpUrl: '', iconName: '', sortOrder: 0 }
  formErrors.value = {}
  showCreateDialog.value = true
}

function validateForm(): boolean {
  formErrors.value = {}
  if (!formData.value.name?.trim()) formErrors.value.name = 'Name is required'
  if (!formData.value.displayName?.trim()) formErrors.value.displayName = 'Display name is required'
  return Object.keys(formErrors.value).length === 0
}

async function handleCreate() {
  if (!validateForm()) return
  isSubmitting.value = true
  try {
    await adminAiProvidersService.createProvider(formData.value)
    toast.add({ severity: 'success', summary: 'Created', detail: 'AI provider created', life: 3000 })
    showCreateDialog.value = false
    await loadProviders()
  } catch (error: unknown) {
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({ severity: 'error', summary: 'Creation Failed', detail: axiosError?.response?.data?.message || 'Failed to create provider', life: 3000 })
  } finally {
    isSubmitting.value = false
  }
}

function confirmDelete(provider: AiProviderAdmin) {
  confirm.require({
    message: `Delete "${provider.displayName}"? This will also delete all its models.`,
    header: 'Delete Provider',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await adminAiProvidersService.deleteProvider(provider.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'Provider deleted', life: 3000 })
        await loadProviders()
      } catch (error: unknown) {
        const axiosError = error as { response?: { data?: { message?: string } } }
        toast.add({ severity: 'error', summary: 'Delete Failed', detail: axiosError?.response?.data?.message || 'Failed to delete provider', life: 3000 })
      }
    },
  })
}

function goToDetail(provider: AiProviderAdmin) {
  router.push({ name: 'admin-provider-detail', params: { providerId: provider.id } })
}
</script>

<template>
  <div class="admin-providers-view">
    <div class="page-header mb-4">
      <div class="flex align-items-center gap-2 mb-2">
        <i class="pi pi-shield text-color-secondary"></i>
        <span class="text-color-secondary text-sm">Administration</span>
      </div>
      <div class="flex align-items-center justify-content-between">
        <div>
          <h1 class="text-2xl font-bold m-0">AI Providers</h1>
          <p class="text-color-secondary mt-1 mb-0">Configure providers, models, and system API keys</p>
        </div>
        <Button label="Add Provider" icon="pi pi-plus" @click="openCreateDialog" />
      </div>
    </div>

    <Card>
      <template #content>
        <DataTable :value="sortedProviders" :loading="loading" striped-rows responsive-layout="scroll">
          <template #empty>
            <div class="text-center py-5 text-color-secondary">
              <i class="pi pi-box text-4xl mb-3 block"></i>
              <p class="m-0">No AI providers configured yet.</p>
            </div>
          </template>

          <Column field="displayName" header="Provider" style="min-width: 200px">
            <template #body="{ data }">
              <div class="font-medium">{{ data.displayName }}</div>
              <div class="text-sm text-color-secondary">{{ data.name }}</div>
            </template>
          </Column>

          <Column field="isActive" header="Visibility" style="width: 110px">
            <template #body="{ data }">
              <Tag :value="data.isActive ? 'Visible' : 'Hidden'" :severity="data.isActive ? 'success' : 'secondary'" />
            </template>
          </Column>

          <Column header="Readiness" style="width: 200px">
            <template #body="{ data }">
              <div class="flex align-items-center gap-2">
                <Tag :value="readinessLabel(data)" :severity="readinessSeverity(data)" />
                <i
                  v-if="!isReady(data) && data.requiresApiKey"
                  class="pi pi-info-circle text-color-secondary text-sm cursor-pointer"
                  title="Click the gear icon to add a system API key and set a default model."
                />
              </div>
            </template>
          </Column>

          <Column field="modelCount" header="Models" style="width: 110px">
            <template #body="{ data }">
              <Button
                :label="`${data.activeModelCount} / ${data.modelCount}`"
                text
                size="small"
                class="p-0"
                title="Manage models"
                @click="goToDetail(data)"
              />
            </template>
          </Column>

          <Column header="Actions" style="width: 130px" align="center">
            <template #body="{ data }">
              <div class="flex gap-1 justify-content-center">
                <Button
                  icon="pi pi-pencil"
                  text
                  rounded
                  severity="warning"
                  size="small"
                  title="Edit provider settings"
                  @click="goToDetail(data)"
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
    <Dialog v-model:visible="showCreateDialog" header="Add AI Provider" modal :style="{ width: '550px' }">
      <div class="flex flex-column gap-4">
        <div class="flex flex-column gap-2">
          <label for="name" class="font-medium">Name *</label>
          <InputText id="name" v-model="formData.name" placeholder="e.g., openai" :class="{ 'p-invalid': formErrors.name }" />
          <small class="text-color-secondary">Unique identifier, cannot be changed later</small>
          <small v-if="formErrors.name" class="text-red-500">{{ formErrors.name }}</small>
        </div>
        <div class="flex flex-column gap-2">
          <label for="displayName" class="font-medium">Display Name *</label>
          <InputText id="displayName" v-model="formData.displayName" placeholder="e.g., OpenAI" :class="{ 'p-invalid': formErrors.displayName }" />
          <small v-if="formErrors.displayName" class="text-red-500">{{ formErrors.displayName }}</small>
        </div>
        <div class="flex flex-column gap-2">
          <label for="description" class="font-medium">Description</label>
          <Textarea id="description" v-model="formData.description" placeholder="Brief description" :rows="2" />
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
            <InputText id="envKeyName" v-model="formData.environmentKeyName" placeholder="e.g., OPENAI_API_KEY" />
          </div>
          <div class="flex flex-column gap-2">
            <label for="apiKeyHelpUrl" class="font-medium text-sm">API Key Help URL</label>
            <InputText id="apiKeyHelpUrl" v-model="formData.apiKeyHelpUrl" placeholder="Link to API key docs" />
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

  </div>
</template>

<style scoped>
.admin-providers-view {
  max-width: 1200px;
}
</style>