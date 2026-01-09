<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dropdown from 'primevue/dropdown'
import Skeleton from 'primevue/skeleton'
import Message from 'primevue/message'
import Dialog from 'primevue/dialog'
import ConfirmDialog from 'primevue/confirmdialog'
import Toast from 'primevue/toast'
import AppCard from '../components/AppCard.vue'
import { useAppsStore } from '../stores/apps.store'
import type { App } from '../types/apps.types'

const router = useRouter()
const toast = useToast()
const appsStore = useAppsStore()

// Local state
const searchQuery = ref('')
const selectedStatus = ref<'all' | 'published' | 'draft'>('all')

const statusOptions = ref([
  { label: 'All', value: 'all' },
  { label: 'Published', value: 'published' },
  { label: 'Draft', value: 'draft' },
])

// Create dialog state
const showCreateDialog = ref(false)
const newAppName = ref('')
const isCreating = ref(false)

// Publish dialog state
const showPublishDialog = ref(false)
const publishingApp = ref<App | null>(null)
const publicUrl = ref('')
const requiresAuth = ref(true)
const isPublishing = ref(false)
const urlError = ref('')

// Computed
const showQuickStart = computed(() => {
  return !appsStore.hasApps && !appsStore.isLoading && !searchQuery.value
})

const showEmptySearch = computed(() => {
  return (
    appsStore.filteredApps.length === 0 &&
    appsStore.hasApps &&
    (searchQuery.value || selectedStatus.value !== 'all')
  )
})

// Watch for filter changes
watch(searchQuery, (value) => {
  appsStore.setSearchFilter(value)
})

watch(selectedStatus, (value) => {
  appsStore.setStatusFilter(value)
})

// Lifecycle
onMounted(async () => {
  try {
    await appsStore.fetchApps()
  } catch {
    // Error is handled in the store
  }
})

onUnmounted(() => {
  appsStore.clearFilters()
})

// Handlers
const handleCreateApp = async () => {
  if (!newAppName.value.trim()) {
    toast.add({
      severity: 'warn',
      summary: 'Validation Error',
      detail: 'App name is required',
      life: 3000,
    })
    return
  }

  isCreating.value = true
  try {
    const app = await appsStore.createApp({ name: newAppName.value.trim() })
    toast.add({
      severity: 'success',
      summary: 'Created',
      detail: `App "${app.name}" created successfully`,
      life: 3000,
    })
    showCreateDialog.value = false
    newAppName.value = ''
    router.push(`/apps/${app.id}`)
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to create app'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  } finally {
    isCreating.value = false
  }
}

const openCreateDialog = () => {
  newAppName.value = ''
  showCreateDialog.value = true
}

const openPublishDialog = (app: App) => {
  publishingApp.value = app
  publicUrl.value = app.publicUrl || generateSlug(app.name)
  requiresAuth.value = true
  urlError.value = ''
  showPublishDialog.value = true
}

const generateSlug = (name: string): string => {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

const validateUrl = () => {
  if (!publicUrl.value.trim()) {
    urlError.value = 'URL slug is required'
    return false
  }
  if (!/^[a-z0-9-]+$/.test(publicUrl.value)) {
    urlError.value = 'URL can only contain lowercase letters, numbers, and hyphens'
    return false
  }
  urlError.value = ''
  return true
}

const handlePublishApp = async () => {
  if (!publishingApp.value) return
  if (!validateUrl()) return

  isPublishing.value = true
  try {
    await appsStore.publishApp(publishingApp.value.id, {
      publicUrl: publicUrl.value,
      requiresAuthentication: requiresAuth.value,
    })
    toast.add({
      severity: 'success',
      summary: 'Published',
      detail: `App is now available at /web/${publicUrl.value}`,
      life: 5000,
    })
    showPublishDialog.value = false
    publishingApp.value = null
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to publish app'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  } finally {
    isPublishing.value = false
  }
}

const handleRetry = async () => {
  appsStore.clearError()
  try {
    await appsStore.fetchApps()
  } catch {
    // Error is handled in the store
  }
}

const handleCreateWithAI = () => {
  // Navigate to AI-powered app creation
  toast.add({
    severity: 'info',
    summary: 'Coming Soon',
    detail: 'AI-powered app creation will be available soon',
    life: 3000,
  })
}

const handleViewExamples = () => {
  // Navigate to examples
  toast.add({
    severity: 'info',
    summary: 'Coming Soon',
    detail: 'Example apps will be available soon',
    life: 3000,
  })
}
</script>

<template>
  <div class="apps-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Apps</h1>
        <p class="text-color-secondary mt-1 mb-0">Build and publish web applications</p>
      </div>
      <Button label="New App" icon="pi pi-plus" @click="openCreateDialog" />
    </div>

    <!-- Search and Filters -->
    <div class="flex align-items-center gap-3 mb-5">
      <span class="p-input-icon-left flex-1" style="max-width: 400px">
        <i class="pi pi-search" />
        <InputText v-model="searchQuery" placeholder="Search apps..." class="w-full" />
      </span>
      <Dropdown
        v-model="selectedStatus"
        :options="statusOptions"
        option-label="label"
        option-value="value"
        placeholder="Status"
        class="w-10rem"
      />
    </div>

    <!-- Loading State -->
    <div v-if="appsStore.isLoading" class="mb-5">
      <div class="flex align-items-center gap-3 mb-4">
        <Skeleton width="5rem" height="1.5rem" />
        <Skeleton width="4rem" height="1.5rem" border-radius="1rem" />
      </div>
      <div class="grid">
        <div v-for="i in 6" :key="i" class="col-12 md:col-6 lg:col-4">
          <Card class="h-full">
            <template #content>
              <div class="flex flex-column gap-3">
                <div class="flex align-items-center gap-2">
                  <Skeleton shape="circle" size="1.5rem" />
                  <Skeleton width="60%" height="1.25rem" />
                </div>
                <Skeleton width="100%" height="2.5rem" />
                <Skeleton width="5rem" height="1.5rem" border-radius="1rem" />
                <Skeleton width="8rem" height="1rem" />
                <div class="flex gap-2 mt-2">
                  <Skeleton width="50%" height="2rem" />
                  <Skeleton width="50%" height="2rem" />
                </div>
              </div>
            </template>
          </Card>
        </div>
      </div>
    </div>

    <!-- Error State -->
    <Message v-else-if="appsStore.error" severity="error" :closable="false" class="mb-5">
      <div class="flex align-items-center justify-content-between w-full">
        <span>{{ appsStore.error }}</span>
        <Button label="Retry" severity="danger" size="small" outlined @click="handleRetry" />
      </div>
    </Message>

    <!-- Content -->
    <template v-else>
      <!-- Apps Grid -->
      <div v-if="appsStore.filteredApps.length > 0" class="mb-5">
        <div class="flex align-items-center gap-3 mb-4">
          <span class="font-semibold">My Apps</span>
          <Tag :value="`${appsStore.filteredApps.length} apps`" severity="secondary" />
        </div>

        <div class="grid">
          <div
            v-for="app in appsStore.filteredApps"
            :key="app.id"
            class="col-12 md:col-6 lg:col-4"
          >
            <AppCard :app="app" @publish="openPublishDialog" />
          </div>
        </div>
      </div>

      <!-- Empty Search Results -->
      <Card v-else-if="showEmptySearch" class="mb-5">
        <template #content>
          <div class="text-center py-5">
            <i class="pi pi-search text-5xl text-color-secondary mb-4"></i>
            <h3 class="text-xl font-semibold mb-2">No apps found</h3>
            <p class="text-color-secondary mb-4">Try adjusting your search or filters</p>
            <Button
              label="Clear Filters"
              icon="pi pi-filter-slash"
              outlined
              @click="
                () => {
                  searchQuery = ''
                  selectedStatus = 'all'
                }
              "
            />
          </div>
        </template>
      </Card>

      <!-- Empty State (No Apps) -->
      <Card v-else-if="!appsStore.hasApps" class="mb-5">
        <template #content>
          <div class="text-center py-5">
            <i class="pi pi-globe text-6xl text-color-secondary mb-4"></i>
            <h3 class="text-xl font-semibold mb-2">No apps yet</h3>
            <p class="text-color-secondary mb-4">Get started by creating your first app</p>
            <Button label="Create App" icon="pi pi-plus" @click="openCreateDialog" />
          </div>
        </template>
      </Card>

      <!-- Quick Start Section -->
      <Card v-if="showQuickStart" class="quick-start-card">
        <template #title>
          <div class="flex align-items-center gap-2">
            <i class="pi pi-lightbulb" style="color: var(--p-yellow-500)"></i>
            <span>Quick Start</span>
          </div>
        </template>
        <template #content>
          <p class="text-color-secondary mt-0 mb-4">
            New to apps? Create your first web app with AI:
          </p>
          <ul class="text-color-secondary m-0 mb-4 pl-4">
            <li>Simple data entry form</li>
            <li>Dashboard with charts</li>
            <li>Customer feedback portal</li>
          </ul>
          <div class="flex gap-3">
            <Button label="Create with AI" icon="pi pi-sparkles" @click="handleCreateWithAI" />
            <Button label="View Examples" outlined severity="secondary" @click="handleViewExamples" />
          </div>
        </template>
      </Card>
    </template>

    <!-- Create App Dialog -->
    <Dialog
      v-model:visible="showCreateDialog"
      modal
      header="Create New App"
      :style="{ width: '450px' }"
      :closable="!isCreating"
      :close-on-escape="!isCreating"
    >
      <div class="flex flex-column gap-3">
        <div class="flex flex-column gap-2">
          <label for="app-name" class="font-semibold">App Name</label>
          <InputText
            id="app-name"
            v-model="newAppName"
            placeholder="Enter app name"
            class="w-full"
            :disabled="isCreating"
            autofocus
            @keyup.enter="handleCreateApp"
          />
        </div>
      </div>
      <template #footer>
        <div class="flex justify-content-end gap-2">
          <Button
            label="Cancel"
            severity="secondary"
            outlined
            :disabled="isCreating"
            @click="showCreateDialog = false"
          />
          <Button label="Create" icon="pi pi-plus" :loading="isCreating" @click="handleCreateApp" />
        </div>
      </template>
    </Dialog>

    <!-- Publish App Dialog -->
    <Dialog
      v-model:visible="showPublishDialog"
      modal
      header="Publish App"
      :style="{ width: '500px' }"
      :closable="!isPublishing"
      :close-on-escape="!isPublishing"
    >
      <div class="flex flex-column gap-4">
        <p class="text-color-secondary m-0">
          Publishing will make "{{ publishingApp?.name }}" accessible to users via a public URL.
        </p>

        <div class="flex flex-column gap-2">
          <label for="public-url" class="font-semibold">Public URL</label>
          <div class="p-inputgroup">
            <span class="p-inputgroup-addon">/web/</span>
            <InputText
              id="public-url"
              v-model="publicUrl"
              placeholder="my-app"
              :class="{ 'p-invalid': urlError }"
              :disabled="isPublishing"
              @input="validateUrl"
            />
          </div>
          <small v-if="urlError" class="p-error">{{ urlError }}</small>
          <small v-else class="text-color-secondary">
            Your app will be accessible at /web/{{ publicUrl || 'your-slug' }}
          </small>
        </div>

        <div class="flex align-items-center gap-2">
          <input
            id="requires-auth"
            v-model="requiresAuth"
            type="checkbox"
            :disabled="isPublishing"
            class="p-checkbox"
          />
          <label for="requires-auth" class="cursor-pointer">
            Require authentication to access
          </label>
        </div>
      </div>
      <template #footer>
        <div class="flex justify-content-end gap-2">
          <Button
            label="Cancel"
            severity="secondary"
            outlined
            :disabled="isPublishing"
            @click="showPublishDialog = false"
          />
          <Button
            label="Publish"
            icon="pi pi-upload"
            :loading="isPublishing"
            @click="handlePublishApp"
          />
        </div>
      </template>
    </Dialog>

    <!-- Confirm Dialog -->
    <ConfirmDialog />

    <!-- Toast -->
    <Toast />
  </div>
</template>

<style scoped>
.apps-view {
  max-width: 1400px;
  margin: 0 auto;
}

.quick-start-card {
  max-width: 600px;
}
</style>
