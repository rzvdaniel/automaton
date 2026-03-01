<script setup lang="ts">
/**
 * PublishDialog.vue
 * Dialog for publishing apps with URL configuration
 */
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Checkbox from 'primevue/checkbox'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { appsService } from '../services/apps.service'
import type { PublishAppRequest } from '../types/apps.types'

const props = defineProps<{
  visible: boolean
  currentSlug?: string
  requiresAuth?: boolean
  appName?: string
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  publish: [request: PublishAppRequest]
}>()

// Form state
const slug = ref('')
const requiresAuthentication = ref(false)
const isValidating = ref(false)
const validationError = ref<string | null>(null)
const isSlugAvailable = ref(true)

// Computed visibility
const isVisible = computed({
  get: () => props.visible,
  set: (value) => emit('update:visible', value),
})

// Computed public URL
const publicUrl = computed(() => {
  return `/web/${slug.value || 'your-app'}`
})

// Validate slug format
const isValidSlug = computed(() => {
  if (!slug.value) return false
  return /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/.test(slug.value) && slug.value.length >= 3
})

// Validation message
const validationMessage = computed(() => {
  if (!slug.value) return 'Enter a URL slug'
  if (slug.value.length < 3) return 'Slug must be at least 3 characters'
  if (!/^[a-z0-9]/.test(slug.value)) return 'Slug must start with a letter or number'
  if (!/[a-z0-9]$/.test(slug.value)) return 'Slug must end with a letter or number'
  if (!/^[a-z0-9-]*$/.test(slug.value)) return 'Only lowercase letters, numbers, and hyphens allowed'
  if (validationError.value) return validationError.value
  return null
})

// Initialize form when dialog opens
watch(isVisible, (visible) => {
  if (visible) {
    // Set initial slug from current or generate from app name
    if (props.currentSlug) {
      slug.value = props.currentSlug
    } else if (props.appName) {
      slug.value = appsService.generateSlug(props.appName)
    } else {
      slug.value = ''
    }

    requiresAuthentication.value = props.requiresAuth ?? false
    validationError.value = null
    isSlugAvailable.value = true
  }
})

// Handle publish
async function handlePublish() {
  if (!isValidSlug.value) return

  emit('publish', {
    publicUrl: slug.value,
    requiresAuthentication: requiresAuthentication.value,
  })
}

// Handle slug change - auto-format
function handleSlugInput(event: Event) {
  const input = event.target as HTMLInputElement
  // Convert to lowercase and replace spaces/invalid chars with hyphens
  slug.value = input.value
    .toLowerCase()
    .replace(/[^a-z0-9-]/g, '-')
    .replace(/-+/g, '-')
}

// Close dialog
function close() {
  isVisible.value = false
}
</script>

<template>
  <Dialog
    v-model:visible="isVisible"
    header="Publish App"
    :modal="true"
    :closable="true"
    :style="{ width: '450px' }"
  >
    <div class="publish-dialog">
      <!-- URL Input -->
      <div class="field mb-4">
        <label for="slug" class="block font-semibold mb-2">Public URL</label>
        <div class="p-inputgroup">
          <span class="p-inputgroup-addon">/web/</span>
          <InputText
            id="slug"
            v-model="slug"
            placeholder="my-app"
            :class="{ 'p-invalid': validationMessage && slug.length > 0 }"
            @input="handleSlugInput"
          />
        </div>
        <small v-if="validationMessage" class="p-error block mt-1">
          {{ validationMessage }}
        </small>
        <small v-else-if="isValidSlug" class="text-color-secondary block mt-1">
          Your app will be available at: {{ publicUrl }}
        </small>
      </div>

      <!-- Authentication checkbox -->
      <div class="field mb-4">
        <div class="flex align-items-center gap-2">
          <Checkbox
            id="requiresAuth"
            v-model="requiresAuthentication"
            :binary="true"
          />
          <label for="requiresAuth">Require authentication</label>
        </div>
        <small class="text-color-secondary block mt-1 ml-6">
          {{ requiresAuthentication
            ? 'Users must be logged in to access this app'
            : 'Anyone with the link can access this app' }}
        </small>
      </div>

      <!-- Warning for public apps -->
      <Message
        v-if="!requiresAuthentication"
        severity="warn"
        :closable="false"
        class="mb-4"
      >
        <i class="pi pi-exclamation-triangle mr-2"></i>
        Public apps can be accessed by anyone with the URL. Make sure you're not exposing sensitive data.
      </Message>
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
          label="Publish"
          icon="pi pi-upload"
          :disabled="!isValidSlug || isValidating"
          :loading="isValidating"
          @click="handlePublish"
        />
      </div>
    </template>
  </Dialog>
</template>

<style scoped>
.publish-dialog {
  padding: 0.5rem 0;
}
</style>
