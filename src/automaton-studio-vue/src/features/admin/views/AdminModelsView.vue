<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
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
  AiModelAdmin,
  CreateAiModelRequest,
  UpdateAiModelRequest,
} from '../types/admin.types'

const router = useRouter()
const route = useRoute()
const toast = useToast()
const confirm = useConfirm()

// State
const provider = ref<AiProviderAdmin | null>(null)
const models = ref<AiModelAdmin[]>([])
const loading = ref(false)
const error = ref('')

// Dialog state
const showDialog = ref(false)
const editingModel = ref<AiModelAdmin | null>(null)
const isSubmitting = ref(false)

// Form state
const formData = ref({
  modelId: '',
  displayName: '',
  description: '',
  isActive: true,
  isDefault: false,
  maxTokens: 4096,
  supportsStreaming: true,
  supportsTools: false,
  supportsVision: false,
  costPer1MTokensInput: undefined as number | undefined,
  costPer1MTokensOutput: undefined as number | undefined,
  sortOrder: 0,
})
const formErrors = ref<Record<string, string>>({})

const providerId = computed(() => route.params.providerId as string)

const dialogHeader = computed(() => (editingModel.value ? 'Edit Model' : 'Add Model'))

onMounted(async () => {
  await loadData()
})

async function loadData() {
  loading.value = true
  error.value = ''
  try {
    const [providerData, modelsData] = await Promise.all([
      adminAiProvidersService.getProvider(providerId.value),
      adminAiProvidersService.getModelsByProvider(providerId.value),
    ])
    provider.value = providerData
    models.value = modelsData
  } catch (err) {
    error.value = 'Failed to load provider data'
    console.error('Error loading data:', err)
  } finally {
    loading.value = false
  }
}

function openCreateDialog() {
  editingModel.value = null
  formData.value = {
    modelId: '',
    displayName: '',
    description: '',
    isActive: true,
    isDefault: false,
    maxTokens: 4096,
    supportsStreaming: true,
    supportsTools: false,
    supportsVision: false,
    costPer1MTokensInput: undefined,
    costPer1MTokensOutput: undefined,
    sortOrder: 0,
  }
  formErrors.value = {}
  showDialog.value = true
}

function openEditDialog(model: AiModelAdmin) {
  editingModel.value = model
  formData.value = {
    modelId: model.modelId,
    displayName: model.displayName,
    description: model.description || '',
    isActive: model.isActive,
    isDefault: model.isDefault,
    maxTokens: model.maxTokens,
    supportsStreaming: model.supportsStreaming,
    supportsTools: model.supportsTools,
    supportsVision: model.supportsVision,
    costPer1MTokensInput: model.costPer1MTokensInput,
    costPer1MTokensOutput: model.costPer1MTokensOutput,
    sortOrder: model.sortOrder,
  }
  formErrors.value = {}
  showDialog.value = true
}

function validateForm(): boolean {
  formErrors.value = {}
  if (!formData.value.modelId?.trim()) {
    formErrors.value.modelId = 'Model ID is required'
  }
  if (!formData.value.displayName?.trim()) {
    formErrors.value.displayName = 'Display name is required'
  }
  return Object.keys(formErrors.value).length === 0
}

async function handleSave() {
  if (!validateForm()) return

  isSubmitting.value = true
  try {
    if (editingModel.value) {
      const request: UpdateAiModelRequest = {
        displayName: formData.value.displayName,
        description: formData.value.description || undefined,
        isActive: formData.value.isActive,
        isDefault: formData.value.isDefault,
        maxTokens: formData.value.maxTokens,
        supportsStreaming: formData.value.supportsStreaming,
        supportsTools: formData.value.supportsTools,
        supportsVision: formData.value.supportsVision,
        costPer1MTokensInput: formData.value.costPer1MTokensInput,
        costPer1MTokensOutput: formData.value.costPer1MTokensOutput,
        sortOrder: formData.value.sortOrder,
      }
      await adminAiProvidersService.updateModel(editingModel.value.id, request)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Model updated', life: 3000 })
    } else {
      const request: CreateAiModelRequest = {
        modelId: formData.value.modelId,
        displayName: formData.value.displayName,
        description: formData.value.description || undefined,
        isActive: formData.value.isActive,
        isDefault: formData.value.isDefault,
        maxTokens: formData.value.maxTokens,
        supportsStreaming: formData.value.supportsStreaming,
        supportsTools: formData.value.supportsTools,
        supportsVision: formData.value.supportsVision,
        costPer1MTokensInput: formData.value.costPer1MTokensInput,
        costPer1MTokensOutput: formData.value.costPer1MTokensOutput,
        sortOrder: formData.value.sortOrder,
      }
      await adminAiProvidersService.createModel(providerId.value, request)
      toast.add({ severity: 'success', summary: 'Created', detail: 'Model created', life: 3000 })
    }
    showDialog.value = false
    await loadData()
  } catch (err: unknown) {
    console.error('Error saving model:', err)
    const axiosError = err as { response?: { data?: { message?: string } } }
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: axiosError?.response?.data?.message || 'Failed to save model',
      life: 3000,
    })
  } finally {
    isSubmitting.value = false
  }
}

function confirmDelete(model: AiModelAdmin) {
  confirm.require({
    message: `Are you sure you want to delete "${model.displayName}"?`,
    header: 'Delete Model',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await adminAiProvidersService.deleteModel(model.id)
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: 'Model deleted',
          life: 3000,
        })
        models.value = models.value.filter((m) => m.id !== model.id)
      } catch (err: unknown) {
        const axiosError = err as { response?: { data?: { message?: string } } }
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: axiosError?.response?.data?.message || 'Failed to delete model',
          life: 3000,
        })
      }
    },
  })
}

function goBackToProviders() {
  router.push({ name: 'admin-providers' })
}
</script>

<template>
  <div class="admin-models-view">
    <!-- Breadcrumb & Page Header -->
    <div class="page-header mb-4">
      <div class="flex align-items-center gap-2 mb-2">
        <i class="pi pi-shield text-color-secondary"></i>
        <span class="text-color-secondary text-sm cursor-pointer hover:text-primary" @click="goBackToProviders">
          AI Providers
        </span>
        <i class="pi pi-angle-right text-color-secondary text-sm"></i>
        <span class="text-color-secondary text-sm">{{ provider?.displayName || 'Models' }}</span>
      </div>
      <div class="flex align-items-center justify-content-between">
        <div>
          <div class="flex align-items-center gap-3">
            <Button
              icon="pi pi-arrow-left"
              text
              rounded
              severity="secondary"
              title="Back to providers"
              @click="goBackToProviders"
            />
            <div>
              <h1 class="text-2xl font-bold m-0">{{ provider?.displayName }} Models</h1>
              <p class="text-color-secondary mt-1 mb-0">
                {{ provider?.description || 'Manage models for this provider' }}
              </p>
            </div>
          </div>
        </div>
        <Button label="Add Model" icon="pi pi-plus" @click="openCreateDialog" />
      </div>
    </div>

    <!-- Error -->
    <Message v-if="error" severity="error" :closable="false" class="mb-4">{{ error }}</Message>

    <!-- Models Table -->
    <Card>
      <template #content>
        <DataTable
          :value="models"
          :loading="loading"
          striped-rows
          responsive-layout="scroll"
          sort-field="sortOrder"
          :sort-order="1"
        >
          <template #empty>
            <div class="text-center py-5 text-color-secondary">
              <i class="pi pi-box text-4xl mb-3 block"></i>
              <p class="m-0">No models configured for this provider yet.</p>
            </div>
          </template>

          <Column field="displayName" header="Model" style="min-width: 200px">
            <template #body="{ data }">
              <div class="font-medium">{{ data.displayName }}</div>
              <div class="text-sm text-color-secondary">{{ data.modelId }}</div>
            </template>
          </Column>

          <Column field="description" header="Description" style="min-width: 150px">
            <template #body="{ data }">
              <span class="text-color-secondary text-sm">{{ data.description || '—' }}</span>
            </template>
          </Column>

          <Column header="Capabilities" style="min-width: 180px">
            <template #body="{ data }">
              <div class="flex flex-wrap gap-1">
                <Tag v-if="data.supportsStreaming" value="Streaming" severity="info" class="text-xs" />
                <Tag v-if="data.supportsTools" value="Tools" severity="info" class="text-xs" />
                <Tag v-if="data.supportsVision" value="Vision" severity="info" class="text-xs" />
              </div>
            </template>
          </Column>

          <Column field="maxTokens" header="Max Tokens" style="width: 110px">
            <template #body="{ data }">
              <span class="text-sm">{{ data.maxTokens.toLocaleString() }}</span>
            </template>
          </Column>

          <Column field="isActive" header="Status" style="width: 100px">
            <template #body="{ data }">
              <div class="flex flex-column gap-1">
                <Tag
                  :value="data.isActive ? 'Active' : 'Inactive'"
                  :severity="data.isActive ? 'success' : 'warning'"
                />
                <Tag v-if="data.isDefault" value="Default" severity="contrast" class="text-xs" />
              </div>
            </template>
          </Column>

          <Column header="Actions" style="width: 120px" align="center">
            <template #body="{ data }">
              <div class="flex gap-1 justify-content-center">
                <Button
                  icon="pi pi-pencil"
                  text
                  rounded
                  severity="warning"
                  size="small"
                  title="Edit model"
                  @click="openEditDialog(data)"
                />
                <Button
                  icon="pi pi-trash"
                  text
                  rounded
                  severity="danger"
                  size="small"
                  title="Delete model"
                  @click="confirmDelete(data)"
                />
              </div>
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <!-- Create/Edit Model Dialog -->
    <Dialog
      v-model:visible="showDialog"
      :header="dialogHeader"
      modal
      :style="{ width: '600px' }"
    >
      <div class="flex flex-column gap-4">
        <!-- Model ID -->
        <div class="flex flex-column gap-2">
          <label for="modelId" class="font-medium">Model ID *</label>
          <InputText
            id="modelId"
            v-model="formData.modelId"
            :disabled="!!editingModel"
            placeholder="e.g., gpt-4-turbo"
            :class="{ 'p-invalid': formErrors.modelId }"
          />
          <small v-if="!editingModel" class="text-color-secondary">
            Unique identifier used by the AI provider API
          </small>
          <small v-if="formErrors.modelId" class="text-red-500">{{ formErrors.modelId }}</small>
        </div>

        <!-- Display Name -->
        <div class="flex flex-column gap-2">
          <label for="modelDisplayName" class="font-medium">Display Name *</label>
          <InputText
            id="modelDisplayName"
            v-model="formData.displayName"
            placeholder="e.g., GPT-4 Turbo"
            :class="{ 'p-invalid': formErrors.displayName }"
          />
          <small v-if="formErrors.displayName" class="text-red-500">{{
            formErrors.displayName
          }}</small>
        </div>

        <!-- Description -->
        <div class="flex flex-column gap-2">
          <label for="modelDescription" class="font-medium">Description</label>
          <Textarea
            id="modelDescription"
            v-model="formData.description"
            placeholder="Brief description of this model's capabilities"
            rows="2"
          />
        </div>

        <!-- Status row -->
        <div class="flex gap-4">
          <div class="flex align-items-center gap-2">
            <Checkbox id="modelIsActive" v-model="formData.isActive" :binary="true" />
            <label for="modelIsActive">Active</label>
          </div>
          <div class="flex align-items-center gap-2">
            <Checkbox id="modelIsDefault" v-model="formData.isDefault" :binary="true" />
            <label for="modelIsDefault">Default model</label>
          </div>
        </div>

        <!-- Max Tokens & Sort Order -->
        <div class="flex gap-4">
          <div class="flex flex-column gap-2 flex-1">
            <label for="modelMaxTokens" class="font-medium">Max Tokens</label>
            <InputNumber
              id="modelMaxTokens"
              v-model="formData.maxTokens"
              :min="1"
              :use-grouping="true"
            />
          </div>
          <div class="flex flex-column gap-2" style="width: 120px">
            <label for="modelSortOrder" class="font-medium">Sort Order</label>
            <InputNumber id="modelSortOrder" v-model="formData.sortOrder" :min="0" />
          </div>
        </div>

        <!-- Capabilities -->
        <div>
          <label class="font-medium block mb-2">Capabilities</label>
          <div class="flex gap-4">
            <div class="flex align-items-center gap-2">
              <Checkbox
                id="modelStreaming"
                v-model="formData.supportsStreaming"
                :binary="true"
              />
              <label for="modelStreaming">Streaming</label>
            </div>
            <div class="flex align-items-center gap-2">
              <Checkbox id="modelTools" v-model="formData.supportsTools" :binary="true" />
              <label for="modelTools">Tools</label>
            </div>
            <div class="flex align-items-center gap-2">
              <Checkbox id="modelVision" v-model="formData.supportsVision" :binary="true" />
              <label for="modelVision">Vision</label>
            </div>
          </div>
        </div>

        <!-- Cost -->
        <div>
          <label class="font-medium block mb-2">Cost per 1K Tokens</label>
          <div class="flex gap-4">
            <div class="flex flex-column gap-2 flex-1">
              <label for="costInput" class="text-sm text-color-secondary">Input</label>
              <InputNumber
                id="costInput"
                v-model="formData.costPer1MTokensInput"
                :min-fraction-digits="2"
                :max-fraction-digits="6"
                mode="decimal"
                placeholder="0.00"
              />
            </div>
            <div class="flex flex-column gap-2 flex-1">
              <label for="costOutput" class="text-sm text-color-secondary">Output</label>
              <InputNumber
                id="costOutput"
                v-model="formData.costPer1MTokensOutput"
                :min-fraction-digits="2"
                :max-fraction-digits="6"
                mode="decimal"
                placeholder="0.00"
              />
            </div>
          </div>
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" text :disabled="isSubmitting" @click="showDialog = false" />
        <Button
          :label="editingModel ? 'Save' : 'Create'"
          :loading="isSubmitting"
          @click="handleSave"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.admin-models-view {
  max-width: 1200px;
}
</style>
