<script setup lang="ts">
/**
 * AppSettings.vue
 * Settings panel for app configuration
 * Includes: App metadata, deployment targets, and appearance
 */
import { ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import InputSwitch from 'primevue/inputswitch'
import Select from 'primevue/select'
import Button from 'primevue/button'
import CopyCommand from './CopyCommand.vue'
import AppDeploymentTargets from './AppDeploymentTargets.vue'
import AppSecrets from './AppSecrets.vue'
import AppPermissions from './AppPermissions.vue'
import { useAppEditorStore } from '../stores/app-editor.store'
import { useAppsStore } from '../stores/apps.store'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { appsService } from '../services/apps.service'
import { AccessScope, type AppDeploymentTarget, type AppScreenshot } from '../types/apps.types'
import { resolveMediaUrl } from '../utils/media-url'

const toast = useToast()
const confirm = useConfirm()
const route = useRoute()
const router = useRouter()
const editorStore = useAppEditorStore()
const appsStore = useAppsStore()
const authStore = useAuthStore()
const props = defineProps<{ targetsRevision?: number }>()
const emit = defineEmits<{
  viewTargetLogs: [target: AppDeploymentTarget]
  targetsChanged: [targets: AppDeploymentTarget[]]
}>()

const isAdmin = computed(() => {
  const roles = authStore.user?.roles
  if (!roles) return false
  return roles.includes('Admin') || roles.includes('SysAdmin')
})

// Active settings section
const activeSection = ref<'basic' | 'targets' | 'secrets' | 'permissions' | 'appearance' | 'templates' | 'connect' | 'danger'>('basic')

watch(
  () => route.query.settings,
  (section) => {
    if (section === 'targets') activeSection.value = 'targets'
    if (section === 'permissions') activeSection.value = 'permissions'
  },
  { immediate: true },
)

// Connect section state
const copiedItem = ref<string | null>(null)
function copyText(text: string, key: string) {
  navigator.clipboard.writeText(text)
  copiedItem.value = key
  setTimeout(() => { copiedItem.value = null }, 2000)
}

const serverBase = computed(() => {
  const base = import.meta.env.VITE_API_BASE_URL || window.location.origin
  return base.replace(/\/$/, '')
})

const cliLoginCommand = computed(() =>
  `automaton login --server ${serverBase.value} --key your-api-key`
)

const linkAndDeployCommand = computed(() =>
  `automaton link ${app.value?.id ?? ''}\nautomaton deploy`
)

const starterPrompt = computed(() =>
  'Build [APP IDEA] in this local Automaton project.\n\nFirst read:\nhttps://automaton.monster/llms.txt\n\nUse the application contract in automaton.json to declare structured build and runtime commands, working directories, listening port, optional health path, environment, and persistent data directory. Choose a suitable technology without assuming a particular language, framework, package manager, or internal source layout. Keep the project source local and deploy it with Automaton CLI.'
)

// Local form state
const name = ref('')
const summary = ref('')
const description = ref('')
const category = ref<string | null>(null)
const categoryOptions = ref<string[]>([])
const marketplaceCategoryOptions = computed(() => {
  if (category.value && !categoryOptions.value.includes(category.value)) {
    return [...categoryOptions.value, category.value]
  }
  return categoryOptions.value
})
const isLoadingCategories = ref(false)
const isProfileFeatured = ref(false)
const thumbnailUrl = ref<string | undefined>(undefined)
const tags = ref<string[]>([])
const originalTags = ref<string[]>([])
const tagInput = ref('')
const tagSuggestions = ref<string[]>([])
const isLoadingTags = ref(false)
const maxScreenshots = 8

// Loading states
const isSaving = ref(false)
const isUploadingImage = ref(false)
const isRemovingImage = ref(false)
const isUploadingScreenshot = ref(false)
const isDraggingOver = ref(false)
const fileInputRef = ref<HTMLInputElement | null>(null)
const screenshotInputRef = ref<HTMLInputElement | null>(null)

function triggerFilePicker() {
  fileInputRef.value?.click()
}

function triggerScreenshotPicker() {
  screenshotInputRef.value?.click()
}

function onFileInputChange(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (file) handleImageUpload({ files: [file] })
  // reset so same file can be re-selected
  if (fileInputRef.value) fileInputRef.value.value = ''
}

function onDrop(e: DragEvent) {
  isDraggingOver.value = false
  const file = e.dataTransfer?.files?.[0]
  if (file) handleImageUpload({ files: [file] })
}

function onScreenshotInputChange(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (file) {
    void handleScreenshotUpload(file)
  }
  if (screenshotInputRef.value) {
    screenshotInputRef.value.value = ''
  }
}
const isDeleting = ref(false)

// Template (admin) state
const isSample = ref(false)
const isFeatured = ref(false)
const isSavingTemplate = ref(false)

// Computed
const app = computed(() => editorStore.currentApp)
const isLinkedToProject = computed(() => app.value?.scope === AccessScope.Project)
const screenshots = computed(() => app.value?.screenshots ?? [])
const canAddMoreScreenshots = computed(() => screenshots.value.length < maxScreenshots)

// Full thumbnail URL with API base URL
const fullThumbnailUrl = computed(() => {
  return resolveMediaUrl(thumbnailUrl.value)
})

const hasChanges = computed(() => {
  if (!app.value) return false
  return (
    name.value !== app.value.name ||
    summary.value !== (app.value.summary || '') ||
    description.value !== (app.value.description || '') ||
    category.value !== (app.value.category ?? null) ||
    isProfileFeatured.value !== (app.value.isProfileFeatured ?? false) ||
    thumbnailUrl.value !== (app.value.thumbnailUrl || undefined) ||
    !sameTags(tags.value, originalTags.value) ||
    pendingTags.value.length > 0
  )
})

// Initialize form from app data
function parseLegacyTags(value?: string | null): string[] {
  return (value ?? '').split(',').map((tag) => tag.trim()).filter(Boolean)
}

function sameTags(left: string[], right: string[]): boolean {
  return left.length === right.length && left.every((tag, index) => tag === right[index])
}

function getNewTags(value: string): string[] {
  const newTags: string[] = []

  for (const rawTag of value.split(',')) {
    const tag = rawTag.trim()
    if (
      tag &&
      !tags.value.some((existing) => existing.localeCompare(tag, undefined, { sensitivity: 'accent' }) === 0) &&
      !newTags.some((existing) => existing.localeCompare(tag, undefined, { sensitivity: 'accent' }) === 0)
    ) {
      newTags.push(tag)
    }
  }

  return newTags
}

const pendingTags = computed(() => getNewTags(tagInput.value))

function addTags(value: string) {
  tags.value.push(...getNewTags(value))
  tagInput.value = ''
}

function removeTag(tag: string) {
  tags.value = tags.value.filter((existing) => existing !== tag)
}

async function loadTagSettings(appId: string, projectId?: string) {
  isLoadingTags.value = true
  try {
    const settings = await appsService.getTagSettings(appId)
    if (app.value?.id === appId) {
      tags.value = [...settings.tags]
      originalTags.value = [...settings.tags]
    }
  } catch {
    if (app.value?.id === appId) {
      const fallbackTags = parseLegacyTags(app.value.tags)
      tags.value = fallbackTags
      originalTags.value = [...fallbackTags]
    }
  } finally {
    isLoadingTags.value = false
  }

  try {
    const suggestions = await appsService.getTagSuggestions(projectId)
    if (app.value?.id === appId) tagSuggestions.value = suggestions
  } catch {
    if (app.value?.id === appId) tagSuggestions.value = []
  }
}

async function loadCategories() {
  isLoadingCategories.value = true
  try {
    categoryOptions.value = await appsService.getMarketplaceCategories()
  } catch {
    categoryOptions.value = []
  } finally {
    isLoadingCategories.value = false
  }
}

watch(
  app,
  (newApp) => {
    if (newApp) {
      name.value = newApp.name || ''
      summary.value = newApp.summary || ''
      description.value = newApp.description || ''
      category.value = newApp.category ?? null
      isProfileFeatured.value = newApp.isProfileFeatured ?? false
      thumbnailUrl.value = newApp.thumbnailUrl ?? undefined
      isSample.value = newApp.isSample ?? false
      isFeatured.value = newApp.isFeatured ?? false
      const fallbackTags = parseLegacyTags(newApp.tags)
      tags.value = fallbackTags
      originalTags.value = [...fallbackTags]
      void loadTagSettings(newApp.id, newApp.projectId)
      void loadCategories()
    }
  },
  { immediate: true },
)

// Save settings
async function saveSettings() {
  if (!app.value) return

  addTags(tagInput.value)
  isSaving.value = true
  try {
    await appsService.updateSettings(app.value.id, {
      name: name.value,
      summary: summary.value || undefined,
      description: description.value || undefined,
      isProfileFeatured: isProfileFeatured.value,
      category: category.value,
    })
    await appsService.updateTagSettings(app.value.id, tags.value)
    originalTags.value = [...tags.value]

    // Update store's app data directly without full reload
    if (editorStore.currentApp) {
      editorStore.currentApp.name = name.value
      editorStore.currentApp.summary = summary.value
      editorStore.currentApp.description = description.value
      editorStore.currentApp.category = category.value
      editorStore.currentApp.isProfileFeatured = isProfileFeatured.value
      editorStore.currentApp.tags = tags.value.join(',') || null
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
  if (!file) return

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
    thumbnailUrl.value = imageUrl
    if (editorStore.currentApp) {
      editorStore.currentApp.thumbnailUrl = imageUrl
    }
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

function screenshotUrl(screenshot: AppScreenshot): string {
  return resolveMediaUrl(screenshot.imageUrl)
}

async function handleScreenshotUpload(file: File) {
  if (!app.value) return

  if (!file.type.startsWith('image/')) {
    toast.add({
      severity: 'error',
      summary: 'Invalid File',
      detail: 'Please select an image file',
      life: 3000,
    })
    return
  }

  if (file.size > 2 * 1024 * 1024) {
    toast.add({
      severity: 'error',
      summary: 'File Too Large',
      detail: 'Image must be less than 2MB',
      life: 3000,
    })
    return
  }

  isUploadingScreenshot.value = true
  try {
    const screenshot = await appsService.uploadScreenshot(app.value.id, file)
    if (editorStore.currentApp) {
      editorStore.currentApp.screenshots = [...(editorStore.currentApp.screenshots ?? []), screenshot]
    }

    toast.add({
      severity: 'success',
      summary: 'Uploaded',
      detail: 'Screenshot added to your app gallery',
      life: 2500,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Upload Failed',
      detail: err instanceof Error ? err.message : 'Failed to upload screenshot',
      life: 5000,
    })
  } finally {
    isUploadingScreenshot.value = false
  }
}

async function removeScreenshot(screenshotId: string) {
  if (!app.value) return

  try {
    await appsService.deleteScreenshot(app.value.id, screenshotId)
    if (editorStore.currentApp) {
      editorStore.currentApp.screenshots = (editorStore.currentApp.screenshots ?? []).filter(
        screenshot => screenshot.id !== screenshotId,
      )
    }

    toast.add({
      severity: 'success',
      summary: 'Removed',
      detail: 'Screenshot deleted',
      life: 2500,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to delete screenshot',
      life: 5000,
    })
  }
}

// Remove image
async function removeImage() {
  if (!app.value) return

  isRemovingImage.value = true
  try {
    await appsService.removeThumbnail(app.value.id)
    thumbnailUrl.value = undefined
    if (editorStore.currentApp) {
      editorStore.currentApp.thumbnailUrl = undefined
    }
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

function handleDelete() {
  if (!app.value) return
  confirm.require({
    message: `Are you sure you want to delete "${app.value.name}"? This action cannot be undone.`,
    header: 'Confirm Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      isDeleting.value = true
      try {
        await appsStore.deleteApp(app.value!.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: `"${app.value!.name}" has been deleted`, life: 3000 })
        router.push('/apps')
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to delete app',
          life: 5000,
        })
      } finally {
        isDeleting.value = false
      }
    },
  })
}

async function handleSaveTemplateStatus() {
  if (!app.value) return
  isSavingTemplate.value = true
  try {
    await appsService.setTemplateStatus(app.value.id, isSample.value, isFeatured.value)
    toast.add({
      severity: 'success',
      summary: 'Saved',
      detail: isSample.value ? 'App is now a template' : 'App removed from templates',
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to update template status',
      life: 5000,
    })
  } finally {
    isSavingTemplate.value = false
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
                label="Environments"
                icon="pi pi-server"
                text
                :class="{ 'surface-hover': activeSection === 'targets' }"
                class="w-full justify-content-start"
                @click="activeSection = 'targets'"
              />
              <Button
                label="Secrets"
                icon="pi pi-lock"
                text
                :class="{ 'surface-hover': activeSection === 'secrets' }"
                class="w-full justify-content-start"
                @click="activeSection = 'secrets'"
              />
              <Button
                label="Permissions"
                icon="pi pi-shield"
                text
                :class="{ 'surface-hover': activeSection === 'permissions' }"
                class="w-full justify-content-start"
                @click="activeSection = 'permissions'"
              />
              <Button
                label="Appearance"
                icon="pi pi-image"
                text
                :class="{ 'surface-hover': activeSection === 'appearance' }"
                class="w-full justify-content-start"
                @click="activeSection = 'appearance'"
              />
      
              <Button
                v-if="isAdmin"
                label="Templates"
                icon="pi pi-star"
                text
                :class="{ 'surface-hover': activeSection === 'templates' }"
                class="w-full justify-content-start"
                @click="activeSection = 'templates'"
              />

              <div class="border-top-1 surface-border my-2" />
              <Button
                label="Local project"
                icon="pi pi-link"
                text
                :class="{ 'surface-hover': activeSection === 'connect' }"
                class="w-full justify-content-start"
                @click="activeSection = 'connect'"
              />

              <div v-if="!isLinkedToProject" class="border-top-1 surface-border my-2" />
              <Button
                v-if="!isLinkedToProject"
                label="Danger Zone"
                icon="pi pi-exclamation-triangle"
                text
                :class="{ 'surface-hover': activeSection === 'danger' }"
                class="w-full justify-content-start text-red-500"
                @click="activeSection = 'danger'"
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
                <label for="app-display-name" class="font-medium">Summary</label>
                <InputText
                  id="app-display-name"
                  v-model="summary"
                  placeholder="Brief summary of the app"
                  class="w-full"
                />
                <small class="text-color-secondary">Brief description shown on app cards (recommended)</small>
              </div>

              <div class="flex flex-column gap-2">
                <label for="app-description" class="font-medium">Description</label>
                <Textarea
                  id="app-description"
                  v-model="description"
                  rows="3"
                  placeholder="Full description of what this app does..."
                  class="w-full"
                  :maxlength="500"
                />
                <small class="text-color-secondary">Full description shown in app details • {{ description.length }}/500 characters</small>
              </div>

              <div class="flex flex-column gap-2">
                <label for="app-category" class="font-medium">Marketplace category</label>
                <Select
                  id="app-category"
                  v-model="category"
                  :options="marketplaceCategoryOptions"
                  placeholder="Select a category"
                  class="w-full"
                  :loading="isLoadingCategories"
                  :disabled="!marketplaceCategoryOptions.length"
                  show-clear
                />
                <small class="text-color-secondary">Organizes this app in the public marketplace. Publishing and runtime visibility are managed separately.</small>
              </div>

              <div class="flex flex-column gap-2">
                <label for="app-tags" class="font-medium">Tags</label>
                <div v-if="tags.length" class="flex flex-wrap gap-2">
                  <span v-for="tag in tags" :key="tag" class="tag-editor-chip">
                    {{ tag }}
                    <Button
                      :aria-label="`Remove ${tag} tag`"
                      icon="pi pi-times"
                      text
                      rounded
                      size="small"
                      @click="removeTag(tag)"
                    />
                  </span>
                </div>
                <InputText
                  id="app-tags"
                  v-model="tagInput"
                  list="app-tag-suggestions"
                  placeholder="Add a tag and press Enter"
                  class="w-full"
                  :disabled="isLoadingTags"
                  @keydown.enter.prevent="addTags(tagInput)"
                  @blur="addTags(tagInput)"
                />
                <datalist id="app-tag-suggestions">
                  <option v-for="suggestion in tagSuggestions" :key="suggestion" :value="suggestion" />
                </datalist>
                <small class="text-color-secondary">Choose a suggested tag or add your own. Press Enter to add it.</small>
              </div>

              <div class="flex flex-column gap-2">
                <label for="profile-featured" class="font-medium">Creator Store Spotlight</label>
                <div class="flex align-items-center gap-3">
                  <InputSwitch id="profile-featured" v-model="isProfileFeatured" />
                  <span>{{ isProfileFeatured ? 'Featured on my creator profile' : 'Use normal profile ranking' }}</span>
                </div>
                <small class="text-color-secondary">
                  Featured apps appear in the carousel on your public creator store. If none are selected, Automaton uses your top published apps.
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

        <Card v-if="activeSection === 'targets'">
          <template #content>
            <AppDeploymentTargets
              v-if="app"
              :app-id="app.id"
              :project-id="app.projectId"
              :refresh-revision="props.targetsRevision"
              :show-deploy-action="false"
              @view-logs="emit('viewTargetLogs', $event)"
              @targets-changed="emit('targetsChanged', $event)"
            />
          </template>
        </Card>

        <Card v-if="activeSection === 'secrets'">
          <template #title>Secrets</template>
          <template #content>
            <AppSecrets v-if="app" :app-id="app.id" />
          </template>
        </Card>

        <Card v-if="activeSection === 'permissions'">
          <template #title>Permissions</template>
          <template #content>
            <AppPermissions v-if="app" :app-id="app.id" />
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
                <label class="font-medium">Cover Image</label>

                <!-- Unified drop zone — click or drag to upload; × to remove -->
                <div
                  class="cover-drop-zone"
                  :class="{ 'is-dragging': isDraggingOver }"
                  @click="!thumbnailUrl && triggerFilePicker()"
                  @dragover.prevent="isDraggingOver = true"
                  @dragleave="isDraggingOver = false"
                  @drop.prevent="onDrop"
                >
                  <!-- Uploaded image — contained, not stretched -->
                  <img v-if="thumbnailUrl" :src="fullThumbnailUrl" alt="App cover" class="cover-img" />

                  <!-- Uploading spinner -->
                  <div v-if="isUploadingImage" class="cover-overlay">
                    <i class="pi pi-spin pi-spinner cover-spinner"></i>
                  </div>

                  <!-- Hover actions when image is set -->
                  <div v-else-if="thumbnailUrl" class="cover-hover-actions">
                    <button class="cover-btn" title="Change image" @click.stop="triggerFilePicker()">
                      <i class="pi pi-camera"></i>
                      <span>Change</span>
                    </button>
                    <button
                      class="cover-btn cover-btn-danger"
                      title="Remove image"
                      :disabled="isRemovingImage"
                      @click.stop="removeImage"
                    >
                      <i :class="isRemovingImage ? 'pi pi-spin pi-spinner' : 'pi pi-trash'"></i>
                      <span>Remove</span>
                    </button>
                  </div>

                  <!-- Empty state prompt -->
                  <div v-else class="cover-empty-hint">
                    <i class="pi pi-cloud-upload"></i>
                    <span>Click or drag &amp; drop</span>
                    <small>PNG · JPG · WEBP</small>
                  </div>
                </div>

                <!-- Hidden native file input -->
                <input
                  ref="fileInputRef"
                  type="file"
                  accept="image/*"
                  style="display:none"
                  @change="onFileInputChange"
                />

                <small class="text-color-secondary">Recommended 16:9 · max 2 MB</small>
              </div>

              <div class="flex flex-column gap-3">
                <div class="flex align-items-center justify-content-between gap-3 flex-wrap">
                  <div>
                    <label class="font-medium">Screenshots</label>
                    <div class="text-color-secondary text-sm mt-1">
                      Add up to {{ maxScreenshots }} screenshots for your app gallery and marketplace previews.
                    </div>
                  </div>
                  <Button
                    label="Add Screenshot"
                    icon="pi pi-images"
                    severity="secondary"
                    outlined
                    :disabled="!canAddMoreScreenshots || isUploadingScreenshot"
                    :loading="isUploadingScreenshot"
                    @click="triggerScreenshotPicker"
                  />
                </div>

                <input
                  ref="screenshotInputRef"
                  type="file"
                  accept="image/*"
                  style="display:none"
                  @change="onScreenshotInputChange"
                />

                <div v-if="screenshots.length > 0" class="screenshots-grid">
                  <article
                    v-for="screenshot in screenshots"
                    :key="screenshot.id"
                    class="screenshot-card"
                  >
                    <img
                      :src="screenshotUrl(screenshot)"
                      :alt="`${app?.name || 'App'} screenshot`"
                      class="screenshot-img"
                    />
                    <button
                      class="screenshot-delete"
                      title="Remove screenshot"
                      @click="removeScreenshot(screenshot.id)"
                    >
                      <i class="pi pi-trash"></i>
                    </button>
                  </article>
                </div>
                <div v-else class="screenshots-empty">
                  <i class="pi pi-images"></i>
                  <span>No screenshots yet</span>
                  <small>Upload a few product shots so users can see what your app feels like before opening it.</small>
                </div>

                <small class="text-color-secondary">
                  Best results: landscape images, under 2 MB each.
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

        <!-- Local project -->
        <Card v-if="activeSection === 'connect'">
          <template #title>Local project</template>
          <template #content>
            <div class="flex flex-column gap-5">
              <p class="text-color-secondary mt-0 mb-0">
                Your local project owns this app's source. Use Automaton CLI to pull, edit, link, and deploy. Use Studio to manage its runtime and publishing.
              </p>

              <!-- CLI setup -->
              <div>
                <div class="font-semibold mb-1">1. Set up the CLI</div>
                <p class="text-color-secondary text-sm mb-2">
                  Install the CLI once, then sign in with an API key that can deploy apps.
                </p>
                <div class="connect-snippet">
                  <pre>dotnet tool install -g Automaton.Cli</pre>
                  <CopyCommand
                    command="dotnet tool install -g Automaton.Cli"
                    button-label="Copy"
                    :show-command="false"
                    class="connect-copy-command"
                  />
                </div>
                <div class="connect-snippet mt-2">
                  <pre>{{ cliLoginCommand }}</pre>
                  <CopyCommand
                    :command="cliLoginCommand"
                    button-label="Copy"
                    :show-command="false"
                    class="connect-copy-command"
                  />
                </div>
              </div>

              <div v-if="app?.sourceAvailable">
                <div class="font-semibold mb-1">2. Pull this app's current source</div>
                <p class="text-color-secondary text-sm mb-2">
                  Run this from a folder where the CLI can create a new project directory. It pulls the current source and contract.
                </p>
                <div class="connect-snippet mt-2">
                  <pre>automaton pull {{ app?.id }}</pre>
                  <CopyCommand
                    :command="`automaton pull ${app?.id ?? ''}`"
                    button-label="Copy"
                    :show-command="false"
                    class="connect-copy-command"
                  />
                </div>
              </div>
              <p v-else class="text-color-secondary text-sm m-0">
                This app has no current source available to pull. Link a local project that already has source below.
              </p>

              <div>
                <div class="font-semibold mb-1">
                  {{ app?.sourceAvailable ? '3. Link a project that already has source' : '2. Link a project that already has source' }}
                </div>
                <p class="text-color-secondary text-sm mb-2">
                  Run these commands from a local project folder that already contains this app's source. Linking records the app connection. It does not download source files.
                </p>
                <div class="connect-snippet">
                  <pre>{{ linkAndDeployCommand }}</pre>
                  <CopyCommand
                    :command="linkAndDeployCommand"
                    button-label="Copy"
                    :show-command="false"
                    class="connect-copy-command"
                  />
                </div>
              </div>

              <div>
                <div class="font-semibold mb-1">Optional prompt for your local AI</div>
                <p class="text-color-secondary text-sm mb-2">
                  Use this prompt in an AI tool while working in your local project. Studio does not create or edit app source.
                </p>
                <div class="connect-snippet">
                  <pre>{{ starterPrompt }}</pre>
                  <Button
                    :icon="copiedItem === 'prompt' ? 'pi pi-check' : 'pi pi-copy'"
                    aria-label="Copy local AI prompt"
                    text rounded size="small" class="connect-copy-btn"
                    @click="copyText(starterPrompt, 'prompt')"
                  />
                </div>
              </div>

              <!-- Contract reference -->
              <div>
                <div class="font-semibold mb-1">Generic application contract</div>
                <div class="connect-tree">
                  <div><span class="ct-file">automaton.json</span> <span class="ct-comment">← application contract</span></div>
                  <div><span class="ct-dir">application</span> <span class="ct-comment">← build/runtime/health/storage contract</span></div>
                  <div class="pl-3"><span class="ct-file">build.command</span> <span class="ct-comment">← executable, arguments, working directory</span></div>
                  <div class="pl-3"><span class="ct-file">runtime.command</span> <span class="ct-comment">← process that serves the application</span></div>
                  <div class="pl-3"><span class="ct-file">runtime.port</span> <span class="ct-comment">← listening port</span></div>
                  <div class="pl-3"><span class="ct-file">health.path</span> <span class="ct-comment">← optional HTTP readiness path</span></div>
                  <div class="pl-3"><span class="ct-file">storage.dataDirectory</span> <span class="ct-comment">← persistent application data</span></div>
                </div>
                <p class="text-color-secondary text-sm mt-2 mb-0">
                  The source layout is your choice. Automaton packages the project, runs the declared build when present, starts the declared runtime, and exposes its healthy endpoint through the deployment target.
                </p>
                <p class="text-color-secondary text-sm mt-2 mb-0">
                  Durable files such as databases, uploads, and generated exports belong under the configured persistent data directory. Framework-specific templates are optional examples, not platform requirements.
                </p>
              </div>
            </div>
          </template>
        </Card>

        <!-- Danger Zone -->
        <Card v-if="activeSection === 'danger'">          <template #title>
            <span class="text-red-500">Danger Zone</span>
          </template>
          <template #content>
            <div class="danger-zone-card">
              <div class="flex align-items-center justify-content-between gap-4">
                <div>
                  <div class="font-medium">Delete this app</div>
                  <small class="text-color-secondary">Once deleted, all app files and data will be permanently removed. This action cannot be undone.</small>
                </div>
                <Button
                  label="Delete App"
                  icon="pi pi-trash"
                  severity="danger"
                  outlined
                  :loading="isDeleting"
                  @click="handleDelete"
                />
              </div>
            </div>
          </template>
        </Card>

        <!-- Templates (Admin only) -->
        <Card v-if="activeSection === 'templates' && isAdmin">
          <template #title>Templates</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <p class="text-color-secondary mt-0 mb-0">
                Mark this app as a template so users can remix it into their own account from the Templates gallery.
                The app must be published before it can appear as a template.
              </p>
              <div class="flex align-items-center gap-3">
                <InputSwitch v-model="isSample" input-id="is-sample" />
                <label for="is-sample" class="cursor-pointer">
                  <span class="font-medium">Publish as Template</span>
                  <div class="text-color-secondary text-sm">Shows in the Templates tab for all users</div>
                </label>
              </div>
              <div class="flex align-items-center gap-3">
                <InputSwitch v-model="isFeatured" input-id="is-featured" :disabled="!isSample" />
                <label for="is-featured" class="cursor-pointer" :class="{ 'text-color-secondary': !isSample }">
                  <span class="font-medium">Featured</span>
                  <div class="text-color-secondary text-sm">Pinned to the top of the Templates gallery</div>
                </label>
              </div>
              <div>
                <Button
                  label="Save"
                  icon="pi pi-save"
                  :loading="isSavingTemplate"
                  @click="handleSaveTemplateStatus"
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
.tag-editor-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.125rem;
  padding: 0.125rem 0.25rem 0.125rem 0.625rem;
  border-radius: 999px;
  background: var(--p-primary-100);
  color: var(--p-primary-800);
  font-size: 0.875rem;
}
</style>

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

.url-inputgroup :deep(.p-button) {
  margin-left: 0.5rem;
}

/* Cover drop zone */
.cover-drop-zone {
  position: relative;
  width: 50%;
  max-width: 320px;
  padding-bottom: 28.125%; /* 16:9 relative to parent — width:50% × 56.25% */
  border-radius: 10px;
  overflow: hidden;
  cursor: pointer;
  border: 2px dashed var(--p-surface-300);
  background: var(--p-surface-ground);
  transition: border-color 0.2s ease, background 0.2s ease;
}

:global(.app-dark) .cover-drop-zone {
  border-color: var(--p-surface-600);
}

.cover-drop-zone:hover,
.cover-drop-zone.is-dragging {
  border-color: var(--p-primary-400);
  background: color-mix(in srgb, var(--p-primary-500) 6%, var(--p-surface-ground));
}

.cover-img {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: contain;
  padding: 0.5rem;
}

/* Full-cover spinner overlay */
.cover-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 20;
}

.cover-spinner {
  font-size: 1.75rem;
  color: white;
}

/* Hover action buttons shown over the image */
.cover-hover-actions {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  background: rgba(0, 0, 0, 0.45);
  opacity: 0;
  transition: opacity 0.2s ease;
  z-index: 10;
}

.cover-drop-zone:hover .cover-hover-actions {
  opacity: 1;
}

.cover-btn {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.3rem;
  padding: 0.6rem 1rem;
  border-radius: 8px;
  border: none;
  background: rgba(255, 255, 255, 0.18);
  backdrop-filter: blur(6px);
  color: white;
  font-size: 0.8rem;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.15s ease;
}

.cover-btn i {
  font-size: 1.1rem;
}

.cover-btn:hover {
  background: rgba(255, 255, 255, 0.32);
}

.cover-btn-danger:hover {
  background: rgba(220, 38, 38, 0.7);
}

/* Empty state hint */
.cover-empty-hint {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  color: var(--p-text-muted-color);
  pointer-events: none;
}

.cover-empty-hint i {
  font-size: 2rem;
  opacity: 0.5;
}

.cover-empty-hint span {
  font-size: 0.9rem;
  font-weight: 500;
}

.cover-empty-hint small {
  font-size: 0.75rem;
  opacity: 0.6;
}

.screenshots-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 0.9rem;
}

.screenshot-card {
  position: relative;
  border-radius: 12px;
  overflow: hidden;
  border: 1px solid var(--p-surface-300);
  background: var(--p-surface-50);
  aspect-ratio: 16 / 9;
}

:global(.app-dark) .screenshot-card {
  border-color: var(--p-surface-700);
  background: var(--p-surface-900);
}

.screenshot-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.screenshot-delete {
  position: absolute;
  top: 0.6rem;
  right: 0.6rem;
  width: 2rem;
  height: 2rem;
  border: none;
  border-radius: 999px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.56);
  color: white;
  cursor: pointer;
  transition: background 0.18s ease;
}

.screenshot-delete:hover {
  background: rgba(185, 28, 28, 0.9);
}

.screenshots-empty {
  border: 1px dashed var(--p-surface-300);
  border-radius: 12px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
  color: var(--p-text-muted-color);
  align-items: flex-start;
}

:global(.app-dark) .screenshots-empty {
  border-color: var(--p-surface-700);
}

.screenshots-empty i {
  font-size: 1.4rem;
  opacity: 0.75;
}

.analytics-section {
  padding: 1rem;
  border-radius: var(--p-border-radius);
  border: 1px solid var(--p-surface-200);
}

.analytics-stat {
  text-align: center;
  padding: 0.5rem 1.5rem;
}

.analytics-stat .stat-value {
  font-size: 1.5rem;
  font-weight: 600;
  color: var(--p-primary-500);
}

.analytics-stat .stat-label {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  margin-top: 0.25rem;
}

:global(.app-dark) .analytics-section {
  border-color: var(--p-surface-600);
}

.danger-zone-card {
  border: 1px solid var(--p-red-200);
  border-radius: 8px;
  padding: 1rem 1.25rem;
  background: color-mix(in srgb, var(--p-red-500) 4%, var(--p-surface-ground));
}

:global(.app-dark) .danger-zone-card {
  border-color: var(--p-red-800);
  background: color-mix(in srgb, var(--p-red-900) 20%, var(--p-surface-ground));
}

/* ── Local project guidance ── */
.connect-snippet {
  position: relative;
  background: var(--p-surface-900, #111);
  border: 1px solid var(--p-surface-border);
  border-radius: 8px;
  padding: 12px 40px 12px 14px;
  overflow-x: auto;
}

:global(.app-dark) .connect-snippet {
  background: color-mix(in srgb, var(--p-surface-900) 90%, black);
}

.connect-snippet pre {
  margin: 0;
  font-family: var(--font-mono, monospace);
  font-size: 0.8rem;
  line-height: 1.55;
  color: #8ef0a1;
  white-space: pre;
}

.connect-copy-btn {
  position: absolute;
  top: 6px;
  right: 6px;
  opacity: 0.6;
}

.connect-copy-btn:hover {
  opacity: 1;
}

.connect-copy-command {
  position: absolute;
  top: 6px;
  right: 6px;
}

.connect-copy-command :deep(.copy-command__button) {
  color: #8ef0a1;
  font-size: 0.75rem;
}

.connect-tree {
  background: var(--p-surface-900, #111);
  border: 1px solid var(--p-surface-border);
  border-radius: 8px;
  padding: 12px 16px;
  font-family: var(--font-mono, monospace);
  font-size: 0.8rem;
  line-height: 1.8;
}

:global(.app-dark) .connect-tree {
  background: color-mix(in srgb, var(--p-surface-900) 90%, black);
}

.ct-dir  { color: #79c0ff; }
.ct-file { color: #c9d1d9; }
.ct-comment { color: #6a737d; }
</style>
