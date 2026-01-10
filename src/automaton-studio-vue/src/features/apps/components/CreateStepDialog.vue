<script setup lang="ts">
/**
 * CreateStepDialog.vue
 * Dialog for creating new backend steps for apps
 */
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Dropdown from 'primevue/dropdown'
import Textarea from 'primevue/textarea'
import Button from 'primevue/button'
import type { CreateAppStepRequest } from '../types/apps.types'

const props = defineProps<{
  visible: boolean
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  create: [request: CreateAppStepRequest]
}>()

// Form state
const stepName = ref('')
const language = ref('Python')
const description = ref('')
const isSubmitting = ref(false)

// Language options
const languageOptions = [
  { label: 'Python', value: 'Python' },
  { label: 'PowerShell', value: 'PowerShell' },
  { label: 'Batch', value: 'Batch' },
]

// Computed visibility
const isVisible = computed({
  get: () => props.visible,
  set: (value) => emit('update:visible', value),
})

// Validation
const isValidName = computed(() => {
  const name = stepName.value.trim()
  if (!name) return false
  // Valid identifier: starts with letter, contains only letters, numbers, underscores
  return /^[a-zA-Z][a-zA-Z0-9_]*$/.test(name) && name.length >= 3
})

const nameValidationMessage = computed(() => {
  const name = stepName.value.trim()
  if (!name) return null
  if (name.length < 3) return 'Name must be at least 3 characters'
  if (!/^[a-zA-Z]/.test(name)) return 'Name must start with a letter'
  if (!/^[a-zA-Z0-9_]*$/.test(name)) return 'Name can only contain letters, numbers, and underscores'
  return null
})

// Reset form when dialog opens
watch(isVisible, (visible) => {
  if (visible) {
    stepName.value = ''
    language.value = 'Python'
    description.value = ''
    isSubmitting.value = false
  }
})

// Handle create
async function handleCreate() {
  if (!isValidName.value || isSubmitting.value) return

  isSubmitting.value = true

  try {
    emit('create', {
      name: stepName.value.trim(),
      language: language.value,
      description: description.value.trim() || undefined,
    })
  } finally {
    isSubmitting.value = false
  }
}

// Close dialog
function close() {
  isVisible.value = false
}
</script>

<template>
  <Dialog
    v-model:visible="isVisible"
    header="Create Backend Step"
    :modal="true"
    :closable="true"
    :style="{ width: '450px' }"
  >
    <div class="create-step-dialog">
      <!-- Step name -->
      <div class="field mb-4">
        <label for="stepName" class="block font-semibold mb-2">
          Step Name <span class="text-red-500">*</span>
        </label>
        <InputText
          id="stepName"
          v-model="stepName"
          placeholder="e.g., GetUserData"
          class="w-full"
          :class="{ 'p-invalid': nameValidationMessage }"
        />
        <small v-if="nameValidationMessage" class="p-error block mt-1">
          {{ nameValidationMessage }}
        </small>
        <small v-else class="text-color-secondary block mt-1">
          Use PascalCase or camelCase (e.g., GetUserData, processOrder)
        </small>
      </div>

      <!-- Language -->
      <div class="field mb-4">
        <label for="language" class="block font-semibold mb-2">
          Language <span class="text-red-500">*</span>
        </label>
        <Dropdown
          id="language"
          v-model="language"
          :options="languageOptions"
          optionLabel="label"
          optionValue="value"
          class="w-full"
        />
        <small class="text-color-secondary block mt-1">
          {{ language === 'Python' ? 'Recommended for most tasks' :
             language === 'PowerShell' ? 'Best for Windows automation' :
             'For simple Windows scripts' }}
        </small>
      </div>

      <!-- Description -->
      <div class="field mb-4">
        <label for="description" class="block font-semibold mb-2">
          Description
        </label>
        <Textarea
          id="description"
          v-model="description"
          placeholder="What does this step do?"
          rows="3"
          class="w-full"
        />
      </div>
    </div>

    <template #footer>
      <div class="flex justify-content-end gap-2">
        <Button
          label="Cancel"
          severity="secondary"
          text
          @click="close"
        />
        <Button
          label="Create"
          icon="pi pi-plus"
          :disabled="!isValidName"
          :loading="isSubmitting"
          @click="handleCreate"
        />
      </div>
    </template>
  </Dialog>
</template>

<style scoped>
.create-step-dialog {
  padding: 0.5rem 0;
}
</style>
