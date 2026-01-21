<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Menu from 'primevue/menu'
import type { App } from '../types/apps.types'
import { useAppsStore } from '../stores/apps.store'

interface Props {
  app: App
}

const props = defineProps<Props>()

// Check if app has a thumbnail image
const hasThumbnail = computed(() => !!props.app.thumbnailUrl)

// Full thumbnail URL with API base URL
const fullThumbnailUrl = computed(() => {
  if (!props.app.thumbnailUrl) return ''
  // If already a full URL, use as-is; otherwise prepend API base URL
  if (props.app.thumbnailUrl.startsWith('http')) {
    return props.app.thumbnailUrl
  }
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  return `${baseUrl}${props.app.thumbnailUrl}`
})

const emit = defineEmits<{
  publish: [app: App]
}>()

const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const appsStore = useAppsStore()

const menu = ref()

const statusBadge = computed(() => ({
  label: props.app.isPublished ? 'Published' : 'Draft',
  severity: props.app.isPublished ? ('success' as const) : ('secondary' as const),
  icon: props.app.isPublished ? 'pi pi-check-circle' : 'pi pi-file-edit',
}))

const updatedAgo = computed(() => {
  if (!props.app.updated) return 'Never updated'

  const now = new Date()
  const updated = new Date(props.app.updated)
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

const handleEdit = () => {
  router.push(`/apps/${props.app.id}`)
}

const handleView = () => {
  if (props.app.isPublished && props.app.publicUrl) {
    window.open(`/web/${props.app.publicUrl}`, '_blank')
  }
}

const handlePublish = () => {
  emit('publish', props.app)
}

const handleUnpublish = () => {
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
    const newApp = await appsStore.createApp({
      name: `${props.app.name} (Copy)`,
      executionRunnerId: props.app.executionRunnerId || undefined,
    })
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

const handleDelete = () => {
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
  const items = [
    {
      label: 'Duplicate',
      icon: 'pi pi-copy',
      command: handleDuplicate,
    },
  ]

  if (props.app.isPublished) {
    items.push({
      label: 'Unpublish',
      icon: 'pi pi-eye-slash',
      command: handleUnpublish,
    })
  }

  items.push({
    label: 'Delete',
    icon: 'pi pi-trash',
    command: handleDelete,
  })

  return items
})
</script>

<template>
  <Card class="app-card h-full" :class="{ 'has-thumbnail': hasThumbnail }">
    <!-- Thumbnail Image -->
    <template v-if="hasThumbnail" #header>
      <div class="app-thumbnail">
        <img :src="fullThumbnailUrl" :alt="app.name" />
        <!-- Overlay with menu button -->
        <div class="thumbnail-overlay">
          <Button
            icon="pi pi-ellipsis-v"
            text
            rounded
            severity="secondary"
            size="small"
            class="menu-button"
            aria-haspopup="true"
            @click="toggleMenu"
          />
        </div>
      </div>
    </template>

    <template #content>
      <div class="flex flex-column h-full">
        <!-- Header (without thumbnail: show menu button here) -->
        <div class="flex align-items-center justify-content-between mb-2">
          <div class="flex align-items-center gap-2 min-w-0 flex-1">
            <i class="pi pi-globe flex-shrink-0" style="color: var(--p-primary-500)"></i>
            <span class="font-semibold text-overflow-ellipsis white-space-nowrap overflow-hidden">
              {{ app.displayName || app.name }}
            </span>
          </div>
          <Button
            v-if="!hasThumbnail"
            icon="pi pi-ellipsis-v"
            text
            rounded
            severity="secondary"
            size="small"
            aria-haspopup="true"
            @click="toggleMenu"
          />
          <Menu ref="menu" :model="menuItems" popup />
        </div>

        <!-- Description -->
        <p class="text-color-secondary text-sm mt-0 mb-3 line-clamp-2">
          {{ app.description || 'No description' }}
        </p>

        <!-- Status Badge -->
        <div class="flex align-items-center gap-2 mb-3">
          <Tag :severity="statusBadge.severity">
            <span class="flex align-items-center gap-1">
              <i :class="statusBadge.icon" style="font-size: 0.75rem"></i>
              {{ statusBadge.label }}
            </span>
          </Tag>
        </div>

        <!-- Metadata -->
        <div class="text-color-secondary text-sm mb-4">
          {{ updatedAgo }}
        </div>

        <!-- Actions -->
        <div class="flex gap-2 mt-auto">
          <Button
            label="Edit"
            icon="pi pi-pencil"
            size="small"
            outlined
            class="flex-1"
            @click="handleEdit"
          />
          <Button
            v-if="app.isPublished"
            label="View"
            icon="pi pi-external-link"
            size="small"
            outlined
            severity="secondary"
            class="flex-1"
            @click="handleView"
          />
          <Button
            v-else
            label="Publish"
            icon="pi pi-upload"
            size="small"
            outlined
            severity="secondary"
            class="flex-1"
            @click="handlePublish"
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

/* Thumbnail styles */
.app-thumbnail {
  position: relative;
  width: 100%;
  height: 140px;
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
  background: rgba(255, 255, 255, 0.9);
  backdrop-filter: blur(4px);
}

.thumbnail-overlay .menu-button:hover {
  background: white;
}

/* Card with thumbnail has different padding */
.has-thumbnail :deep(.p-card-body) {
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
  background: rgba(0, 0, 0, 0.7);
  color: white;
}

:global(.app-dark) .thumbnail-overlay .menu-button:hover {
  background: rgba(0, 0, 0, 0.85);
}
</style>
