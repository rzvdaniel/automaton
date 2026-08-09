<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Select from 'primevue/select'
import apiClient from '@/shared/services/api/axios-instance'
import { appsService } from '../services/apps.service'
import type { PublishAppRequest } from '../types/apps.types'
import { getPublishedAppUrl, getPublishedAppsUrlPrefix } from '../utils/public-app-url'
import { getPublishVisibilityState } from '../utils/publish-visibility'

const props = defineProps<{
  visible: boolean
  currentSlug?: string
  currentCategory?: string | null
  currentTags?: string | null
  requiresAuth?: boolean
  isForkable?: boolean
  currentRuntimeVisibility?: 'private' | 'public'
  currentCatalogVisibility?: 'unlisted' | 'listed'
  appName?: string
  liveTargetName?: string
  liveTargetSlug?: string
  viewingTargetName?: string
  viewingTargetSlug?: string
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  publish: [request: PublishAppRequest]
}>()

const slug = ref('')
const requiresAuthentication = ref(false)
const allowRemixing = ref(false)
const publishRuntime = ref(true)
const listInMarketplace = ref(true)
const selectedCategory = ref<string | null>(null)
const tags = ref('')
const validationError = ref<string | null>(null)
const categories = ref<string[]>([])
const isLoadingCategories = ref(false)

const isVisible = computed({
  get: () => props.visible,
  set: (value) => emit('update:visible', value),
})

const publicUrl = computed(() => getPublishedAppUrl(slug.value || 'your-app'))

const isValidSlug = computed(() => {
  if (!slug.value) return false
  return /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/.test(slug.value) && slug.value.length >= 3
})

const validationMessage = computed(() => {
  if (!slug.value) return 'Enter a URL slug'
  if (slug.value.length < 3) return 'Slug must be at least 3 characters'
  if (!/^[a-z0-9]/.test(slug.value)) return 'Slug must start with a letter or number'
  if (!/[a-z0-9]$/.test(slug.value)) return 'Slug must end with a letter or number'
  if (!/^[a-z0-9-]*$/.test(slug.value)) return 'Only lowercase letters, numbers, and hyphens allowed'
  if (validationError.value) return validationError.value
  return null
})

const categoryOptions = computed(() => categories.value.map(category => ({
  label: category,
  value: category,
})))

watch(isVisible, async (visible) => {
  if (!visible) {
    return
  }

  if (props.currentSlug) {
    slug.value = props.currentSlug
  } else if (props.appName) {
    slug.value = appsService.generateSlug(props.appName)
  } else {
    slug.value = ''
  }

  requiresAuthentication.value = false
  allowRemixing.value = props.isForkable ?? false
  const visibilityState = getPublishVisibilityState(
    props.currentRuntimeVisibility,
    props.currentCatalogVisibility,
  )
  publishRuntime.value = visibilityState.publishRuntime
  listInMarketplace.value = visibilityState.listInMarketplace
  selectedCategory.value = props.currentCategory ?? null
  tags.value = props.currentTags ?? ''
  validationError.value = null

  await loadCategories()
})

async function loadCategories() {
  isLoadingCategories.value = true
  try {
    const response = await apiClient.get<string[]>('/api/public/marketplace/categories')
    categories.value = response.data ?? []
  } catch {
    categories.value = []
  } finally {
    isLoadingCategories.value = false
  }
}

function handlePublish() {
  if (!isValidSlug.value) return

  emit('publish', {
    publicUrl: slug.value,
    requiresAuthentication: requiresAuthentication.value,
    category: selectedCategory.value,
    tags: tags.value.trim() || null,
    isForkable: allowRemixing.value,
    runtimeVisibility: publishRuntime.value ? 'public' : 'private',
    catalogVisibility: listInMarketplace.value ? 'listed' : 'unlisted',
    marketplacePreviewMode: publishRuntime.value ? 'launchable' : 'mediaOnly',
  })
}

function handleSlugInput(event: Event) {
  const input = event.target as HTMLInputElement
  slug.value = input.value
    .toLowerCase()
    .replace(/[^a-z0-9-]/g, '-')
    .replace(/-+/g, '-')
}

function close() {
  isVisible.value = false
}
</script>

<template>
  <Dialog
    v-model:visible="isVisible"
    header="Publish Live"
    :modal="true"
    :closable="true"
    :style="{ width: '540px' }"
  >
    <div class="publish-dialog">
      <p class="text-color-secondary mt-0">
        Publishes the current Live deployment. Additional deployment targets remain private previews.
      </p>
      <p v-if="liveTargetName || liveTargetSlug" class="text-color-secondary text-sm">
        Publishing Live: {{ liveTargetSlug || liveTargetName || 'Live' }}
      </p>
      <p v-if="viewingTargetSlug && viewingTargetSlug !== liveTargetSlug" class="text-color-secondary text-sm">
        Currently viewing {{ viewingTargetSlug }}. Additional targets are not published. Publishing still uses Live: {{ liveTargetSlug || liveTargetName || 'Live' }}.
      </p>
      <div class="field mb-4">
        <label for="slug" class="block font-semibold mb-2">Public URL</label>
        <div class="p-inputgroup">
          <span class="p-inputgroup-addon">{{ getPublishedAppsUrlPrefix() }}</span>
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

      <div class="field mb-4">
        <label for="publishRuntime" class="block font-semibold mb-2">Runtime visibility</label>
        <div class="flex align-items-center gap-3">
          <InputSwitch id="publishRuntime" v-model="publishRuntime" />
          <label for="publishRuntime">{{ publishRuntime ? 'Public runtime' : 'Private runtime' }}</label>
        </div>
        <small class="text-color-secondary block mt-1 ml-6">
          {{ publishRuntime ? 'Visitors with the app URL can open the runtime.' : 'Only permitted Studio users can open the runtime.' }}
        </small>
      </div>

      <div class="field mb-4">
        <label for="listInMarketplace" class="block font-semibold mb-2">Marketplace visibility</label>
        <div class="flex align-items-center gap-3">
          <InputSwitch id="listInMarketplace" v-model="listInMarketplace" />
          <label for="listInMarketplace">{{ listInMarketplace ? 'Listed in marketplace' : 'Unlisted' }}</label>
        </div>
        <small class="text-color-secondary block mt-1 ml-6">
          {{ !publishRuntime && listInMarketplace ? 'Private runtime — visitors can view media and fork, but cannot launch it.' : 'Use unlisted to keep the app out of public discovery.' }}
        </small>
      </div>

      <div class="field mb-4">
        <label for="category" class="block font-semibold mb-2">Marketplace Category</label>
        <Select
          id="category"
          v-model="selectedCategory"
          :options="categoryOptions"
          option-label="label"
          option-value="value"
          placeholder="Select a category"
          class="w-full"
          :loading="isLoadingCategories"
          show-clear
        />
        <small class="text-color-secondary block mt-1">
          Choose the storefront section where your app belongs best. Admins can move it later if needed.
        </small>
      </div>

      <div class="field mb-4">
        <label for="tags" class="block font-semibold mb-2">Tags</label>
        <InputText
          id="tags"
          v-model="tags"
          placeholder="productivity, forms, crm"
          class="w-full"
        />
        <small class="text-color-secondary block mt-1">
          Add a few search-friendly tags separated by commas.
        </small>
      </div>

      <div class="field mb-4">
        <label for="allowForking" class="block font-semibold mb-2">Remixing</label>
        <div class="flex align-items-center gap-3">
          <InputSwitch
            id="allowForking"
            v-model="allowRemixing"
          />
          <label for="allowForking">{{ allowRemixing ? 'Others can remix this app' : 'Source stays private' }}</label>
        </div>
        <small class="text-color-secondary block mt-1 ml-6">
          {{ allowRemixing
            ? 'Others can create editable remixes with attribution.'
            : 'People can use the app, but the editable source stays private.' }}
        </small>
      </div>

      <div class="field mb-4">
        <label for="requiresAuth" class="block font-semibold mb-2">Access Control</label>
        <div class="flex align-items-center gap-3">
          <InputSwitch
            id="requiresAuth"
            :model-value="!requiresAuthentication"
            disabled
            @update:model-value="requiresAuthentication = !$event"
          />
          <label for="requiresAuth">{{ requiresAuthentication ? 'Restricted access' : 'Public access' }}</label>
        </div>
        <small class="text-color-secondary block mt-1 ml-6">
          Public app installs must run from the standalone app host. Private and organization apps stay in Studio preview for now.
        </small>
      </div>

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
          label="Publish Live"
          icon="pi pi-upload"
          :disabled="!isValidSlug"
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
