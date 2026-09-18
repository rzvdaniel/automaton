<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputSwitch from 'primevue/inputswitch'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { appsService } from '../services/apps.service'
import type { PublishSettingsRequest } from '../types/apps.types'
import { getPublishedAppUrl } from '../utils/public-app-url'
import { getPublishVisibilityState } from '../utils/publish-visibility'

const props = defineProps<{
  visible: boolean
  currentSlug?: string
  isPublished?: boolean
  requiresAuth?: boolean
  currentRuntimeVisibility?: 'private' | 'public'
  currentCatalogVisibility?: 'unlisted' | 'listed'
  appName?: string
  hasLiveTarget?: boolean
  liveTargetName?: string
  liveTargetSlug?: string
  viewingTargetName?: string
  viewingTargetSlug?: string
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  publish: [request: PublishSettingsRequest]
}>()

const slug = ref('')
const requiresAuthentication = computed(() => props.requiresAuth ?? false)
const publishRuntime = ref(true)
const listInMarketplace = ref(true)
const validationError = ref<string | null>(null)

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

watch(isVisible, (visible) => {
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

  const visibilityState = getPublishVisibilityState(
    props.currentRuntimeVisibility,
    props.currentCatalogVisibility,
  )
  publishRuntime.value = requiresAuthentication.value ? false : visibilityState.publishRuntime
  listInMarketplace.value = visibilityState.listInMarketplace
  validationError.value = null
}, { immediate: true })

function handlePublish() {
  if (!isValidSlug.value) return

  emit('publish', {
    publicUrl: slug.value,
    requiresAuthentication: requiresAuthentication.value,
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
    header="Publishing settings"
    :modal="true"
    :closable="true"
    :style="{ width: '540px' }"
  >
    <div class="publish-dialog">
      <p class="text-color-secondary mt-0">
        Publishing controls public access and marketplace visibility. It does not deploy or update the Live runtime.
      </p>
      <p v-if="liveTargetName || liveTargetSlug" class="text-color-secondary text-sm">
        The public runtime URL uses the Live environment. Other environments remain private previews.
      </p>
      <Message v-if="hasLiveTarget === false" severity="info" :closable="false">
        No Live environment exists yet. You can save publishing settings now; the public runtime URL will not serve the app until a Live runtime is deployed.
      </Message>
      <p v-if="viewingTargetSlug && viewingTargetSlug !== liveTargetSlug" class="text-color-secondary text-sm">
        Currently viewing: {{ viewingTargetName || 'another environment' }}. Additional environments remain private previews.
      </p>
      <div class="field mb-4">
        <label for="slug" class="block font-semibold mb-2">Public URL</label>
        <InputText
          id="slug"
          v-model="slug"
          placeholder="my-app"
          :class="{ 'p-invalid': validationMessage && slug.length > 0 }"
          @input="handleSlugInput"
        />
        <small v-if="validationMessage" class="p-error block mt-1">
          {{ validationMessage }}
        </small>
        <small v-else-if="isValidSlug" class="text-color-secondary block mt-1">
          {{ publishRuntime
            ? `Public runtime URL: ${publicUrl}. It serves the Live environment after deployment.`
            : 'This slug is saved with the settings, but a private runtime is not publicly launchable.' }}
        </small>
      </div>

      <div class="field mb-4">
        <label for="publishRuntime" class="block font-semibold mb-2">Runtime visibility</label>
        <div class="flex align-items-center gap-3">
          <InputSwitch id="publishRuntime" v-model="publishRuntime" :disabled="requiresAuthentication" />
          <label for="publishRuntime">{{ publishRuntime ? 'Public runtime' : 'Private runtime' }}</label>
        </div>
        <small class="text-color-secondary block mt-1 ml-6">
          {{ requiresAuthentication ? 'This app requires sign-in, so its runtime must remain private.' : publishRuntime ? 'Visitors with the app URL can open the runtime.' : 'Only permitted Studio users can open the runtime.' }}
        </small>
      </div>

      <div class="field mb-4">
        <label for="listInMarketplace" class="block font-semibold mb-2">Marketplace visibility</label>
        <div class="flex align-items-center gap-3">
          <InputSwitch id="listInMarketplace" v-model="listInMarketplace" />
          <label for="listInMarketplace">{{ listInMarketplace ? 'Listed in marketplace' : 'Unlisted' }}</label>
        </div>
        <small class="text-color-secondary block mt-1 ml-6">
          {{ !publishRuntime && listInMarketplace ? 'Private runtime — visitors can view media, but cannot launch it.' : 'Use unlisted to keep the app out of public discovery.' }}
        </small>
      </div>

      <p v-if="requiresAuthentication" class="text-color-secondary text-sm">
        Sign-in is currently required. This dialog preserves that setting and keeps the runtime private.
      </p>

      <Message
        v-if="publishRuntime && !requiresAuthentication"
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
          :label="isPublished ? 'Save publishing settings' : 'Publish app'"
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
