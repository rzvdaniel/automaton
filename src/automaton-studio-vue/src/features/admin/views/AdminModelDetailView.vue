<script setup lang="ts">
/**
 * AdminModelDetailView
 * Full-page editor for a single AI model (create or edit).
 * Navigated to from AdminProviderDetailView.
 */
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import InputNumber from 'primevue/inputnumber'
import Divider from 'primevue/divider'
import { adminAiProvidersService } from '../services/adminAiProvidersService'
import type { AiModelAdmin, CreateAiModelRequest, UpdateAiModelRequest, GlobalAiDefaults } from '../types/admin.types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const providerId = route.params.providerId as string
const modelId = route.params.modelId as string
const isNew = modelId === 'new'

const loading = ref(false)
const isSubmitting = ref(false)
const globalDefaults = ref<GlobalAiDefaults>({})
const existingModel = ref<AiModelAdmin | null>(null)

const form = ref<CreateAiModelRequest>({
  modelId: '',
  displayName: '',
  description: '',
  isActive: true,
  isDefault: false,
  isPlanningModel: false,
  maxTokens: null as number | null,
  supportsStreaming: true,
  supportsTools: false,
  supportsVision: false,
  costPer1MTokensInput: undefined,
  costPer1MTokensOutput: undefined,
  extraBodyJson: '',
  sortOrder: 0,
})
const formErrors = ref<Record<string, string>>({})

const pageTitle = computed(() => isNew ? 'Add Model' : `Edit: ${existingModel.value?.displayName ?? 'Model'}`)

const defaultConflict = computed(() => {
  const d = globalDefaults.value.defaultModel
  return d && !d.isDefault && form.value.isDefault
    ? null
    : (d && d.id !== existingModel.value?.id && form.value.isDefault ? d : null)
})

const planningConflict = computed(() => {
  const p = globalDefaults.value.planningModel
  return p && p.id !== existingModel.value?.id && form.value.isPlanningModel ? p : null
})

onMounted(async () => {
  loading.value = true
  try {
    const defaults = adminAiProvidersService.getGlobalDefaults()
    if (!isNew) {
      const [model, defs] = await Promise.all([
        adminAiProvidersService.getModel(modelId),
        defaults,
      ])
      existingModel.value = model
      globalDefaults.value = defs
      form.value = {
        modelId: model.modelId,
        displayName: model.displayName,
        description: model.description,
        isActive: model.isActive,
        isDefault: model.isDefault,
        isPlanningModel: model.isPlanningModel,
        maxTokens: model.maxTokens ?? null,
        supportsStreaming: model.supportsStreaming,
        supportsTools: model.supportsTools,
        supportsVision: model.supportsVision,
        costPer1MTokensInput: model.costPer1MTokensInput,
        costPer1MTokensOutput: model.costPer1MTokensOutput,
        extraBodyJson: model.extraBodyJson ?? '',
        sortOrder: model.sortOrder,
      }
    } else {
      const [defs] = await Promise.all([defaults])
      globalDefaults.value = defs
      // Default maxTokens from global config
      form.value.maxTokens = defs.defaultMaxOutputTokens ?? null
    }
  } catch {
    toast.add({ severity: 'error', summary: 'Load Failed', detail: 'Failed to load model data', life: 3000 })
  } finally {
    loading.value = false
  }
})

function validate(): boolean {
  formErrors.value = {}
  if (!form.value.modelId?.trim()) formErrors.value.modelId = 'Model ID is required'
  if (!form.value.displayName?.trim()) formErrors.value.displayName = 'Display name is required'
  return Object.keys(formErrors.value).length === 0
}

async function handleSave() {
  if (!validate()) return
  isSubmitting.value = true
  try {
    const extraBody = form.value.extraBodyJson?.trim() || undefined
    if (isNew) {
      const req: CreateAiModelRequest = { ...form.value, extraBodyJson: extraBody }
      await adminAiProvidersService.createModel(providerId, req)
      toast.add({ severity: 'success', summary: 'Created', detail: 'Model created', life: 3000 })
    } else {
      const req: UpdateAiModelRequest = {
        modelId: form.value.modelId,
        displayName: form.value.displayName,
        description: form.value.description,
        isActive: form.value.isActive,
        isDefault: form.value.isDefault,
        isPlanningModel: form.value.isPlanningModel,
        maxTokens: form.value.maxTokens,
        supportsStreaming: form.value.supportsStreaming,
        supportsTools: form.value.supportsTools,
        supportsVision: form.value.supportsVision,
        costPer1MTokensInput: form.value.costPer1MTokensInput,
        costPer1MTokensOutput: form.value.costPer1MTokensOutput,
        extraBodyJson: extraBody,
        sortOrder: form.value.sortOrder,
      }
      await adminAiProvidersService.updateModel(modelId, req)
      toast.add({ severity: 'success', summary: 'Saved', detail: 'Model saved', life: 3000 })
    }
    router.push({ name: 'admin-provider-detail', params: { providerId } })
  } catch (error: unknown) {
    const axiosError = error as { response?: { data?: { message?: string } } }
    toast.add({ severity: 'error', summary: 'Save Failed', detail: axiosError?.response?.data?.message || 'Failed to save model', life: 3000 })
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <div class="admin-model-detail">
    <!-- Header -->
    <div class="page-header mb-4">
      <div class="flex align-items-center gap-2 mb-2">
        <Button
          icon="pi pi-arrow-left"
          text rounded size="small"
          title="Back to provider"
          @click="router.push({ name: 'admin-provider-detail', params: { providerId } })"
        />
        <i class="pi pi-shield text-color-secondary"></i>
        <span class="text-color-secondary text-sm">Administration / AI Providers / Models</span>
      </div>
      <h1 class="text-2xl font-bold m-0">{{ pageTitle }}</h1>
    </div>

    <div v-if="loading" class="flex justify-content-center py-6">
      <i class="pi pi-spin pi-spinner text-3xl text-color-secondary" />
    </div>

    <div v-else class="flex flex-column gap-4">
      <!-- Identity -->
      <Card>
        <template #header>
          <h2 class="text-base font-semibold m-0 px-4 pt-3">Identity</h2>
        </template>
        <template #content>
          <div class="flex flex-column gap-4">
            <div class="flex gap-4">
              <div class="flex flex-column gap-2 flex-1">
                <label for="modelId" class="font-medium">Model ID *</label>
                <InputText
                  id="modelId"
                  v-model="form.modelId"
                  placeholder="e.g., gpt-4o"
                  :disabled="!isNew"
                  :class="{ 'p-invalid': formErrors.modelId }"
                />
                <small v-if="!isNew" class="text-color-secondary">Cannot be changed after creation</small>
                <small v-if="formErrors.modelId" class="text-red-500">{{ formErrors.modelId }}</small>
              </div>
              <div class="flex flex-column gap-2 flex-1">
                <label for="displayName" class="font-medium">Display Name *</label>
                <InputText
                  id="displayName"
                  v-model="form.displayName"
                  placeholder="e.g., GPT-4o"
                  :class="{ 'p-invalid': formErrors.displayName }"
                />
                <small v-if="formErrors.displayName" class="text-red-500">{{ formErrors.displayName }}</small>
              </div>
            </div>
            <div class="flex flex-column gap-2">
              <label for="description" class="font-medium">Description</label>
              <Textarea id="description" v-model="form.description" placeholder="Short description" :rows="2" />
            </div>
            <div class="flex gap-4">
              <div class="flex flex-column gap-2" style="width: 180px">
                <label for="sortOrder" class="font-medium">Sort Order</label>
                <InputNumber id="sortOrder" v-model="form.sortOrder" :min="0" />
              </div>
              <div class="flex align-items-center gap-2 mt-4">
                <Checkbox id="isActive" v-model="form.isActive" :binary="true" />
                <label for="isActive">Active (shown in chat selectors)</label>
              </div>
            </div>
          </div>
        </template>
      </Card>

      <!-- Role -->
      <Card>
        <template #header>
          <h2 class="text-base font-semibold m-0 px-4 pt-3">Role</h2>
        </template>
        <template #content>
          <div class="flex flex-column gap-3">
            <div class="flex align-items-start gap-3">
              <Checkbox id="isDefault" v-model="form.isDefault" :binary="true" class="mt-1" />
              <div>
                <label for="isDefault" class="font-medium cursor-pointer">Global Default</label>
                <p class="text-color-secondary text-sm m-0">
                  Used by the system for all AI operations when no specific model is selected. Only one model across all providers can be global default.
                </p>
                <div v-if="defaultConflict" class="mt-1 text-orange-500 text-sm flex align-items-center gap-1">
                  <i class="pi pi-exclamation-triangle" />
                  Will replace current default: <strong>{{ defaultConflict.displayName }}</strong> ({{ defaultConflict.providerName }})
                </div>
              </div>
            </div>
            <Divider />
            <div class="flex align-items-start gap-3">
              <Checkbox id="isPlanningModel" v-model="form.isPlanningModel" :binary="true" class="mt-1" />
              <div>
                <label for="isPlanningModel" class="font-medium cursor-pointer">Planning Model</label>
                <p class="text-color-secondary text-sm m-0">
                  Used during the planning phase of AI flow generation. Only one model can hold this role.
                </p>
                <div v-if="planningConflict" class="mt-1 text-orange-500 text-sm flex align-items-center gap-1">
                  <i class="pi pi-exclamation-triangle" />
                  Will replace current planner: <strong>{{ planningConflict.displayName }}</strong> ({{ planningConflict.providerName }})
                </div>
              </div>
            </div>
          </div>
        </template>
      </Card>

      <!-- Capabilities & Limits -->
      <Card>
        <template #header>
          <h2 class="text-base font-semibold m-0 px-4 pt-3">Capabilities & Limits</h2>
        </template>
        <template #content>
          <div class="flex flex-column gap-4">
            <div class="flex flex-column gap-2" style="max-width: 240px">
              <label for="maxTokens" class="font-medium">Max Output Tokens</label>
              <InputNumber
                id="maxTokens"
                v-model="form.maxTokens"
                :min="1"
                :use-grouping="false"
                :class="{ 'p-invalid': formErrors.maxTokens }"
              />
              <small v-if="formErrors.maxTokens" class="text-red-500">{{ formErrors.maxTokens }}</small>
            </div>
            <div class="flex flex-wrap gap-4">
              <div class="flex align-items-center gap-2">
                <Checkbox id="supportsStreaming" v-model="form.supportsStreaming" :binary="true" />
                <label for="supportsStreaming">Streaming</label>
              </div>
              <div class="flex align-items-center gap-2">
                <Checkbox id="supportsTools" v-model="form.supportsTools" :binary="true" />
                <label for="supportsTools">Tool Use</label>
              </div>
              <div class="flex align-items-center gap-2">
                <Checkbox id="supportsVision" v-model="form.supportsVision" :binary="true" />
                <label for="supportsVision">Vision</label>
              </div>
            </div>
          </div>
        </template>
      </Card>

      <!-- Cost & Advanced -->
      <Card>
        <template #header>
          <h2 class="text-base font-semibold m-0 px-4 pt-3">Cost & Advanced</h2>
        </template>
        <template #content>
          <div class="flex flex-column gap-4">
            <div class="flex gap-4">
              <div class="flex flex-column gap-2 flex-1">
                <label for="costInput" class="font-medium">Cost per 1M input tokens ($)</label>
                <InputNumber id="costInput" v-model="form.costPer1MTokensInput" mode="decimal" :min-fraction-digits="2" :max-fraction-digits="6" placeholder="e.g., 2.50" />
              </div>
              <div class="flex flex-column gap-2 flex-1">
                <label for="costOutput" class="font-medium">Cost per 1M output tokens ($)</label>
                <InputNumber id="costOutput" v-model="form.costPer1MTokensOutput" mode="decimal" :min-fraction-digits="2" :max-fraction-digits="6" placeholder="e.g., 10.00" />
              </div>
            </div>
            <div class="flex flex-column gap-2">
              <label for="extraBody" class="font-medium">Extra Body JSON</label>
              <Textarea
                id="extraBody"
                v-model="form.extraBodyJson"
                placeholder='{"thinking": {"type": "enabled", "budget_tokens": 8000}}'
                :rows="3"
                class="font-mono text-sm"
              />
              <small class="text-color-secondary">JSON merged into every request body for this model (e.g. extended thinking, custom parameters).</small>
            </div>
          </div>
        </template>
      </Card>

      <!-- Save bar -->
      <div class="flex justify-content-end gap-2 pb-4">
        <Button
          label="Cancel"
          text
          :disabled="isSubmitting"
          @click="router.push({ name: 'admin-provider-detail', params: { providerId } })"
        />
        <Button
          :label="isNew ? 'Create Model' : 'Save Changes'"
          icon="pi pi-check"
          :loading="isSubmitting"
          @click="handleSave"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.admin-model-detail {
  max-width: 900px;
}
</style>