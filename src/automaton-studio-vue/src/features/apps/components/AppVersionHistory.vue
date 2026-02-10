<script setup lang="ts">
/**
 * AppVersionHistory.vue
 * Displays app version history and allows restoration from previous versions
 * Shows timeline of snapshots with triggers and summaries
 */
import { ref, onMounted, defineExpose, computed } from 'vue'
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
 * Load version history (exposed for parent component to call)
 */
const loadVersions = async () => {
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
 * Parse summary JSON (if it's JSON format with overview and changes)
 */
const parsedSummary = computed(() => {
  if (!selectedVersion.value?.summary) {
    return null
  }

  try {
    const parsed = JSON.parse(selectedVersion.value.summary)
    if (parsed.overview && Array.isArray(parsed.changes)) {
      return parsed as { overview: string; changes: string[] }
    }
  } catch {
    // Not JSON, return null - template will fall back to plain text
  }

  return null
})

/**
 * Parse a summary string (JSON or plain text) for display in version cards
 */
function parseSummary(summary: string | undefined): { overview: string; changes: string[] } | null {
  if (!summary) return null

  try {
    const parsed = JSON.parse(summary)
    if (parsed.overview && Array.isArray(parsed.changes)) {
      return parsed as { overview: string; changes: string[] }
    }
  } catch {
    // Not JSON, return null
  }

  return null
}

/**
 * Get display text for version card (overview only if JSON, otherwise full text)
 */
function getCardSummary(summary: string | undefined): string {
  if (!summary) return 'No summary available'
  
  const parsed = parseSummary(summary)
  if (parsed) {
    return parsed.overview
  }
  
  return summary
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
 * Get trigger label for display
 */
function getTriggerLabel(trigger: string): string {
  switch (trigger) {
    case 'NewSession':
      return 'AI Chat'
    case 'ManualSave':
      return 'Manual'
    case 'Publish':
      return 'Publish'
    case 'Restore':
      return 'Restore'
    default:
      return trigger
  }
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

// Expose loadVersions to parent component for manual refresh
defineExpose({
  loadVersions,
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
      <i class="pi pi-history text-3xl text-color-secondary mb-2"></i>
      <p>No versions yet</p>
      <p class="text-sm text-color-secondary mb-3">
        Versions are created when you start a new AI chat or manually create snapshots
      </p>
      <Button
        label="Create Snapshot"
        icon="pi pi-camera"
        size="small"
        :loading="isCreatingSnapshot"
        @click="createSnapshot"
      />
    </div>

    <!-- Version list -->
    <ScrollPanel v-else class="versions-scroll">
      <div class="versions-list">
        <div
          v-for="version in versionList.versions"
          :key="version.id"
          class="version-card"
          @click="viewVersionDetail(version)"
        >
          <div class="version-card-content">
            <!-- Header -->
            <div class="version-card-header">
              <div class="version-title-row">
                <span class="version-number">Version {{ version.versionNumber }}</span>
                <Tag
                  :value="getTriggerLabel(version.trigger)"
                  :severity="getTriggerSeverity(version.trigger)"
                  :icon="getTriggerIcon(version.trigger)"
                  size="small"
                />
              </div>
              <div class="version-card-actions" @click.stop>
                <Button
                  v-tooltip.top="'View details'"
                  icon="pi pi-info-circle"
                  size="small"
                  text
                  @click="viewVersionDetail(version)"
                />
                <Button
                  v-tooltip.top="'Restore this version'"
                  icon="pi pi-refresh"
                  size="small"
                  text
                  severity="warn"
                  @click="restoreVersion(version.versionNumber)"
                />
              </div>
            </div>

            <!-- Summary -->
            <div class="version-summary">
              <p class="summary-text">{{ getCardSummary(version.summary) }}</p>
            </div>

            <!-- Footer metadata -->
            <div class="version-card-footer">
              <div class="meta-item">
                <i class="pi pi-calendar text-xs"></i>
                <span class="text-xs">{{ formatDate(version.createdAt) }}</span>
              </div>
              <div class="meta-item">
                <i class="pi pi-user text-xs"></i>
                <span class="text-xs">{{ version.createdBy }}</span>
              </div>
              <div v-if="version.aiModel" class="meta-item">
                <i class="pi pi-bolt text-xs"></i>
                <span class="text-xs">{{ version.aiModel }}</span>
              </div>
            </div>
          </div>
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
          
          <!-- AI-generated structured summary -->
          <template v-if="parsedSummary">
            <p class="summary-overview">{{ parsedSummary.overview }}</p>
            <div v-if="parsedSummary.changes.length > 0" class="summary-changes">
              <h5>Changes:</h5>
              <ul class="changes-list">
                <li v-for="(change, index) in parsedSummary.changes" :key="index">
                  {{ change }}
                </li>
              </ul>
            </div>
          </template>
          
          <!-- Plain text summary or no summary -->
          <template v-else>
            <p v-if="selectedVersion.summary" class="summary-text">{{ selectedVersion.summary }}</p>
            <p v-else class="text-color-secondary">No summary available</p>
          </template>
        </div>

        <!-- Metadata -->
        <div class="detail-section">
          <h4>Version Info</h4>
          <div class="detail-grid">
            <div class="detail-row">
              <span class="detail-label">Version:</span>
              <span class="detail-value">{{ selectedVersion.versionNumber }}</span>
            </div>
            <div class="detail-row">
              <span class="detail-label">Trigger:</span>
              <Tag
                :value="getTriggerLabel(selectedVersion.trigger)"
                :severity="getTriggerSeverity(selectedVersion.trigger)"
                :icon="getTriggerIcon(selectedVersion.trigger)"
                size="small"
              />
            </div>
            <div class="detail-row">
              <span class="detail-label">Created:</span>
              <span class="detail-value">{{ formatDate(selectedVersion.createdAt) }}</span>
            </div>
            <div class="detail-row">
              <span class="detail-label">Created By:</span>
              <span class="detail-value">{{ selectedVersion.createdBy }}</span>
            </div>
            <div v-if="selectedVersion.aiModel" class="detail-row">
              <span class="detail-label">AI Model:</span>
              <span class="detail-value">{{ selectedVersion.aiModel }}</span>
            </div>
          </div>
        </div>

        <!-- File changes -->
        <div class="detail-section">
          <h4>File Changes</h4>
          <Message
            v-if="!selectedVersion.filesChangeSummary"
            severity="info"
            :closable="false"
          >
            No file change summary available
          </Message>
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

.versions-list {
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.version-card {
  cursor: pointer;
  transition: all 0.2s ease;
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  padding: 0.75rem;
}

.version-card:hover {
  border-color: var(--p-primary-300);
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.version-card-content {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.version-card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 0.5rem;
}

.version-title-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 1;
}

.version-number {
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--p-text-color);
}

.version-card-actions {
  display: flex;
  gap: 0.25rem;
  opacity: 0;
  transition: opacity 0.2s ease;
}

.version-card:hover .version-card-actions {
  opacity: 1;
}

.version-summary {
  padding: 0.25rem 0;
}

.summary-text {
  font-size: 0.875rem;
  line-height: 1.5;
  margin: 0;
  color: var(--p-text-color);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  text-overflow: ellipsis;
}

.summary-overview {
  font-size: 0.875rem;
  line-height: 1.6;
  margin: 0 0 1rem 0;
  color: var(--p-text-color);
}

.summary-changes h5 {
  font-size: 0.8125rem;
  font-weight: 600;
  margin: 0 0 0.5rem 0;
  color: var(--p-text-color);
}

.changes-list {
  margin: 0;
  padding-left: 1.5rem;
  list-style-type: disc;
}

.changes-list li {
  font-size: 0.8125rem;
  line-height: 1.5;
  margin-bottom: 0.375rem;
  color: var(--p-text-color);
}

.changes-list li:last-child {
  margin-bottom: 0;
}

.version-card-footer {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  padding-top: 0.25rem;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  color: var(--p-text-muted-color);
}

/* Version detail dialog */
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

.detail-grid {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.detail-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem;
  border-radius: var(--p-border-radius);
}

.detail-label {
  font-weight: 500;
  min-width: 100px;
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}

.detail-value {
  color: var(--p-text-color);
  font-size: 0.875rem;
}

.file-changes {
  background: var(--p-surface-ground);
  color: var(--p-text-color);
  border: 1px solid var(--p-surface-border);
  border-radius: var(--p-border-radius);
  padding: 0.75rem;
  font-size: 0.75rem;
  max-height: 200px;
  overflow: auto;
  margin: 0;
  font-family: 'Monaco', 'Courier New', monospace;
}

.step-change-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem;
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
