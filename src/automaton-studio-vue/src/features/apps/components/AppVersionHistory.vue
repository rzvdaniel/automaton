<script setup lang="ts">
/**
 * AppVersionHistory.vue
 * Displays app version history and allows restoration from previous versions
 * Shows timeline of snapshots with triggers and summaries
 */
import { ref, onMounted } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import Card from 'primevue/card'
import Divider from 'primevue/divider'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import ScrollPanel from 'primevue/scrollpanel'
import Message from 'primevue/message'
import { appsService } from '../services/apps.service'
import type { AppVersion, AppVersionDetail, AppVersionList } from '../types/apps.types'

interface Props {
  appId: string
}

const props = defineProps<Props>()
const emit = defineEmits<{
  versionRestored: [versionNumber: number]
  versionCreated: []
}>()

const toast = useToast()
const confirm = useConfirm()

const isLoading = ref(false)
const isCreatingSnapshot = ref(false)
const isRestoringVersion = ref(false)
const versionList = ref<AppVersionList | null>(null)
const selectedVersion = ref<AppVersionDetail | null>(null)
const showVersionDetail = ref(false)

/**
 * Load version history
 */
async function loadVersions() {
  if (!props.appId) return

  isLoading.value = true
  try {
    const result = await appsService.getVersions(props.appId, 1, 50)
    versionList.value = result
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to load versions',
      life: 5000,
    })
  } finally {
    isLoading.value = false
  }
}

/**
 * View version details
 */
async function viewVersionDetail(version: AppVersion) {
  try {
    const detail = await appsService.getVersionDetail(props.appId, version.versionNumber)
    selectedVersion.value = detail
    showVersionDetail.value = true
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to load version details',
      life: 5000,
    })
  }
}

/**
 * Create a manual version snapshot
 */
async function createSnapshot() {
  isCreatingSnapshot.value = true
  try {
    await appsService.createVersionSnapshot(props.appId, 'ManualSave')
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Version snapshot created',
      life: 3000,
    })
    emit('versionCreated')
    await loadVersions()
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to create snapshot',
      life: 5000,
    })
  } finally {
    isCreatingSnapshot.value = false
  }
}

/**
 * Restore app to a previous version
 */
async function restoreVersion(versionNumber: number) {
  confirm.require({
    message: `Restore app to version ${versionNumber}? Current files will be replaced.`,
    header: 'Confirm Restore',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Restore',
    rejectLabel: 'Cancel',
    accept: async () => {
      isRestoringVersion.value = true
      try {
        await appsService.restoreVersion(props.appId, versionNumber)
        toast.add({
          severity: 'success',
          summary: 'Success',
          detail: `Restored to version ${versionNumber}`,
          life: 3000,
        })
        emit('versionRestored', versionNumber)
        showVersionDetail.value = false
        await loadVersions()
      } catch (error: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: error instanceof Error ? error.message : 'Failed to restore version',
          life: 5000,
        })
      } finally {
        isRestoringVersion.value = false
      }
    },
  })
}

/**
 * Get trigger severity
 */
function getTriggerSeverity(trigger: string): string {
  switch (trigger) {
    case 'NewSession':
      return 'info'
    case 'ManualSave':
      return 'primary'
    case 'Publish':
      return 'success'
    case 'Restore':
      return 'warning'
    default:
      return 'secondary'
  }
}

/**
 * Get trigger icon
 */
function getTriggerIcon(trigger: string): string {
  switch (trigger) {
    case 'NewSession':
      return 'pi pi-comments'
    case 'ManualSave':
      return 'pi pi-save'
    case 'Publish':
      return 'pi pi-cloud-upload'
    case 'Restore':
      return 'pi pi-refresh'
    default:
      return 'pi pi-clock'
  }
}

/**
 * Format date
 */
function formatDate(dateStr: string): string {
  const date = new Date(dateStr)
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

onMounted(() => {
  loadVersions()
})
</script>

<template>
  <div class="version-history-panel">
    <!-- Header -->
    <div class="panel-header">
      <div class="header-title">
        <i class="pi pi-history text-lg"></i>
        <h3>Version History</h3>
      </div>
      <Button
        v-tooltip.left="'Create manual snapshot'"
        icon="pi pi-camera"
        size="small"
        rounded
        text
        :loading="isCreatingSnapshot"
        @click="createSnapshot"
      />
    </div>

    <Divider />

    <!-- Loading state -->
    <div v-if="isLoading" class="loading-state">
      <ProgressSpinner :style="{ width: '40px', height: '40px' }" stroke-width="4" />
      <p class="mt-2 text-sm text-color-secondary">Loading versions...</p>
    </div>

    <!-- Empty state -->
    <div v-else-if="!versionList || versionList.versions.length === 0" class="empty-state">
      <i class="pi pi-inbox text-3xl text-color-secondary mb-2"></i>
      <p>No versions yet</p>
      <p class="text-sm text-color-secondary">Versions are created when you start a new AI chat or manually create snapshots</p>
    </div>

    <!-- Version list timeline -->
    <ScrollPanel v-else class="versions-scroll">
      <div class="versions-timeline">
        <div
          v-for="(version, index) in versionList.versions"
          :key="version.id"
          class="timeline-item"
        >
          <!-- Timeline marker -->
          <div class="timeline-marker">
            <div class="marker-dot"></div>
            <div v-if="index < versionList.versions.length - 1" class="marker-line"></div>
          </div>

          <!-- Version card -->
          <Card class="version-card">
            <template #header>
              <div class="card-header">
                <div class="version-info">
                  <span class="version-number">v{{ version.versionNumber }}</span>
                  <Tag
                    :value="version.trigger"
                    :severity="getTriggerSeverity(version.trigger)"
                    :icon="getTriggerIcon(version.trigger)"
                    size="small"
                  />
                </div>
                <span class="version-date text-sm text-color-secondary">
                  {{ formatDate(version.createdAt) }}
                </span>
              </div>
            </template>

            <!-- Summary -->
            <div class="card-content">
              <p v-if="version.summary" class="summary">{{ version.summary }}</p>
              <p v-else class="summary empty">No summary available</p>

              <!-- Created by -->
              <p class="created-by text-xs text-color-secondary mt-2">
                Created by {{ version.createdBy }}
              </p>
            </div>

            <!-- Actions -->
            <template #footer>
              <div class="card-actions">
                <Button
                  label="Details"
                  icon="pi pi-info-circle"
                  size="small"
                  text
                  @click="viewVersionDetail(version)"
                />
                <Button
                  label="Restore"
                  icon="pi pi-refresh"
                  size="small"
                  text
                  severity="warn"
                  @click="restoreVersion(version.versionNumber)"
                />
              </div>
            </template>
          </Card>
        </div>
      </div>
    </ScrollPanel>

    <!-- Version detail dialog -->
    <Dialog
      v-model:visible="showVersionDetail"
      modal
      :header="`Version ${selectedVersion?.versionNumber} Details`"
      :style="{ width: '90vw', maxWidth: '800px' }"
    >
      <div v-if="selectedVersion" class="version-detail">
        <!-- Summary -->
        <div class="detail-section">
          <h4>Summary</h4>
          <p v-if="selectedVersion.summary">{{ selectedVersion.summary }}</p>
          <p v-else class="text-color-secondary">No summary available</p>
        </div>

        <!-- File changes -->
        <div class="detail-section">
          <h4>File Changes</h4>
          <Message
            v-if="!selectedVersion.filesChangeSummary"
            severity="info"
            text
            message="No file change summary available"
          />
          <pre v-else class="file-changes">{{ selectedVersion.filesChangeSummary }}</pre>
        </div>

        <!-- Step changes -->
        <div class="detail-section">
          <h4>Step Changes ({{ selectedVersion.stepChanges.length }})</h4>
          <div v-if="selectedVersion.stepChanges.length === 0" class="text-color-secondary">
            No step changes in this version
          </div>
          <div
            v-for="step in selectedVersion.stepChanges"
            :key="step.id"
            class="step-change-row"
          >
            <div class="step-info">
              <span class="step-name">{{ step.stepName }}</span>
              <Tag :value="step.action" size="small" />
            </div>
            <div v-if="step.language" class="step-lang">{{ step.language }}</div>
          </div>
        </div>
      </div>

      <!-- Restore button -->
      <template #footer>
        <Button
          label="Cancel"
          icon="pi pi-times"
          text
          @click="showVersionDetail = false"
        />
        <Button
          label="Restore This Version"
          icon="pi pi-refresh"
          severity="warn"
          :loading="isRestoringVersion"
          @click="selectedVersion && restoreVersion(selectedVersion.versionNumber)"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.version-history-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  background-color: var(--p-surface-ground);
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 1rem;
  flex-shrink: 0;
}

.header-title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.header-title h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 600;
}

.loading-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2rem 1rem;
  text-align: center;
  color: var(--p-text-muted-color);
}

.versions-scroll {
  flex: 1;
  overflow: auto;
}

.versions-timeline {
  padding: 1rem;
}

.timeline-item {
  display: flex;
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.timeline-marker {
  display: flex;
  flex-direction: column;
  align-items: center;
  position: relative;
  width: 24px;
  flex-shrink: 0;
}

.marker-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background-color: var(--p-primary-500);
  border: 2px solid var(--p-surface-ground);
  position: relative;
  z-index: 1;
}

.marker-line {
  width: 2px;
  flex: 1;
  background: linear-gradient(to bottom, var(--p-primary-300), transparent);
  position: absolute;
  top: 12px;
  left: 50%;
  transform: translateX(-50%);
}

.version-card {
  flex: 1;
  background-color: var(--p-surface-50);
  border: 1px solid var(--p-surface-200);
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 0;
  background-color: transparent;
  border-bottom: 1px solid var(--p-surface-200);
}

.version-info {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.version-number {
  font-weight: 600;
  color: var(--p-text-color);
}

.version-date {
  white-space: nowrap;
  margin-left: auto;
}

.card-content {
  padding: 0;
}

.summary {
  font-size: 0.875rem;
  line-height: 1.5;
  margin: 0;
  color: var(--p-text-muted-color);
}

.summary.empty {
  font-style: italic;
}

.created-by {
  display: block;
}

.card-actions {
  display: flex;
  gap: 0.5rem;
  justify-content: flex-end;
  padding: 0;
  background-color: transparent;
  border-top: 1px solid var(--p-surface-200);
}

.version-detail {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  max-height: 70vh;
  overflow-y: auto;
}

.detail-section {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.detail-section h4 {
  margin: 0;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--p-text-color);
}

.file-changes {
  background-color: var(--p-surface-100);
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  padding: 0.75rem;
  font-size: 0.75rem;
  max-height: 200px;
  overflow: auto;
  margin: 0;
}

.step-change-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem;
  background-color: var(--p-surface-50);
  border-radius: var(--p-border-radius);
  border: 1px solid var(--p-surface-200);
  margin-bottom: 0.5rem;
}

.step-info {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.step-name {
  font-weight: 500;
  font-size: 0.875rem;
}

.step-lang {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}
</style>
