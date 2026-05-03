<script setup lang="ts">
/**
 * AdminProviderDetailView
 * Three-tab page for a single AI provider: Details | System API Keys | Models.
 * Replaces both the "Edit provider settings" popup and the old keys-only section.
 */
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import InputNumber from 'primevue/inputnumber'
import Password from 'primevue/password'
import Divider from 'primevue/divider'
import { adminAiProvidersService } from '../services/adminAiProvidersService'
import type {
  AiProviderAdmin,
  AiModelAdmin,
  AiApiKeyAdmin,
  CreateAiProviderRequest,
  UpdateAiProviderRequest,
  CreateAiApiKeyRequest,
  UpdateAiApiKeyRequest,
} from '../types/admin.types'

const route = useRoute()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()

const providerId = route.params.providerId as string
const isNew = providerId === 'new'

const pageTitle = computed(() => isNew ? 'Add Provider' : (provider.value?.displayName ?? 'Provider'))

// Active tab (0=Details, 1=Models, 2=API Keys)
const activeTab = ref(0)

const provider = ref<AiProviderAdmin | null>(null)
const models = ref<AiModelAdmin[]>([])
const keys = ref<AiApiKeyAdmin[]>([])
const loading = ref(false)

// ---- Details tab / Create form ----
// For new providers: name is editable; for existing: populated from provider
const detailsForm = ref<CreateAiProviderRequest>({
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
const detailsErrors = ref<Record<string, string>>({})
const isSavingDetails = ref(false)

// ---- Keys tab ----
const showAddKeyDialog = ref(false)
const showEditKeyDialog = ref(false)
const selectedKey = ref<AiApiKeyAdmin | null>(null)
const isSubmittingKey = ref(false)
const keyForm = ref<CreateAiApiKeyRequest>({ name: '', apiKey: '', description: '' })
const editKeyForm = ref<UpdateAiApiKeyRequest>({ name: '', apiKey: '', description: '', isActive: true })
const keyErrors = ref<Record<string, string>>({})

onMounted(async () => {
  if (isNew) return  // form already initialised with defaults
  loadAll()
})

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
    populateDetailsForm(prov)
  } catch {
    toast.add({ severity: 'error', summary: 'Load Failed', detail: 'Failed to load provider details', life: 3000 })
  } finally {
    loading.value = false
  }
}

function populateDetailsForm(p: AiProviderAdmin) {
  detailsForm.value = {
    name: p.name,
    displayName: p.displayName,
    description: p.description,
    baseUrl: p.baseUrl,
    isActive: p.isActive,
    requiresApiKey: p.requiresApiKey,
    environmentKeyName: p.environmentKeyName,
    apiKeyHelpUrl: p.apiKeyHelpUrl,
    iconName: p.iconName,
    sortOrder: p.sortOrder,
  }
}

// ---- Details ----

function validateDetails(): boolean {
  detailsErrors.value = {}
  if (isNew && !detailsForm.value.name?.trim()) detailsErrors.value.name = 'Name is required'
  if (!detailsForm.value.displayName?.trim()) detailsErrors.value.displayName = 'Display name is required'
  return Object.keys(detailsErrors.value).length === 0
}

async function saveDetails() {
  if (!validateDetails()) return
  isSavingDetails.value = true
  try {
    if (isNew) {
      const created = await adminAiProvidersService.createProvider(detailsForm.value)
      toast.add({ severity: 'success', summary: 'Created', detail: 'Provider created', life: 3000 })
      router.replace({ name: 'admin-provider-detail', params: { providerId: created.id } })
    } else {
      await adminAiProvidersService.updateProvider(providerId, detailsForm.value)
      toast.add({ severity: 'success', summary: 'Saved', detail: 'Provider settings saved', life: 3000 })
      provider.value = await adminAiProvidersService.getProvider(providerId)
    }
  } catch (error: unknown) {
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({ severity: 'error', summary: isNew ? 'Create Failed' : 'Save Failed', detail: axiosError?.response?.data?.message || 'Operation failed', life: 3000 })
  } finally {
    isSavingDetails.value = false
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
    provider.value = await adminAiProvidersService.getProvider(providerId)
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
          text rounded size="small"
          title="Back to providers"
          @click="router.push({ name: 'admin-providers' })"
        />
        <i class="pi pi-shield text-color-secondary"></i>
        <span class="text-color-secondary text-sm">Administration / AI Providers</span>
      </div>
      <h1 class="text-2xl font-bold m-0">
        {{ pageTitle }}
      </h1>
      <p v-if="!isNew" class="text-color-secondary mt-1 mb-0">{{ provider?.name }}</p>
    </div>

    <div v-if="loading" class="flex justify-content-center py-6">
      <i class="pi pi-spin pi-spinner text-3xl text-color-secondary" />
    </div>

    <Card v-else>
      <template #content>
        <TabView v-model:activeIndex="activeTab">

          <!-- ── Tab 1: Details ─────────────────────────────── -->
          <TabPanel header="Details">
            <div class="flex flex-column gap-4 pt-2" style="max-width: 600px">
              <div v-if="isNew" class="flex flex-column gap-2">
                <label for="providerName" class="font-medium">Name *</label>
                <InputText id="providerName" v-model="detailsForm.name" placeholder="e.g., openai" :class="{ 'p-invalid': detailsErrors.name }" />
                <small class="text-color-secondary">Unique internal identifier — cannot be changed later</small>
                <small v-if="detailsErrors.name" class="text-red-500">{{ detailsErrors.name }}</small>
              </div>
              <div v-else class="flex flex-column gap-2">
                <label class="font-medium">Name</label>
                <InputText :model-value="provider?.name" disabled />
                <small class="text-color-secondary">Cannot be changed after creation</small>
              </div>

              <div class="flex flex-column gap-2">
                <label for="displayName" class="font-medium">Display Name *</label>
                <InputText
                  id="displayName"
                  v-model="detailsForm.displayName"
                  :class="{ 'p-invalid': detailsErrors.displayName }"
                />
                <small v-if="detailsErrors.displayName" class="text-red-500">{{ detailsErrors.displayName }}</small>
              </div>

              <div class="flex flex-column gap-2">
                <label for="description" class="font-medium">Description</label>
                <Textarea id="description" v-model="detailsForm.description" :rows="2" />
              </div>

              <div class="flex flex-column gap-2">
                <label for="baseUrl" class="font-medium">Base URL</label>
                <InputText id="baseUrl" v-model="detailsForm.baseUrl" placeholder="https://api.example.com" />
              </div>

              <div class="flex align-items-center gap-2">
                <Checkbox id="isActive" v-model="detailsForm.isActive" :binary="true" />
                <label for="isActive">Visible in chat selectors</label>
              </div>

              <div class="flex align-items-center gap-2">
                <Checkbox id="requiresApiKey" v-model="detailsForm.requiresApiKey" :binary="true" />
                <label for="requiresApiKey">Requires API Key</label>
              </div>

              <div v-if="detailsForm.requiresApiKey" class="flex flex-column gap-3 pl-4">
                <div class="flex flex-column gap-2">
                  <label for="envKeyName" class="font-medium text-sm">Environment Key Name</label>
                  <InputText id="envKeyName" v-model="detailsForm.environmentKeyName" placeholder="e.g., OPENAI_API_KEY" />
                </div>
                <div class="flex flex-column gap-2">
                  <label for="apiKeyHelpUrl" class="font-medium text-sm">API Key Help URL</label>
                  <InputText id="apiKeyHelpUrl" v-model="detailsForm.apiKeyHelpUrl" placeholder="Link to API key docs" />
                </div>
              </div>

              <div class="flex gap-4">
                <div class="flex flex-column gap-2 flex-1">
                  <label for="iconName" class="font-medium">Icon Name</label>
                  <InputText id="iconName" v-model="detailsForm.iconName" placeholder="e.g., openai" />
                </div>
                <div class="flex flex-column gap-2" style="width: 140px">
                  <label for="sortOrder" class="font-medium">Sort Order</label>
                  <InputNumber id="sortOrder" v-model="detailsForm.sortOrder" :min="0" />
                </div>
              </div>

              <div class="flex justify-content-end gap-2 pt-2">
                <Button
                  label="Cancel"
                  text
                  :disabled="isSavingDetails"
                  @click="isNew ? router.push({ name: 'admin-providers' }) : populateDetailsForm(provider!)"
                />
                <Button
                  :label="isNew ? 'Create Provider' : 'Save Details'"
                  icon="pi pi-check"
                  :loading="isSavingDetails"
                  @click="saveDetails"
                />
              </div>
            </div>
          </TabPanel>

          <!-- ── Tab 2: Models ────────────────────────────────── -->
          <TabPanel header="Models">
            <div v-if="isNew" class="flex flex-column align-items-center justify-content-center py-6 gap-3 text-color-secondary">
              <i class="pi pi-list text-4xl" />
              <p class="m-0 font-medium">Create the provider first</p>
              <p class="m-0 text-sm">Fill in the Details tab and click <strong>Create Provider</strong>, then come back here to add models.</p>
            </div>
            <div class="pt-2">
              <div class="flex align-items-center justify-content-between mb-3">
                <p class="text-color-secondary text-sm m-0">
                  Active models appear in chat selectors. Set one as the global default so the system knows which model to use.
                </p>
                <Button label="Add Model" icon="pi pi-plus" size="small" @click="goToAddModel" />
              </div>

              <DataTable :value="models" striped-rows responsive-layout="scroll" sort-field="sortOrder" :sort-order="1">
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

                <Column header="Capabilities" style="width: 130px">
                  <template #body="{ data }">
                    <div class="flex gap-2">
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
            </div>
          </TabPanel>

          <!-- ── Tab 3: API Keys ──────────────────────────────── -->
          <TabPanel header="API Keys">
            <div v-if="isNew" class="flex flex-column align-items-center justify-content-center py-6 gap-3 text-color-secondary">
              <i class="pi pi-key text-4xl" />
              <p class="m-0 font-medium">Create the provider first</p>
              <p class="m-0 text-sm">Fill in the Details tab and click <strong>Create Provider</strong>, then come back here to add API keys.</p>
            </div>
            <div class="pt-2">
              <div class="flex align-items-center justify-content-between mb-3">
                <p class="text-color-secondary text-sm m-0">
                  Keys added here are available to all users. At least one active key is required.
                </p>
                <Button
                  v-if="provider?.requiresApiKey"
                  label="Add Key"
                  icon="pi pi-plus"
                  size="small"
                  @click="openAddKeyDialog"
                />
              </div>

              <div v-if="!provider?.requiresApiKey" class="text-color-secondary text-sm py-2">
                This provider does not require an API key.
              </div>

              <DataTable v-else :value="keys" striped-rows responsive-layout="scroll">
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
            </div>
          </TabPanel>

        </TabView>
      </template>
    </Card>

    <!-- Add Key Dialog -->
    <Dialog v-model:visible="showAddKeyDialog" header="Add System API Key" modal :style="{ width: '480px' }">
      <div class="flex flex-column gap-4">
        <div class="surface-100 border-round p-3 text-sm flex align-items-start gap-2">
          <i class="pi pi-info-circle mt-1 text-blue-400"></i>
          <span>This key will be used for <strong>all users</strong>. It is stored encrypted.</span>
        </div>
        <div class="flex flex-column gap-2">
          <label for="keyName" class="font-medium">Name *</label>
          <InputText id="keyName" v-model="keyForm.name" placeholder="e.g., Production key" :class="{ 'p-invalid': keyErrors.name }" />
          <small v-if="keyErrors.name" class="text-red-500">{{ keyErrors.name }}</small>
        </div>
        <div class="flex flex-column gap-2">
          <label for="keyValue" class="font-medium">API Key *</label>
          <Password id="keyValue" v-model="keyForm.apiKey" placeholder="sk-..." :feedback="false" class="w-full" input-class="w-full" :class="{ 'p-invalid': keyErrors.apiKey }" />
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
          <Password id="editKeyValue" v-model="editKeyForm.apiKey" placeholder="Leave blank to keep current" :feedback="false" class="w-full" input-class="w-full" />
          <small class="text-color-secondary">Leave blank to keep the existing key value.</small>
        </div>
        <div class="flex flex-column gap-2">
          <label for="editKeyDesc" class="font-medium">Description</label>
          <Textarea id="editKeyDesc" v-model="editKeyForm.description" :rows="2" />
        </div>
        <div class="flex align-items-center gap-2">
          <Checkbox id="editKeyActive" v-model="editKeyForm.isActive" :binary="true" />
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
  max-width: 1100px;
}
</style>