<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import {
  getAvailableProviders,
  getUserAiApiKeys,
  createAiApiKey,
  updateAiApiKey,
} from '@/shared/services/ai/ai-providers-service'
import type { AiProvider, AiApiKey } from '@/shared/types/ai.types'

const CUSTOM_VALUE = '__custom__'

const router = useRouter()
const route = useRoute()
const toast = useToast()

const editId = computed(() => route.params.id as string | undefined)
const isEdit = computed(() => !!editId.value)

// ── State ──────────────────────────────────────────────────────────────────
const providers = ref<AiProvider[]>([])
const existingKey = ref<AiApiKey | null>(null)
const loading = ref(false)
const saving = ref(false)

const form = ref({
  providerName: '',
  customProviderName: '',
  apiKey: '',
  name: '',
  description: '',
  baseUrl: '',
  defaultModel: '',
})
const errors = ref<Record<string, string>>({})

// ── Derived ────────────────────────────────────────────────────────────────

/** Custom (OpenAI-compatible) is shown first, then known providers. */
const providerOptions = computed(() => {
  const known = providers.value.map((p) => ({
    label: p.displayName || p.providerName,
    value: p.providerName,
  }))
  return [
    { label: 'Custom (OpenAI-compatible)', value: CUSTOM_VALUE },
    ...known,
  ]
})

const isCustom = computed(() => form.value.providerName === CUSTOM_VALUE)

/** True once the user has picked a provider (or we're in edit mode). */
const providerChosen = computed(() => isEdit.value || form.value.providerName !== '')

const effectiveProviderName = computed(() =>
  isCustom.value ? form.value.customProviderName.trim() : form.value.providerName,
)

const pageTitle = computed(() => (isEdit.value ? 'Edit AI Key' : 'Add AI Key'))
const submitLabel = computed(() => (isEdit.value ? 'Save Changes' : 'Add Key'))

// ── Load ───────────────────────────────────────────────────────────────────
onMounted(async () => {
  loading.value = true
  try {
    const [providerData, keyData] = await Promise.all([
      getAvailableProviders(false, true),
      isEdit.value ? getUserAiApiKeys() : Promise.resolve([] as AiApiKey[]),
    ])
    providers.value = providerData

    if (isEdit.value) {
      existingKey.value = keyData.find((k) => k.id === editId.value) ?? null
      if (!existingKey.value) {
        toast.add({ severity: 'error', summary: 'Not found', detail: 'Key not found', life: 3000 })
        router.push({ name: 'ai-keys' })
        return
      }
      populateForm(existingKey.value)
    } else {
      // Start with no selection — user must choose
      form.value.providerName = ''
    }
  } catch {
    toast.add({ severity: 'error', summary: 'Load failed', detail: 'Could not load data', life: 3000 })
  } finally {
    loading.value = false
  }
})

function populateForm(key: AiApiKey) {
  // Detect whether the saved key is a custom provider (has a baseUrl)
  const isKnown = providers.value.some(
    (p) => p.providerName.toLowerCase() === key.providerName.toLowerCase(),
  )
  if (isKnown) {
    form.value.providerName = key.providerName
    form.value.customProviderName = ''
  } else {
    form.value.providerName = CUSTOM_VALUE
    form.value.customProviderName = key.providerName
  }
  form.value.name = key.name
  form.value.description = key.description ?? ''
  form.value.baseUrl = key.baseUrl ?? ''
  form.value.defaultModel = ''
  form.value.apiKey = ''
}

// ── Validation ─────────────────────────────────────────────────────────────
function validate(): boolean {
  const e: Record<string, string> = {}
  if (!form.value.providerName) e.providerName = 'Select a provider'
  if (isCustom.value && !form.value.customProviderName.trim())
    e.customProviderName = 'Provider name is required'
  if (!isEdit.value && !form.value.apiKey.trim()) e.apiKey = 'API key is required'
  if (isCustom.value && !form.value.baseUrl.trim()) e.baseUrl = 'Base URL is required'
  if (isCustom.value && !isEdit.value && !form.value.defaultModel.trim())
    e.defaultModel = 'Default model is required'
  errors.value = e
  return Object.keys(e).length === 0
}

// ── Submit ─────────────────────────────────────────────────────────────────
async function submit() {
  if (!validate()) return
  saving.value = true
  try {
    const friendlyName = form.value.name.trim() || `${effectiveProviderName.value} key`
    if (isEdit.value) {
      await updateAiApiKey(editId.value!, {
        name: friendlyName,
        description: form.value.description || undefined,
      })
      toast.add({ severity: 'success', summary: 'Saved', detail: `${friendlyName} updated`, life: 3000 })
    } else {
      await createAiApiKey({
        name: friendlyName,
        providerName: effectiveProviderName.value,
        apiKey: form.value.apiKey,
        description: form.value.description || undefined,
        baseUrl: isCustom.value ? form.value.baseUrl.trim() : undefined,
        defaultModel: isCustom.value ? form.value.defaultModel.trim() : undefined,
      })
      toast.add({ severity: 'success', summary: 'Key added', detail: `${friendlyName} saved`, life: 3000 })
    }
    router.push({ name: 'ai-keys' })
  } catch (err: any) {
    const detail = err?.response?.data?.detail || err?.message || 'Failed to save key'
    toast.add({ severity: 'error', summary: 'Error', detail, life: 4000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="ai-key-form-view">
    <!-- Back navigation -->
    <div class="page-header">
      <Button icon="pi pi-arrow-left" text rounded @click="router.push({ name: 'ai-keys' })" />
      <span class="text-color-secondary">AI Keys</span>
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 1.5rem" />
    </div>

    <Card v-else>
      <template #title>{{ pageTitle }}</template>
      <template #content>
      <div class="form-card">
      <!-- Step 1: Provider selector (create) or badge (edit) -->
      <div v-if="!isEdit" class="field">
        <label class="field-label">Provider <span class="required">*</span></label>
        <Select
          v-model="form.providerName"
          :options="providerOptions"
          option-label="label"
          option-value="value"
          placeholder="Select a provider…"
          class="w-full"
          :invalid="!!errors.providerName"
        />
        <small v-if="errors.providerName" class="field-error">{{ errors.providerName }}</small>
      </div>

      <div v-if="isEdit && existingKey" class="field">
        <label class="field-label">Provider</label>
        <div class="provider-badge">{{ existingKey.providerName }}</div>
        <small class="text-color-secondary">Provider cannot be changed. Delete and re-add the key to switch providers.</small>
      </div>

      <!-- Step 2: rest of form — revealed after provider is chosen -->
      <Transition name="reveal">
        <div v-if="providerChosen" class="form-fields">
          <!-- Custom provider name (custom only) -->
          <div v-if="isCustom && !isEdit" class="field">
            <label class="field-label">Provider Name <span class="required">*</span></label>
            <InputText
              v-model="form.customProviderName"
              placeholder="e.g. Kimi, DeepSeek, Mistral"
              class="w-full"
              :invalid="!!errors.customProviderName"
            />
            <small v-if="errors.customProviderName" class="field-error">{{ errors.customProviderName }}</small>
          </div>

          <!-- API Key (create only) -->
          <div v-if="!isEdit" class="field">
            <label class="field-label">API Key <span class="required">*</span></label>
            <Password
              v-model="form.apiKey"
              placeholder="sk-..."
              class="w-full"
              :feedback="false"
              toggle-mask
              input-class="w-full"
              :invalid="!!errors.apiKey"
            />
            <small v-if="errors.apiKey" class="field-error">{{ errors.apiKey }}</small>
          </div>

          <!-- Base URL (custom, create only) -->
          <div v-if="isCustom && !isEdit" class="field">
            <label class="field-label">Base URL <span class="required">*</span></label>
            <InputText
              v-model="form.baseUrl"
              placeholder="https://api.moonshot.cn/v1"
              class="w-full"
              :invalid="!!errors.baseUrl"
            />
            <small v-if="errors.baseUrl" class="field-error">{{ errors.baseUrl }}</small>
          </div>

          <!-- Default Model (custom, create only) -->
          <div v-if="isCustom && !isEdit" class="field">
            <label class="field-label">Default Model <span class="required">*</span></label>
            <InputText
              v-model="form.defaultModel"
              placeholder="e.g. kimi-k2.5-turbo"
              class="w-full"
              :invalid="!!errors.defaultModel"
            />
            <small v-if="errors.defaultModel" class="field-error">{{ errors.defaultModel }}</small>
          </div>

          <!-- Friendly Name -->
          <div class="field">
            <label class="field-label">Friendly Name</label>
            <InputText
              v-model="form.name"
              :placeholder="`${effectiveProviderName || 'My'} key`"
              class="w-full"
            />
          </div>

          <!-- Description -->
          <div class="field">
            <label class="field-label">Description</label>
            <Textarea
              v-model="form.description"
              placeholder="Optional note"
              :rows="2"
              class="w-full"
            />
          </div>

          <!-- Actions -->
          <div class="form-actions">
            <Button label="Cancel" text @click="router.push({ name: 'ai-keys' })" />
            <Button :label="submitLabel" icon="pi pi-check" :loading="saving" @click="submit" />
          </div>
        </div>
      </Transition>

      <!-- Cancel always reachable before a provider is chosen -->
      <div v-if="!providerChosen" class="form-actions">
        <Button label="Cancel" text @click="router.push({ name: 'ai-keys' })" />
      </div>
      </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.ai-key-form-view {
  padding: 1.5rem;
  max-width: 1400px;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1.5rem;
}

.loading-state {
  display: flex;
  justify-content: center;
  padding: 3rem;
}

.form-card {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  max-width: 540px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.field-label {
  font-weight: 500;
  font-size: 0.9rem;
}

.required {
  color: var(--p-red-500);
  margin-left: 2px;
}

.field-error {
  color: var(--p-red-500);
  font-size: 0.8rem;
}

.provider-badge {
  display: inline-flex;
  padding: 0.25rem 0.75rem;
  border-radius: 1rem;
  background: var(--p-surface-100);
  font-size: 0.85rem;
  font-weight: 500;
  width: fit-content;
}

.form-fields {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  padding-top: 0.5rem;
}

/* Progressive reveal transition */
.reveal-enter-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}
.reveal-enter-from {
  opacity: 0;
  transform: translateY(-6px);
}
</style>
