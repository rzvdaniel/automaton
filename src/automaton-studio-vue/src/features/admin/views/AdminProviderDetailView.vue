<script setup lang="ts">
/**
 * AdminProviderDetailView
 * Unified page for a single AI provider: models list + system API keys.
 * No popups for add/edit — separate detail pages are used for models.
 */
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Password from 'primevue/password'
import Divider from 'primevue/divider'
import { adminAiProvidersService } from '../services/adminAiProvidersService'
import type { AiProviderAdmin, AiModelAdmin, AiApiKeyAdmin, CreateAiApiKeyRequest, UpdateAiApiKeyRequest } from '../types/admin.types'

const route = useRoute()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()

const providerId = route.params.providerId as string

const provider = ref<AiProviderAdmin | null>(null)
const models = ref<AiModelAdmin[]>([])
const keys = ref<AiApiKeyAdmin[]>([])
const loading = ref(false)

// Key dialog state
const showAddKeyDialog = ref(false)
const showEditKeyDialog = ref(false)
const selectedKey = ref<AiApiKeyAdmin | null>(null)
const isSubmittingKey = ref(false)
const keyForm = ref<CreateAiApiKeyRequest>({ name: '', apiKey: '', description: '' })
const editKeyForm = ref<UpdateAiApiKeyRequest>({ name: '', apiKey: '', description: '', isActive: true })
const keyErrors = ref<Record<string, string>>({})

onMounted(loadAll)

async function loadAll() {
  loading.value = true
  try {
    const [prov, mods, ks] = await Promise.all([
      adminAiProvidersService.getProvider(providerId),
      adminAiProvidersService.getModelsByProvider(providerId),
      adminAiProvidersService.getProviderKeys(providerId),
    ])
    provider.value = prov
    models.value = mods
    keys.value = ks
  } catch {
    toast.add({ severity: 'error', summary: 'Load Failed', detail: 'Failed to load provider details', life: 3000 })
  } finally {
    loading.value = false
  }
}

// ---- Models ----

function goToAddModel() {
  router.push({ name: 'admin-model-detail', params: { providerId, modelId: 'new' } })
}

function goToEditModel(model: AiModelAdmin) {
  router.push({ name: 'admin-model-detail', params: { providerId, modelId: model.id } })
}

function confirmDeleteModel(model: AiModelAdmin) {
  confirm.require({
    message: `Delete model "${model.displayName}"?`,
    header: 'Delete Model',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await adminAiProvidersService.deleteModel(model.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'Model deleted', life: 3000 })
        models.value = await adminAiProvidersService.getModelsByProvider(providerId)
      } catch (error: unknown) {
        const axiosError = error as { response?: { data?: { message?: string } } }
        toast.add({ severity: 'error', summary: 'Delete Failed', detail: axiosError?.response?.data?.message || 'Failed to delete model', life: 3000 })
      }
    },
  })
}

async function setDefaultModel(model: AiModelAdmin) {
  try {
    await adminAiProvidersService.updateModel(model.id, { isDefault: true })
    toast.add({ severity: 'success', summary: 'Default Set', detail: `${model.displayName} is now the global default`, life: 3000 })
    models.value = await adminAiProvidersService.getModelsByProvider(providerId)
    if (provider.value) {
      provider.value = await adminAiProvidersService.getProvider(providerId)
    }
  } catch (error: unknown) {
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({ severity: 'error', summary: 'Failed', detail: axiosError?.response?.data?.message || 'Failed to set default', life: 3000 })
  }
}

async function setPlanningModel(model: AiModelAdmin) {
  try {
    await adminAiProvidersService.updateModel(model.id, { isPlanningModel: true })
    toast.add({ severity: 'success', summary: 'Planning Model Set', detail: `${model.displayName} is now the planning model`, life: 3000 })
    models.value = await adminAiProvidersService.getModelsByProvider(providerId)
  } catch (error: unknown) {
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({ severity: 'error', summary: 'Failed', detail: axiosError?.response?.data?.message || 'Failed to set planning model', life: 3000 })
  }
}

// ---- Keys ----

function openAddKeyDialog() {
  keyForm.value = { name: '', apiKey: '', description: '' }
  keyErrors.value = {}
  showAddKeyDialog.value = true
}

function openEditKeyDialog(key: AiApiKeyAdmin) {
  selectedKey.value = key
  editKeyForm.value = { name: key.name, description: key.description, isActive: key.isActive, apiKey: '' }
  keyErrors.value = {}
  showEditKeyDialog.value = true
}

function validateKeyForm(): boolean {
  keyErrors.value = {}
  if (!keyForm.value.name?.trim()) keyErrors.value.name = 'Name is required'
  if (!keyForm.value.apiKey?.trim()) keyErrors.value.apiKey = 'API key is required'
  return Object.keys(keyErrors.value).length === 0
}

async function handleAddKey() {
  if (!validateKeyForm()) return
  isSubmittingKey.value = true
  try {
    await adminAiProvidersService.createProviderKey(providerId, keyForm.value)
    toast.add({ severity: 'success', summary: 'Key Added', detail: 'System API key added', life: 3000 })
    showAddKeyDialog.value = false
    keys.value = await adminAiProvidersService.getProviderKeys(providerId)
    provider.value = await adminAiProvidersService.getProvider(providerId)
  } catch (error: unknown) {
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({ severity: 'error', summary: 'Failed', detail: axiosError?.response?.data?.message || 'Failed to add key', life: 3000 })
  } finally {
    isSubmittingKey.value = false
  }
}

async function handleUpdateKey() {
  if (!selectedKey.value) return
  isSubmittingKey.value = true
  try {
    await adminAiProvidersService.updateApiKey(selectedKey.value.id, editKeyForm.value)
    toast.add({ severity: 'success', summary: 'Updated', detail: 'Key updated', life: 3000 })
    showEditKeyDialog.value = false
    keys.value = await adminAiProvidersService.getProviderKeys(providerId)
    provider.value = await adminAiProvidersService.getProvider(providerId)
  } catch (error: unknown) {
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({ severity: 'error', summary: 'Failed', detail: axiosError?.response?.data?.message || 'Failed to update key', life: 3000 })
  } finally {
    isSubmittingKey.value = false
  }
}

function confirmDeleteKey(key: AiApiKeyAdmin) {
  confirm.require({
    message: `Delete API key "${key.name}"?`,
    header: 'Delete Key',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await adminAiProvidersService.deleteApiKey(key.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'Key deleted', life: 3000 })
        keys.value = await adminAiProvidersService.getProviderKeys(providerId)
        provider.value = await adminAiProvidersService.getProvider(providerId)
      } catch (error: unknown) {
        const axiosError = error as { response?: { data?: { message?: string } } }
        toast.add({ severity: 'error', summary: 'Delete Failed', detail: axiosError?.response?.data?.message || 'Failed to delete key', life: 3000 })
      }
    },
  })
}

function formatDate(dateStr?: string): string {
  if (!dateStr) return '—'
  return new Date(dateStr).toLocaleDateString()
}
</script>

<template>
  <div class="admin-provider-detail">
    <!-- Breadcrumb / header -->
    <div class="page-header mb-4">
      <div class="flex align-items-center gap-2 mb-2">
        <Button
          icon="pi pi-arrow-left"
          text
          rounded
          size="small"
          title="Back to providers"
          @click="router.push({ name: 'admin-providers' })"
        />
        <i class="pi pi-shield text-color-secondary"></i>
        <span class="text-color-secondary text-sm">Administration / AI Providers</span>
      </div>
      <div class="flex align-items-center justify-content-between">
        <div>
          <h1 class="text-2xl font-bold m-0">
            {{ provider?.displayName ?? 'Provider' }}
          </h1>
          <p class="text-color-secondary mt-1 mb-0">
            Manage models and system API keys for this provider
          </p>
        </div>
      </div>
    </div>

    <!-- System API Keys section -->
    <Card class="mb-4">
      <template #header>
        <div class="flex align-items-center justify-content-between px-4 pt-3 pb-0">
          <div>
            <h2 class="text-lg font-semibold m-0">System API Keys</h2>
            <p class="text-color-secondary text-sm mt-1 mb-0">
              Keys added here are available to all users. At least one active key is required for this provider to work.
            </p>
          </div>
          <Button
            v-if="provider?.requiresApiKey"
            label="Add Key"
            icon="pi pi-plus"
            size="small"
            @click="openAddKeyDialog"
          />
        </div>
      </template>
      <template #content>
        <div v-if="!provider?.requiresApiKey" class="text-color-secondary text-sm py-2">
          This provider does not require an API key.
        </div>
        <DataTable v-else :value="keys" :loading="loading" striped-rows responsive-layout="scroll">
          <template #empty>
            <div class="text-center py-4 text-color-secondary">
              <i class="pi pi-key text-3xl mb-2 block"></i>
              <p class="m-0 font-medium">No system keys yet.</p>
              <p class="m-0 text-sm">Add one so all users can use this provider.</p>
            </div>
          </template>
          <Column field="name" header="Name" style="min-width: 160px" />
          <Column field="description" header="Description" style="min-width: 180px">
            <template #body="{ data }">
              <span class="text-color-secondary text-sm">{{ data.description || '—' }}</span>
            </template>
          </Column>
          <Column field="isActive" header="Status" style="width: 100px">
            <template #body="{ data }">
              <Tag :value="data.isActive ? 'Active' : 'Inactive'" :severity="data.isActive ? 'success' : 'secondary'" />
            </template>
          </Column>
          <Column field="createdAt" header="Added" style="width: 120px">
            <template #body="{ data }">
              <span class="text-sm text-color-secondary">{{ formatDate(data.createdAt) }}</span>
            </template>
          </Column>
          <Column field="lastUsedAt" header="Last Used" style="width: 120px">
            <template #body="{ data }">
              <span class="text-sm text-color-secondary">{{ formatDate(data.lastUsedAt) }}</span>
            </template>
          </Column>
          <Column header="Actions" style="width: 100px" align="center">
            <template #body="{ data }">
              <div class="flex gap-1 justify-content-center">
                <Button icon="pi pi-pencil" text rounded severity="warning" size="small" title="Edit key" @click="openEditKeyDialog(data)" />
                <Button icon="pi pi-trash" text rounded severity="danger" size="small" title="Delete key" @click="confirmDeleteKey(data)" />
              </div>
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <!-- Models section -->
    <Card>
      <template #header>
        <div class="flex align-items-center justify-content-between px-4 pt-3 pb-0">
          <div>
            <h2 class="text-lg font-semibold m-0">Models</h2>
            <p class="text-color-secondary text-sm mt-1 mb-0">
              Active models are shown in chat selectors. Exactly one model should be the global default.
            </p>
          </div>
          <Button label="Add Model" icon="pi pi-plus" size="small" @click="goToAddModel" />
        </div>
      </template>
      <template #content>
        <DataTable :value="models" :loading="loading" striped-rows responsive-layout="scroll" sort-field="sortOrder" :sort-order="1">
          <template #empty>
            <div class="text-center py-4 text-color-secondary">
              <i class="pi pi-list text-3xl mb-2 block"></i>
              <p class="m-0">No models configured yet.</p>
            </div>
          </template>

          <Column field="displayName" header="Model" style="min-width: 180px">
            <template #body="{ data }">
              <div class="font-medium">{{ data.displayName }}</div>
              <div class="text-sm text-color-secondary font-mono">{{ data.modelId }}</div>
            </template>
          </Column>

          <Column header="Badges" style="min-width: 200px">
            <template #body="{ data }">
              <div class="flex flex-wrap gap-1">
                <Tag v-if="data.isDefault" value="Global Default" severity="success" />
                <Tag v-if="data.isPlanningModel" value="Planning" severity="info" />
                <Tag v-if="!data.isActive" value="Inactive" severity="secondary" />
              </div>
            </template>
          </Column>

          <Column header="Capabilities" style="width: 160px">
            <template #body="{ data }">
              <div class="flex gap-1">
                <i v-if="data.supportsStreaming" class="pi pi-bolt text-yellow-500 text-sm" title="Streaming" />
                <i v-if="data.supportsTools" class="pi pi-wrench text-blue-400 text-sm" title="Tool use" />
                <i v-if="data.supportsVision" class="pi pi-image text-purple-400 text-sm" title="Vision" />
              </div>
            </template>
          </Column>

          <Column header="Actions" style="width: 180px" align="center">
            <template #body="{ data }">
              <div class="flex gap-1 justify-content-center">
                <Button
                  v-if="!data.isDefault"
                  icon="pi pi-star"
                  text rounded severity="success" size="small"
                  title="Set as global default"
                  @click="setDefaultModel(data)"
                />
                <Button
                  v-if="!data.isPlanningModel"
                  icon="pi pi-map"
                  text rounded severity="info" size="small"
                  title="Set as planning model"
                  @click="setPlanningModel(data)"
                />
                <Button icon="pi pi-pencil" text rounded severity="warning" size="small" title="Edit model" @click="goToEditModel(data)" />
                <Button icon="pi pi-trash" text rounded severity="danger" size="small" title="Delete model" @click="confirmDeleteModel(data)" />
              </div>
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <!-- Add Key Dialog -->
    <Dialog v-model:visible="showAddKeyDialog" header="Add System API Key" modal :style="{ width: '480px' }">
      <div class="flex flex-column gap-4">
        <div class="p-message p-message-info p-3 border-round text-sm">
          <i class="pi pi-info-circle mr-2"></i>
          This key will be used for all users. Keep it secret — it is stored encrypted.
        </div>
        <div class="flex flex-column gap-2">
          <label for="keyName" class="font-medium">Name *</label>
          <InputText id="keyName" v-model="keyForm.name" placeholder="e.g., Production key" :class="{ 'p-invalid': keyErrors.name }" />
          <small v-if="keyErrors.name" class="text-red-500">{{ keyErrors.name }}</small>
        </div>
        <div class="flex flex-column gap-2">
          <label for="keyValue" class="font-medium">API Key *</label>
          <Password id="keyValue" v-model="keyForm.apiKey" placeholder="sk-..." :feedback="false" :class="{ 'p-invalid': keyErrors.apiKey }" />
          <small v-if="keyErrors.apiKey" class="text-red-500">{{ keyErrors.apiKey }}</small>
        </div>
        <div class="flex flex-column gap-2">
          <label for="keyDesc" class="font-medium">Description</label>
          <Textarea id="keyDesc" v-model="keyForm.description" placeholder="Optional note" :rows="2" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text :disabled="isSubmittingKey" @click="showAddKeyDialog = false" />
        <Button label="Add Key" :loading="isSubmittingKey" @click="handleAddKey" />
      </template>
    </Dialog>

    <!-- Edit Key Dialog -->
    <Dialog v-model:visible="showEditKeyDialog" :header="`Edit Key: ${selectedKey?.name}`" modal :style="{ width: '480px' }">
      <div class="flex flex-column gap-4">
        <div class="flex flex-column gap-2">
          <label for="editKeyName" class="font-medium">Name</label>
          <InputText id="editKeyName" v-model="editKeyForm.name" />
        </div>
        <div class="flex flex-column gap-2">
          <label for="editKeyValue" class="font-medium">New API Key</label>
          <Password id="editKeyValue" v-model="editKeyForm.apiKey" placeholder="Leave blank to keep current" :feedback="false" />
          <small class="text-color-secondary">Leave blank to keep the existing key value.</small>
        </div>
        <div class="flex flex-column gap-2">
          <label for="editKeyDesc" class="font-medium">Description</label>
          <Textarea id="editKeyDesc" v-model="editKeyForm.description" :rows="2" />
        </div>
        <div class="flex align-items-center gap-2">
          <input id="editKeyActive" type="checkbox" v-model="editKeyForm.isActive" />
          <label for="editKeyActive">Active</label>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text :disabled="isSubmittingKey" @click="showEditKeyDialog = false" />
        <Button label="Save" :loading="isSubmittingKey" @click="handleUpdateKey" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.admin-provider-detail {
  max-width: 1200px;
}
</style>