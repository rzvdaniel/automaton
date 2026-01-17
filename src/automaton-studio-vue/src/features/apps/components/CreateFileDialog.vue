<script setup lang="ts">
/**
 * CreateFileDialog.vue
 * Dialog for creating new files in the app
 */
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Dropdown from 'primevue/dropdown'
import Button from 'primevue/button'

const props = defineProps<{
  visible: boolean
  existingFiles?: string[]
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  create: [path: string, content: string]
}>()

// Form state
const fileName = ref('')
const fileType = ref('html')
const isSubmitting = ref(false)

// File type options
const fileTypeOptions = [
  { label: 'HTML', value: 'html', extension: '.html', template: getHtmlTemplate },
  { label: 'CSS', value: 'css', extension: '.css', template: getCssTemplate },
  { label: 'JavaScript', value: 'js', extension: '.js', template: getJsTemplate },
  { label: 'JSON', value: 'json', extension: '.json', template: getJsonTemplate },
]

// Computed visibility
const isVisible = computed({
  get: () => props.visible,
  set: (value) => emit('update:visible', value),
})

// Get selected file type config
const selectedType = computed(() => {
  return fileTypeOptions.find((t) => t.value === fileType.value) || fileTypeOptions[0]
})

// Full file path
const fullPath = computed(() => {
  const name = fileName.value.trim()
  if (!name) return ''
  // If name already has extension, use it
  if (/\.[a-z]+$/i.test(name)) return name
  // Otherwise add extension
  return name + selectedType.value.extension
})

// Validation
const isValidName = computed(() => {
  const name = fileName.value.trim()
  if (!name) return false
  // Valid filename: letters, numbers, hyphens, underscores, dots
  return /^[a-zA-Z0-9_-]+(\.[a-zA-Z0-9]+)?$/.test(name)
})

const isDuplicate = computed(() => {
  if (!props.existingFiles || !fullPath.value) return false
  return props.existingFiles.includes(fullPath.value)
})

const validationMessage = computed(() => {
  const name = fileName.value.trim()
  if (!name) return null
  if (!/^[a-zA-Z0-9]/.test(name)) return 'Name must start with a letter or number'
  if (!/^[a-zA-Z0-9_.-]*$/.test(name)) return 'Only letters, numbers, hyphens, underscores, and dots allowed'
  if (isDuplicate.value) return 'A file with this name already exists'
  return null
})

// Reset form when dialog opens
watch(isVisible, (visible) => {
  if (visible) {
    fileName.value = ''
    fileType.value = 'html'
    isSubmitting.value = false
  }
})

// Handle create
async function handleCreate() {
  if (!isValidName.value || isDuplicate.value || isSubmitting.value) return

  isSubmitting.value = true

  try {
    const content = selectedType.value.template(fileName.value.trim())
    emit('create', fullPath.value, content)
  } finally {
    isSubmitting.value = false
  }
}

// Close dialog
function close() {
  isVisible.value = false
}

// Template functions
function getHtmlTemplate(name: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>${name}</title>
</head>
<body>
    <h1>${name}</h1>
</body>
</html>`
}

function getCssTemplate(name: string): string {
  return `/* ${name} styles */

body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    margin: 0;
    padding: 0;
}

.container {
    max-width: 1200px;
    margin: 0 auto;
    padding: 20px;
}
`
}

function getJsTemplate(name: string): string {
  return `// ${name}

document.addEventListener('DOMContentLoaded', function() {
    console.log('${name} loaded');
});
`
}

function getJsonTemplate(_name: string): string {
  return `{

}
`
}
</script>

<template>
  <Dialog
    v-model:visible="isVisible"
    header="Create New File"
    :modal="true"
    :closable="true"
    :style="{ width: '400px' }"
  >
    <div class="create-file-dialog">
      <!-- File type -->
      <div class="field mb-4">
        <label for="fileType" class="block font-semibold mb-2">File Type</label>
        <Dropdown
          id="fileType"
          v-model="fileType"
          :options="fileTypeOptions"
          option-label="label"
          option-value="value"
          class="w-full"
        />
      </div>

      <!-- File name -->
      <div class="field mb-4">
        <label for="fileName" class="block font-semibold mb-2">
          File Name <span class="text-red-500">*</span>
        </label>
        <div class="p-inputgroup">
          <InputText
            id="fileName"
            v-model="fileName"
            :placeholder="'e.g., ' + (fileType === 'html' ? 'page' : fileType === 'css' ? 'styles' : 'script')"
            class="flex-1"
            :class="{ 'p-invalid': validationMessage }"
          />
          <span class="p-inputgroup-addon">{{ selectedType.extension }}</span>
        </div>
        <small v-if="validationMessage" class="p-error block mt-1">
          {{ validationMessage }}
        </small>
        <small v-else-if="fullPath" class="text-color-secondary block mt-1">
          File will be created as: {{ fullPath }}
        </small>
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
          :disabled="!isValidName || isDuplicate"
          :loading="isSubmitting"
          @click="handleCreate"
        />
      </div>
    </template>
  </Dialog>
</template>

<style scoped>
.create-file-dialog {
  padding: 0.5rem 0;
}
</style>
