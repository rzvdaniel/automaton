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
import { getDeployedAppUrl, getDeploymentTargetUrl, getPublishedAppUrl, getStudioApplicationUrl } from '../utils/public-app-url'
import { resolveMediaUrl } from '../utils/media-url'

interface Props {
  app: AppListItem
}

const props = defineProps<Props>()

// Check if app has a real uploaded thumbnail image
const hasThumbnail = computed(() => !!props.app.thumbnailUrl)

// Full thumbnail URL with API base URL (only used when thumbnailUrl is set)
const fullThumbnailUrl = computed(() => {
  return resolveMediaUrl(props.app.thumbnailUrl)
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

const initials = computed(() => props.app.name
  .split(/\s+/)
  .slice(0, 2)
  .map((word) => word[0] ?? '')
  .join('')
  .toUpperCase())

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

  return frontend && backend ? 'Full stack deployed' : frontend ? 'Frontend deployed' : 'Backend deployed'
})

const liveEnvironment = computed(() => props.app.liveEnvironment)
const liveStatusLabel = computed(() => {
  const environment = liveEnvironment.value
  if (!environment) return props.app.runner?.connectivityStatus === 'Online' ? 'Running' : 'Unassigned'
  if (!environment.runnerOnline && environment.status !== 'Unassigned') return 'Offline'
  if (!environment.frontendDeployed && !environment.backendDeployed) return 'Unassigned'
  return environment.status === 'Running' ? 'Running' : environment.status
})
const liveStatusClass = computed(() => liveStatusLabel.value === 'Running' ? 'runner-status--online' : 'runner-status--offline')
const liveDestinationLabel = computed(() => {
  const environment = liveEnvironment.value
  if (!environment || environment.status === 'Unassigned') return 'No runtime assigned'
  if (environment.isHostedRunner) return 'Automaton Hosted'
  return environment.applicationProfile === 'PrivilegedHost'
    ? 'Self-hosted · Privileged Host'
    : 'Self-hosted · Standard'
})

const canOpenPrivateApp = computed(() => props.app.runtime.frontendAvailable)
const canOpenPublicApp = computed(() => props.app.isPublished && !!props.app.publicUrl)
const canOpenApp = computed(() => canOpenPublicApp.value || canOpenPrivateApp.value)
const primaryAction = computed(() => canOpenApp.value
  ? { label: 'Open', icon: 'pi pi-external-link' }
  : { label: 'Deploy', icon: 'pi pi-cloud-upload' })
const openAppUrl = computed(() => {
  if (canOpenPublicApp.value) {
    return getPublishedAppUrl(props.app.publicUrl)
  }

  return liveEnvironment.value?.routePath
    ? getDeploymentTargetUrl(props.app.id, liveEnvironment.value.routePath)
    : getDeployedAppUrl(props.app.id)
})
const publicAppUrl = computed(() => getPublishedAppUrl(props.app.publicUrl))
const studioApplicationUrl = computed(() => getStudioApplicationUrl(props.app.id))

const handleEdit = () => {
  router.push(`/apps/${props.app.id}`)
}

const handlePrimaryAction = async () => {
  if (!canOpenApp.value) {
    handleEdit()
    return
  }

  await handleOpenApp()
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
        <Tag
          :value="statusBadge.label"
          :icon="statusBadge.icon"
          :severity="statusBadge.severity"
          class="status-overlay-badge"
        />
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
      <div class="app-card-content flex flex-column h-full">
        <div class="app-card-identity">
          <div class="app-icon" aria-hidden="true">{{ initials }}</div>
          <div class="app-title-group min-w-0">
            <div class="app-title text-overflow-ellipsis white-space-nowrap overflow-hidden">{{ app.name }}</div>
            <div class="app-summary">{{ app.summary || 'No summary' }}</div>
          </div>
        </div>

        <div class="app-card-tags" v-if="app.tags.length">
          <Tag v-for="tag in app.tags.slice(0, 3)" :key="tag" :value="tag" severity="secondary" />
          <Tag v-if="app.tags.length > 3" :value="`+${app.tags.length - 3}`" severity="secondary" />
        </div>

        <div class="app-card-divider" aria-hidden="true"></div>

        <div class="app-status-rows">
          <div class="app-status-row">
            <span>Live</span>
            <span class="app-status-value" :aria-label="`${liveDestinationLabel} · ${liveStatusLabel}`"><i :class="['runner-status-dot', liveStatusClass]" aria-hidden="true"></i>{{ liveDestinationLabel }} · {{ liveStatusLabel }}</span>
          </div>
          <div class="app-status-row">
            <span>Deployment</span>
            <span class="app-status-value">{{ deploymentSummary }}</span>
          </div>
          <div class="app-status-row">
            <span>Updated</span>
            <span class="app-status-value">{{ updatedAgo.replace('Updated: ', '') }}</span>
          </div>
        </div>

        <div class="app-card-actions">
          <Button
            :label="primaryAction.label"
            :icon="primaryAction.icon"
            size="small"
            class="primary-action"
            @click="handlePrimaryAction"
          />
          <Button
            label="Edit"
            icon="pi pi-pencil"
            size="small"
            outlined
            class="secondary-action"
            @click="handleEdit"
          />
        </div>
      </div>
    </template>
  </Card>
</template>

<style scoped>
.app-card {
  border: 1px solid var(--p-surface-border);
  border-radius: 0.85rem;
  box-shadow: 0 1px 2px rgba(15, 23, 42, 0.04);
  transition:
    transform 0.18s ease,
    box-shadow 0.18s ease,
    border-color 0.18s ease;
}

.app-card:hover {
  transform: translateY(-2px);
  border-color: color-mix(in srgb, var(--p-primary-500) 35%, var(--p-surface-border));
  box-shadow: 0 10px 24px rgba(15, 23, 42, 0.08);
}

/* Thumbnail — always shown */
.app-thumbnail {
  position: relative;
  width: 100%;
  aspect-ratio: 16 / 6;
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
  top: 0.7rem;
  left: 0.7rem;
  z-index: 10;
  min-height: 1.5rem;
  padding: 0.24rem 0.58rem;
  border: 1px solid rgba(255, 255, 255, 0.7);
  border-radius: 0.45rem;
  background: rgba(255, 255, 255, 0.94);
  box-shadow: 0 2px 8px rgba(15, 23, 42, 0.12);
  color: var(--p-surface-700);
  font-size: 0.72rem;
  font-weight: 600;
  line-height: 1;
}

.status-overlay-badge :deep(.p-tag-icon) {
  margin-right: 0.3rem;
  font-size: 0.7rem;
}

.status-overlay-badge :deep(.p-tag-label) {
  line-height: 1;
}

.thumbnail-overlay {
  position: absolute;
  top: 0;
  right: 0;
  padding: 0.5rem;
  opacity: 1;
  transition: opacity 0.2s ease;
}

.app-card:hover .thumbnail-overlay {
  opacity: 1;
}

.thumbnail-overlay .menu-button {
  width: 2rem;
  height: 2rem;
  background: rgba(255, 255, 255, 0.9) !important;
  backdrop-filter: blur(4px);
}

.thumbnail-overlay .menu-button:hover {
  background: white !important;
}

/* Reduce body top padding since thumbnail is always present */
:deep(.p-card-body) {
  padding: 0;
}

.app-card-content {
  padding: 0.9rem 1rem 1rem;
}

.app-card-identity {
  display: grid;
  grid-template-columns: 2.625rem minmax(0, 1fr);
  align-items: start;
  gap: 0.75rem;
  min-width: 0;
}

.app-icon {
  display: grid;
  width: 2.625rem;
  height: 2.625rem;
  place-items: center;
  border-radius: 0.65rem;
  background: color-mix(in srgb, var(--p-primary-500) 12%, var(--p-surface-100));
  color: var(--p-primary-600);
  font-size: 0.9rem;
  font-weight: 700;
}

.app-title-group {
  padding-top: 0.05rem;
}

.app-title {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 700;
  line-height: 1.15;
  letter-spacing: -0.01em;
}

.app-summary {
  display: -webkit-box;
  margin-top: 0.25rem;
  overflow: hidden;
  color: var(--p-text-muted-color);
  font-size: 0.8rem;
  line-height: 1.25;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.app-card-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  margin-top: 0.45rem;
}

.app-card-tags :deep(.p-tag) {
  padding: 0.2rem 0.45rem;
  font-size: 0.72rem;
}

.app-card-divider {
  margin: 0.85rem 0 0.75rem;
  border-top: 1px solid var(--p-surface-200);
}

.app-identity-icon {
  color: var(--p-primary-500);
  font-size: 0.85rem;
}

.app-status-rows {
  display: grid;
  gap: 0.45rem;
  color: var(--p-text-muted-color);
  font-size: 0.82rem;
}

.app-status-row {
  display: grid;
  grid-template-columns: 4.5rem minmax(0, 1fr);
  gap: 0.5rem;
  align-items: start;
}

.app-status-row > span:first-child { color: var(--p-text-muted-color); }

.app-status-value {
  min-width: 0;
  overflow-wrap: anywhere;
  color: var(--p-text-color);
  font-weight: 500;
}

.app-card-actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.65rem;
  margin-top: auto;
  padding-top: 0.85rem;
  width: 100%;
}

.app-card-actions :deep(.p-button) {
  min-height: 2.15rem;
  padding: 0.45rem 0.75rem;
  font-size: 0.875rem;
}

.app-card-actions .primary-action,
.app-card-actions .secondary-action {
  width: 100%;
  min-width: 0;
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.runner-status-dot {
  display: inline-block;
  width: 0.5rem;
  height: 0.5rem;
  margin-right: 0.35rem;
  border-radius: 50%;
  background: var(--p-surface-400);
}

.runner-status--online { background: var(--p-green-500); }
.runner-status--offline { background: var(--p-surface-500); }

/* Dark mode */
:global(.app-dark) .thumbnail-overlay .menu-button {
  background: rgba(0, 0, 0, 0.7) !important;
  color: white;
}

:global(.app-dark) .thumbnail-overlay .menu-button:hover {
  background: rgba(0, 0, 0, 0.85) !important;
}
</style>
