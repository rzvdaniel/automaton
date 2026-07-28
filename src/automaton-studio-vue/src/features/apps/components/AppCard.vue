<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Menu from 'primevue/menu'
import type { AppListItem } from '../types/apps.types'
import { useAppsStore } from '../stores/apps.store'
import { appsService } from '../services/apps.service'
import AppCoverPlaceholder from './AppCoverPlaceholder.vue'
import { getPublishedAppUrl, getStudioApplicationUrl } from '../utils/public-app-url'

interface Props {
  app: AppListItem
}

const props = defineProps<Props>()

// Check if app has a real uploaded thumbnail image
const hasThumbnail = computed(() => !!props.app.thumbnailUrl)

// Full thumbnail URL with API base URL (only used when thumbnailUrl is set)
const fullThumbnailUrl = computed(() => {
  if (!props.app.thumbnailUrl) return ''
  if (props.app.thumbnailUrl.startsWith('http')) {
    return props.app.thumbnailUrl
  }
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  return `${baseUrl}${props.app.thumbnailUrl}`
})

const emit = defineEmits<{ publish: [appId: string] }>()

const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const appsStore = useAppsStore()

const menu = ref()
const isExporting = ref(false)

const statusBadge = computed(() => ({
  label: props.app.isPublished ? 'Published' : 'Draft',
  severity: props.app.isPublished ? ('success' as const) : ('secondary' as const),
  icon: props.app.isPublished ? 'pi pi-check-circle' : 'pi pi-file-edit',
}))

// Project linkage info
const updatedAgo = computed(() => {
  if (!props.app.updatedAtUtc) return 'Never updated'

  const now = new Date()
  const updated = new Date(props.app.updatedAtUtc)
  const diffMs = now.getTime() - updated.getTime()
  const diffMinutes = Math.floor(diffMs / (1000 * 60))
  const diffHours = Math.floor(diffMs / (1000 * 60 * 60))
  const diffDays = Math.floor(diffHours / 24)
  const diffWeeks = Math.floor(diffDays / 7)

  if (diffMinutes < 1) return 'Updated: just now'
  if (diffMinutes < 60) return `Updated: ${diffMinutes}m ago`
  if (diffHours < 24) return `Updated: ${diffHours}h ago`
  if (diffDays < 7) return `Updated: ${diffDays}d ago`
  return `Updated: ${diffWeeks}w ago`
})
const deploymentSummary = computed(() => {
  const frontend = props.app.runtime.frontendAvailable
  const backend = props.app.runtime.backendAvailable
  if (!frontend && !backend) return 'Not deployed yet'

  const values = [props.app.runtime.frontendDeployedAtUtc, props.app.runtime.backendDeployedAtUtc]
    .filter((value): value is string => Boolean(value))
    .map((value) => new Date(value).getTime())
  const state = frontend && backend ? 'Full stack deployed' : frontend ? 'Frontend deployed' : 'Backend deployed'
  if (!values.length) return state
  const diffHours = Math.floor((Date.now() - Math.max(...values)) / (1000 * 60 * 60))
  return diffHours < 1 ? `${state} · just now` : `${state} · ${diffHours}h ago`
})

const canOpenPrivateApp = computed(() => props.app.runtime.frontendAvailable)
const canOpenPublicApp = computed(() => props.app.isPublished && !!props.app.publicUrl)
const canOpenApp = computed(() => canOpenPublicApp.value || canOpenPrivateApp.value)
const openAppUrl = computed(() => {
  if (canOpenPublicApp.value) {
    return getPublishedAppUrl(props.app.publicUrl)
  }

  return getStudioApplicationUrl(props.app.id)
})
const publicAppUrl = computed(() => getPublishedAppUrl(props.app.publicUrl))
const studioApplicationUrl = computed(() => getStudioApplicationUrl(props.app.id))

const handleEdit = () => {
  router.push(`/apps/${props.app.id}`)
}

const handleOpenApp = async () => {
  if (!canOpenApp.value || !openAppUrl.value) {
    return
  }

  if (canOpenPublicApp.value) {
    window.open(openAppUrl.value, '_blank', 'noopener')
    return
  }

  await handleOpenPrivateApp()
}

const handleOpenPublicApp = async (): Promise<void> => {
  if (!canOpenPublicApp.value || !publicAppUrl.value) {
    return
  }

  window.open(publicAppUrl.value, '_blank', 'noopener')
}

const handleOpenPrivateApp = async () => {
  if (!canOpenPrivateApp.value || !studioApplicationUrl.value) {
    return
  }

  window.open(studioApplicationUrl.value, '_blank', 'noopener')
}

const handleUnpublish = async () => {
  confirm.require({
    message: `Are you sure you want to unpublish "${props.app.name}"? It will no longer be accessible via its public URL.`,
    header: 'Confirm Unpublish',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-warning',
    accept: async () => {
      try {
        await appsStore.unpublishApp(props.app.id)
        toast.add({
          severity: 'success',
          summary: 'Unpublished',
          detail: `"${props.app.name}" has been unpublished`,
          life: 3000,
        })
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : 'Failed to unpublish app'
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: message,
          life: 5000,
        })
      }
    },
  })
}

const handleDuplicate = async () => {
  try {
    const newApp = await appsStore.duplicateApp(props.app.id)
    toast.add({
      severity: 'success',
      summary: 'Duplicated',
      detail: `Created "${newApp.name}"`,
      life: 3000,
    })
    router.push(`/apps/${newApp.id}`)
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to duplicate app'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  }
}

const handleExport = async () => {
  isExporting.value = true

  try {
    const { blob, fileName } = await appsService.exportApp(props.app.id, props.app.name)
    const downloadUrl = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = downloadUrl
    link.download = fileName
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    URL.revokeObjectURL(downloadUrl)

    toast.add({
      severity: 'success',
      summary: 'Export Ready',
      detail: `Downloaded "${fileName}"`,
      life: 3000,
    })
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to export app'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  } finally {
    isExporting.value = false
  }
}

const handleDelete = async () => {
  confirm.require({
    message: `Are you sure you want to delete "${props.app.name}"? This action cannot be undone.`,
    header: 'Confirm Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await appsStore.deleteApp(props.app.id)
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: `"${props.app.name}" has been deleted`,
          life: 3000,
        })
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : 'Failed to delete app'
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: message,
          life: 5000,
        })
      }
    },
  })
}

const toggleMenu = (event: Event) => {
  menu.value.toggle(event)
}

const menuItems = computed(() => {
  const items: Array<{
    label: string
    icon: string
    command: () => Promise<void>
    disabled?: boolean
  }> = [
    {
      label: isExporting.value ? 'Exporting...' : 'Export',
      icon: 'pi pi-download',
      command: handleExport,
      disabled: isExporting.value,
    },
    {
      label: 'Duplicate',
      icon: 'pi pi-copy',
      command: handleDuplicate,
    },
  ]

  if (canOpenPrivateApp.value) {
    items.unshift({
      label: canOpenPublicApp.value ? 'Open in Studio' : 'Open Application',
      icon: 'pi pi-lock',
      command: handleOpenPrivateApp,
    })
  }

  if (canOpenPublicApp.value) {
    items.unshift({
      label: 'Open Public App',
      icon: 'pi pi-globe',
      command: handleOpenPublicApp,
    })
    items.push({
      label: 'Unpublish',
      icon: 'pi pi-eye-slash',
      command: handleUnpublish,
    })
  }

  if (props.app.canDelete) items.push({ label: 'Delete', icon: 'pi pi-trash', command: handleDelete })

  return items
})
</script>

<template>
  <Card class="app-card h-full">
    <!-- Thumbnail — always shown (uploaded image or gradient placeholder) -->
    <template #header>
      <div class="app-thumbnail">
        <img v-if="hasThumbnail" :src="fullThumbnailUrl" :alt="app.name" loading="lazy" />
        <AppCoverPlaceholder v-else :app-id="app.id" :name="app.name" />
        <!-- Status badge on image -->
        <Tag :severity="statusBadge.severity" class="status-overlay-badge">
          <span class="flex align-items-center gap-1">
            <i :class="statusBadge.icon" style="font-size: 0.7rem"></i>
            {{ statusBadge.label }}
          </span>
        </Tag>
        <!-- Menu button overlay -->
        <div class="thumbnail-overlay">
          <Button
            icon="pi pi-ellipsis-v"
            text
            rounded
            size="small"
            class="menu-button"
            aria-haspopup="true"
            @click="toggleMenu"
          />
          <Menu ref="menu" :model="menuItems" popup />
        </div>
      </div>
    </template>

    <template #content>
      <div class="flex flex-column h-full">
        <!-- App name -->
        <div class="flex align-items-center gap-2 min-w-0 mb-1">
          <i class="pi pi-globe flex-shrink-0" style="color: var(--p-primary-500); font-size: 0.85rem"></i>
          <span class="font-semibold text-overflow-ellipsis white-space-nowrap overflow-hidden">
            {{ app.name }}
          </span>
        </div>

        <!-- Description -->
        <p class="text-color-secondary text-sm mt-0 mb-3 line-clamp-2">
          {{ app.description || 'No description' }}
        </p>

        <!-- Metadata -->
        <div class="text-color-secondary text-sm mb-4">
          {{ updatedAgo }}
        </div>
        <div class="text-color-secondary text-xs mb-3">
          <div><i class="pi pi-server mr-1"></i>{{ app.runner?.name || 'No Runner' }} · {{ app.runner?.connectivityStatus || 'None' }}</div>
          <div>{{ deploymentSummary }}</div>
        </div>

        <!-- Actions -->
        <div class="flex gap-2 mt-auto">
          <Button
            label="Open"
            icon="pi pi-external-link"
            size="small"
            class="flex-1"
            :disabled="!canOpenApp"
            @click="handleOpenApp"
          />
          <Button
            label="Edit"
            icon="pi pi-pencil"
            size="small"
            outlined
            class="flex-1"
            @click="handleEdit"
          />
        </div>
      </div>
    </template>
  </Card>
</template>

<style scoped>
.app-card {
  transition:
    transform 0.15s ease,
    box-shadow 0.15s ease;
}

.app-card:hover {
  transform: translateY(-2px);
}

/* Thumbnail — always shown */
.app-thumbnail {
  position: relative;
  width: 100%;
  height: 160px;
  overflow: hidden;
  border-radius: var(--p-card-border-radius) var(--p-card-border-radius) 0 0;
}

.app-thumbnail img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform 0.3s ease;
}

.app-card:hover .app-thumbnail img {
  transform: scale(1.05);
}

/* Status badge pinned top-left on thumbnail */
.status-overlay-badge {
  position: absolute;
  top: 8px;
  left: 8px;
  z-index: 10;
  font-size: 0.72rem;
}

.thumbnail-overlay {
  position: absolute;
  top: 0;
  right: 0;
  padding: 0.5rem;
  opacity: 0;
  transition: opacity 0.2s ease;
}

.app-card:hover .thumbnail-overlay {
  opacity: 1;
}

.thumbnail-overlay .menu-button {
  background: rgba(255, 255, 255, 0.9) !important;
  backdrop-filter: blur(4px);
}

.thumbnail-overlay .menu-button:hover {
  background: white !important;
}

/* Reduce body top padding since thumbnail is always present */
:deep(.p-card-body) {
  padding-top: 0.75rem;
}

.line-clamp-2 {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  min-height: 2.5rem;
}

/* Dark mode */
:global(.app-dark) .thumbnail-overlay .menu-button {
  background: rgba(0, 0, 0, 0.7) !important;
  color: white;
}

:global(.app-dark) .thumbnail-overlay .menu-button:hover {
  background: rgba(0, 0, 0, 0.85) !important;
}
</style>
