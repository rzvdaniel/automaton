<script setup lang="ts">
/**
 * AppSettings.vue
 * Settings panel for app configuration
 * Includes: Basic info, Publishing, Runner, Appearance
 */
import { ref, computed, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import InputSwitch from 'primevue/inputswitch'
import Button from 'primevue/button'
import FileUpload from 'primevue/fileupload'
import Image from 'primevue/image'
import Message from 'primevue/message'
import RunnerSelector from '@/shared/components/ui/RunnerSelector.vue'
import { useAppEditorStore } from '../stores/app-editor.store'
import { appsService } from '../services/apps.service'

const toast = useToast()
const editorStore = useAppEditorStore()

// Active settings section
const activeSection = ref<'basic' | 'publishing' | 'execution' | 'appearance'>('basic')

// Local form state
const name = ref('')
const displayName = ref('')
const description = ref('')
const publicUrl = ref('')
const requiresAuthentication = ref(false)
const executionRunnerId = ref<string | null>(null)
const backgroundImageUrl = ref<string | undefined>(undefined)

// Loading states
const isSaving = ref(false)
const isUploadingImage = ref(false)
const isRemovingImage = ref(false)

// Computed
const app = computed(() => editorStore.currentApp)
const isPublished = computed(() => app.value?.isPublished ?? false)
const hasChanges = computed(() => {
  if (!app.value) return false
  return (
    name.value !== app.value.name ||
    displayName.value !== (app.value.displayName || '') ||
    description.value !== (app.value.description || '') ||
    publicUrl.value !== (app.value.publicUrl || '') ||
    requiresAuthentication.value !== app.value.requiresAuthentication ||
    executionRunnerId.value !== (app.value.executionRunnerId || null) ||
    backgroundImageUrl.value !== app.value.appearance?.backgroundImageUrl
  )
})

const fullPublicUrl = computed(() => {
  if (!publicUrl.value) return ''
  const baseUrl = window.location.origin
  return `${baseUrl}/web/${publicUrl.value}`
})

// Initialize form from app data
watch(
  app,
  (newApp) => {
    if (newApp) {
      name.value = newApp.name || ''
      displayName.value = newApp.displayName || ''
      description.value = newApp.description || ''
      publicUrl.value = newApp.publicUrl || ''
      requiresAuthentication.value = newApp.requiresAuthentication || false
      executionRunnerId.value = newApp.executionRunnerId || null
      backgroundImageUrl.value = newApp.appearance?.backgroundImageUrl
    }
  },
  { immediate: true },
)

// Generate URL slug from name
function generateSlug() {
  if (!name.value) return
  publicUrl.value = name.value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

// Copy public URL to clipboard
async function copyPublicUrl() {
  if (!fullPublicUrl.value) return
  try {
    await navigator.clipboard.writeText(fullPublicUrl.value)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'Public URL copied to clipboard',
      life: 2000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to copy URL',
      life: 3000,
    })
  }
}

// Open public URL in new tab
function openPublicUrl() {
  if (fullPublicUrl.value) {
    window.open(fullPublicUrl.value, '_blank')
  }
}

// Save settings
async function saveSettings() {
  if (!app.value) return

  isSaving.value = true
  try {
    await appsService.updateSettings(app.value.id, {
      name: name.value,
      displayName: displayName.value || undefined,
      description: description.value || undefined,
      publicUrl: publicUrl.value || undefined,
      requiresAuthentication: requiresAuthentication.value,
      executionRunnerId: executionRunnerId.value || undefined,
    })

    // Update store's app data directly without full reload
    if (editorStore.currentApp) {
      editorStore.currentApp.name = name.value
      editorStore.currentApp.displayName = displayName.value
      editorStore.currentApp.description = description.value
      editorStore.currentApp.publicUrl = publicUrl.value
      editorStore.currentApp.requiresAuthentication = requiresAuthentication.value
      editorStore.currentApp.executionRunnerId = executionRunnerId.value || undefined
    }

    toast.add({
      severity: 'success',
      summary: 'Saved',
      detail: 'App settings saved successfully',
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to save settings',
      life: 5000,
    })
  } finally {
    isSaving.value = false
  }
}

// Handle image upload
async function handleImageUpload(event: { files: File[] }) {
  if (!app.value || !event.files.length) return

  const file = event.files[0]

  // Validate file type
  if (!file.type.startsWith('image/')) {
    toast.add({
      severity: 'error',
      summary: 'Invalid File',
      detail: 'Please select an image file',
      life: 3000,
    })
    return
  }

  // Validate file size (max 2MB)
  if (file.size > 2 * 1024 * 1024) {
    toast.add({
      severity: 'error',
      summary: 'File Too Large',
      detail: 'Image must be less than 2MB',
      life: 3000,
    })
    return
  }

  isUploadingImage.value = true
  try {
    const imageUrl = await appsService.uploadThumbnail(app.value.id, file)
    backgroundImageUrl.value = imageUrl

    // Reload app to get updated appearance
    await editorStore.loadApp(app.value.id)

    toast.add({
      severity: 'success',
      summary: 'Uploaded',
      detail: 'App image uploaded successfully',
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Upload Failed',
      detail: err instanceof Error ? err.message : 'Failed to upload image',
      life: 5000,
    })
  } finally {
    isUploadingImage.value = false
  }
}

// Remove image
async function removeImage() {
  if (!app.value) return

  isRemovingImage.value = true
  try {
    await appsService.removeThumbnail(app.value.id)
    backgroundImageUrl.value = undefined

    // Reload app to get updated appearance
    await editorStore.loadApp(app.value.id)

    toast.add({
      severity: 'success',
      summary: 'Removed',
      detail: 'App image removed',
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to remove image',
      life: 5000,
    })
  } finally {
    isRemovingImage.value = false
  }
}
</script>

<template>
  <div class="app-settings">
    <div class="grid">
      <!-- Sidebar -->
      <div class="col-12 lg:col-3">
        <Card>
          <template #content>
            <div class="flex flex-column gap-1">
              <div class="text-color-secondary text-sm font-semibold mb-2 px-3">App Settings</div>
              <Button
                label="Basic Info"
                icon="pi pi-info-circle"
                text
                :class="{ 'surface-hover': activeSection === 'basic' }"
                class="w-full justify-content-start"
                @click="activeSection = 'basic'"
              />
              <Button
                label="Publishing"
                icon="pi pi-globe"
                text
                :class="{ 'surface-hover': activeSection === 'publishing' }"
                class="w-full justify-content-start"
                @click="activeSection = 'publishing'"
              />
              <Button
                label="Execution"
                icon="pi pi-server"
                text
                :class="{ 'surface-hover': activeSection === 'execution' }"
                class="w-full justify-content-start"
                @click="activeSection = 'execution'"
              />
              <Button
                label="Appearance"
                icon="pi pi-image"
                text
                :class="{ 'surface-hover': activeSection === 'appearance' }"
                class="w-full justify-content-start"
                @click="activeSection = 'appearance'"
              />
            </div>
          </template>
        </Card>
      </div>

      <!-- Content -->
      <div class="col-12 lg:col-9">
        <!-- Basic Information -->
        <Card v-if="activeSection === 'basic'">
          <template #title>Basic Information</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div class="flex flex-column gap-2">
                <label for="app-name" class="font-medium">Name *</label>
                <InputText
                  id="app-name"
                  v-model="name"
                  placeholder="Enter app name"
                  class="w-full"
                />
                <small class="text-color-secondary">Internal name used for identification</small>
              </div>

              <div class="flex flex-column gap-2">
                <label for="app-display-name" class="font-medium">Display Name</label>
                <InputText
                  id="app-display-name"
                  v-model="displayName"
                  placeholder="Enter display name"
                  class="w-full"
                />
                <small class="text-color-secondary">Shown on app cards and headers</small>
              </div>

              <div class="flex flex-column gap-2">
                <label for="app-description" class="font-medium">Description</label>
                <Textarea
                  id="app-description"
                  v-model="description"
                  rows="3"
                  placeholder="Describe what this app does..."
                  class="w-full"
                  :maxlength="500"
                />
                <small class="text-color-secondary">{{ description.length }}/500 characters</small>
              </div>

              <div class="flex justify-content-end pt-3 border-top-1 surface-border">
                <Button
                  label="Save Changes"
                  icon="pi pi-save"
                  :loading="isSaving"
                  :disabled="!hasChanges"
                  @click="saveSettings"
                />
              </div>
            </div>
          </template>
        </Card>

        <!-- Publishing Settings -->
        <Card v-if="activeSection === 'publishing'">
          <template #title>Publishing</template>
          <template #content>
            <Message v-if="isPublished" severity="success" :closable="false" class="mb-4">
              <div class="flex align-items-center gap-2">
                <i class="pi pi-check-circle"></i>
                <span>This app is published and accessible to users</span>
              </div>
            </Message>

            <div class="flex flex-column gap-4">
              <div class="flex flex-column gap-2">
                <label for="public-url" class="font-medium">Public URL Slug</label>
                <div class="p-inputgroup">
                  <span class="p-inputgroup-addon">/web/</span>
                  <InputText
                    id="public-url"
                    v-model="publicUrl"
                    placeholder="my-app"
                  />
                  <Button
                    v-tooltip="'Generate from name'"
                    icon="pi pi-sync"
                    severity="secondary"
                    outlined
                    @click="generateSlug"
                  />
                </div>
                <small class="text-color-secondary">
                  URL-friendly identifier for your app
                </small>
              </div>

              <div v-if="isPublished && publicUrl" class="flex flex-column gap-2">
                <label class="font-medium">Full URL</label>
                <div class="p-inputgroup">
                  <InputText :model-value="fullPublicUrl" readonly class="url-display" />
                  <Button
                    v-tooltip="'Copy URL'"
                    icon="pi pi-copy"
                    severity="secondary"
                    outlined
                    @click="copyPublicUrl"
                  />
                  <Button
                    v-tooltip="'Open in new tab'"
                    icon="pi pi-external-link"
                    severity="secondary"
                    outlined
                    @click="openPublicUrl"
                  />
                </div>
              </div>

              <div class="flex flex-column gap-2">
                <label for="public-access" class="font-medium">Access Control</label>
                <div class="flex align-items-center gap-3">
                  <InputSwitch
                    id="public-access"
                    :model-value="!requiresAuthentication"
                    @update:model-value="requiresAuthentication = !$event"
                  />
                  <span>{{ requiresAuthentication ? 'Requires login' : 'Public access' }}</span>
                </div>
                <small class="text-color-secondary">
                  {{ requiresAuthentication
                    ? 'Users must be logged in to access this app'
                    : 'Anyone with the link can access this app' }}
                </small>
              </div>

              <div class="flex justify-content-end pt-3 border-top-1 surface-border">
                <Button
                  label="Save Changes"
                  icon="pi pi-save"
                  :loading="isSaving"
                  :disabled="!hasChanges"
                  @click="saveSettings"
                />
              </div>
            </div>
          </template>
        </Card>

        <!-- Execution Settings -->
        <Card v-if="activeSection === 'execution'">
          <template #title>Execution</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div class="flex flex-column gap-2">
                <label for="execution-runner" class="font-medium">Default Runner</label>
                <RunnerSelector
                  id="execution-runner"
                  v-model="executionRunnerId"
                  class="w-full"
                />
                <small class="text-color-secondary">
                  Runner used for preview and step execution
                </small>
              </div>

              <div class="flex justify-content-end pt-3 border-top-1 surface-border">
                <Button
                  label="Save Changes"
                  icon="pi pi-save"
                  :loading="isSaving"
                  :disabled="!hasChanges"
                  @click="saveSettings"
                />
              </div>
            </div>
          </template>
        </Card>

        <!-- Appearance Settings -->
        <Card v-if="activeSection === 'appearance'">
          <template #title>Card Appearance</template>
          <template #content>
            <p class="text-color-secondary mb-4">
              Customize how this app appears on the Apps page
            </p>

            <div class="flex flex-column gap-4">
              <div class="flex flex-column gap-2">
                <label class="font-medium">App Thumbnail</label>

                <!-- Current image preview -->
                <div v-if="backgroundImageUrl" class="image-preview mb-3">
                  <Image
                    :src="backgroundImageUrl"
                    alt="App thumbnail"
                    preview
                    image-class="preview-image"
                  />
                  <Button
                    label="Remove Image"
                    icon="pi pi-trash"
                    severity="danger"
                    outlined
                    size="small"
                    :loading="isRemovingImage"
                    @click="removeImage"
                    class="mt-2"
                  />
                </div>

                <!-- Upload area -->
                <FileUpload
                  mode="basic"
                  accept="image/*"
                  :max-file-size="2000000"
                  choose-label="Upload Image"
                  :auto="true"
                  custom-upload
                  @uploader="handleImageUpload"
                  :disabled="isUploadingImage"
                  class="upload-button"
                />
                <small class="text-color-secondary block mt-2">
                  Recommended: 500x300px, PNG or JPG (max 2MB)
                </small>
              </div>

              <div class="flex justify-content-end pt-3 border-top-1 surface-border">
                <Button
                  label="Save Changes"
                  icon="pi pi-save"
                  :loading="isSaving"
                  :disabled="!hasChanges"
                  @click="saveSettings"
                />
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.app-settings {
  padding: 1.5rem;
  overflow-y: auto;
  height: 100%;
}

.url-display {
  font-family: monospace;
  font-size: 0.875rem;
}

.image-preview {
  max-width: 300px;
}

.image-preview :deep(.preview-image) {
  max-width: 100%;
  max-height: 200px;
  object-fit: cover;
  border-radius: var(--p-border-radius);
  border: 1px solid var(--p-surface-200);
}

.upload-button :deep(.p-button) {
  width: 100%;
  max-width: 200px;
}

/* Dark mode */
:global(.app-dark) .image-preview :deep(.preview-image) {
  border-color: var(--p-surface-600);
}
</style>
