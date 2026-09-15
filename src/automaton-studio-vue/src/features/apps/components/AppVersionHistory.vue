<script setup lang="ts">
/**
 * AppVersionHistory.vue
 * Displays read-only source snapshot history.
 */
import { ref, watch, onMounted, onUnmounted, computed } from 'vue'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import Divider from 'primevue/divider'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import InputText from 'primevue/inputtext'
import { appsService } from '../services/apps.service'
import type { AppVersion, AppVersionDetail, AppVersionList } from '../types/apps.types'

interface Props {
  appId: string
  title?: string
  fullPage?: boolean
}

const props = defineProps<Props>()
const toast = useToast()

const isLoading = ref(false)
const isLoadingMore = ref(false)
const versionList = ref<AppVersionList | null>(null)
const selectedVersion = ref<AppVersionDetail | null>(null)
const showVersionDetail = ref(false)
const searchQuery = ref('')
const versionsPageSize = 20

const hasMore = computed(() => {
  if (!versionList.value) return false
  return versionList.value.versions.length < versionList.value.totalCount
})

/**
 * Load version history page 1 (exposed for parent component to call)
 */
const loadVersions = async (search?: string) => {
  if (!props.appId) return
  if (search !== undefined) searchQuery.value = search

  isLoading.value = true
  try {
    const result = await appsService.getVersions(props.appId, 1, versionsPageSize, searchQuery.value || undefined)
    versionList.value = result
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to load source snapshots',
      life: 5000,
    })
  } finally {
    isLoading.value = false
  }
}

/**
 * Append the next page (infinite scroll)
 */
async function loadMoreVersions() {
  if (!props.appId || !versionList.value || !hasMore.value || isLoadingMore.value) return

  isLoadingMore.value = true
  const nextPage = versionList.value.pageNumber + 1
  try {
    const result = await appsService.getVersions(props.appId, nextPage, versionsPageSize, searchQuery.value || undefined)
    versionList.value = {
      ...result,
      versions: [...versionList.value.versions, ...result.versions],
    }
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to load more source snapshots',
      life: 5000,
    })
  } finally {
    isLoadingMore.value = false
  }
}

// Debounced search — watch covers both typing and the clear-button click
let searchDebounceTimer: ReturnType<typeof setTimeout> | null = null
watch(searchQuery, (value) => {
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer)
  searchDebounceTimer = setTimeout(() => {
    void loadVersions(value.trim() || undefined)
  }, 300)
})

// Infinite scroll sentinel
const scrollSentinelRef = ref<HTMLElement | null>(null)
let intersectionObserver: IntersectionObserver | null = null

function setupIntersectionObserver() {
  if (intersectionObserver) intersectionObserver.disconnect()
  if (!scrollSentinelRef.value) return
  intersectionObserver = new IntersectionObserver(
    (entries) => {
      if (entries[0]?.isIntersecting && hasMore.value) {
        void loadMoreVersions()
      }
    },
    { threshold: 0.1 }
  )
  intersectionObserver.observe(scrollSentinelRef.value)
}

watch(scrollSentinelRef, (el) => {
  if (el) setupIntersectionObserver()
})

onUnmounted(() => {
  intersectionObserver?.disconnect()
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer)
})

/**
 * View restore point details
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
      detail: error instanceof Error ? error.message : 'Failed to load source snapshot details',
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
 * Parse file changes JSON for display
 */
const parsedFileChanges = computed(() => {
  if (!selectedVersion.value?.filesChangeSummary) {
    return null
  }

  try {
    const parsed = JSON.parse(selectedVersion.value.filesChangeSummary)
    return {
      totalChanges: parsed.totalChanges || 0,
      added: (parsed.added || []) as string[],
      modified: (parsed.modified || []) as string[],
      deleted: (parsed.deleted || []) as string[],
    }
  } catch {
    return null
  }
})

/**
 * Get trigger label for display
 */
function getTriggerLabel(trigger: string): string {
  switch (trigger) {
    case 'NewGeneration':
      return 'AI Edit'
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

function getSourceLabel(version: Pick<AppVersion, 'trigger'> & { source?: string }): string {
  const source = version.source || getFallbackSource(version.trigger)
  switch (source) {
    case 'AiChat':
      return 'AI Chat'
    case 'CliDeploy':
      return 'CLI Deploy'
    case 'Studio':
      return 'Studio'
    default:
      return source
  }
}

function getReasonLabel(version: Pick<AppVersion, 'trigger'> & { reason?: string }): string {
  return version.reason || getFallbackReason(version.trigger)
}

function getFallbackSource(trigger: string): string {
  switch (trigger) {
    case 'NewGeneration':
      return 'AiChat'
    default:
      return 'Studio'
  }
}

function getFallbackReason(trigger: string): string {
  switch (trigger) {
    case 'NewGeneration':
      return 'After AI Session'
    case 'Publish':
      return 'Publish'
    case 'Restore':
      return 'Restore'
    default:
      return 'Manual'
  }
}

/**
 * Get trigger severity
 */
function getTriggerSeverity(trigger: string): string {
  switch (trigger) {
    case 'NewGeneration':
      return 'secondary'
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
    case 'NewGeneration':
      return 'pi pi-sparkles'
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

function getChangedFileCount(version: Pick<AppVersion, 'summary'> & { filesChangeSummary?: string }): number | null {
  if (!version.filesChangeSummary) {
    return null
  }

  try {
    const parsed = JSON.parse(version.filesChangeSummary)
    const totalChanges = Number(parsed.totalChanges)
    return Number.isFinite(totalChanges) ? totalChanges : null
  } catch {
    return null
  }
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
  <div class="version-history-panel" :class="{ 'version-history-panel--page': fullPage }">
    <!-- Header -->
    <div class="panel-header">
      <div class="header-title">
        <i class="pi pi-history text-lg"></i>
        <h3>{{ title || 'Source snapshots' }}</h3>
      </div>
    </div>

    <Divider />

    <!-- Search -->
    <div class="px-2 pb-2">
      <IconField class="w-full">
        <InputIcon
          :class="searchQuery ? 'pi pi-times cursor-pointer' : 'pi pi-search'"
          @click="searchQuery ? (searchQuery = '') : undefined"
        />
        <InputText
          v-model="searchQuery"
          placeholder="Search source snapshots..."
          size="small"
          class="w-full"
        />
      </IconField>
    </div>

    <!-- Loading state -->
    <div v-if="isLoading" class="loading-state">
      <ProgressSpinner :style="{ width: '40px', height: '40px' }" stroke-width="4" />
      <p class="mt-2 text-sm text-color-secondary">Loading source snapshots...</p>
    </div>

    <!-- Empty state -->
    <div v-else-if="!versionList || versionList.versions.length === 0" class="empty-state">
      <i class="pi pi-history text-3xl text-color-secondary mb-2"></i>
      <p>No source snapshots yet</p>
      <p class="text-sm text-color-secondary mb-3">
        Source snapshots record Studio's server-side source copy. They do not represent deployed runtime versions.
      </p>
    </div>

    <!-- Version list -->
    <div v-else class="versions-scroll">
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
                <span class="version-number">Snapshot {{ version.versionNumber }}</span>
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
                <i class="pi pi-desktop text-xs"></i>
                <span class="text-xs">{{ getSourceLabel(version) }}</span>
              </div>
              <div class="meta-item">
                <i class="pi pi-comment text-xs"></i>
                <span class="text-xs">{{ getReasonLabel(version) }}</span>
              </div>
              <div class="meta-item">
                <i class="pi pi-user text-xs"></i>
                <span class="text-xs">{{ version.createdBy }}</span>
              </div>
              <div v-if="version.aiModel" class="meta-item">
                <i class="pi pi-bolt text-xs"></i>
                <span class="text-xs">{{ version.aiModel }}</span>
              </div>
              <div v-if="getChangedFileCount(version) !== null" class="meta-item">
                <i class="pi pi-file-edit text-xs"></i>
                <span class="text-xs">
                  {{ getChangedFileCount(version) }}
                  {{ getChangedFileCount(version) === 1 ? 'file changed' : 'files changed' }}
                </span>
              </div>
            </div>
          </div>
        </div>

        <!-- Infinite scroll sentinel -->
        <div ref="scrollSentinelRef" class="scroll-sentinel">
          <i v-if="isLoadingMore" class="pi pi-spin pi-spinner text-color-secondary"></i>
        </div>
      </div>
    </div>

    <!-- Version detail dialog -->
    <Dialog
      v-model:visible="showVersionDetail"
      modal
      :header="`Source snapshot ${selectedVersion?.versionNumber}`"
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
          <h4>Source snapshot information</h4>
          <div class="detail-grid">
            <div class="detail-row">
              <span class="detail-label">Snapshot:</span>
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
              <span class="detail-label">Source:</span>
              <span class="detail-value">{{ getSourceLabel(selectedVersion) }}</span>
            </div>
            <div class="detail-row">
              <span class="detail-label">Reason:</span>
              <span class="detail-value">{{ getReasonLabel(selectedVersion) }}</span>
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
          <div v-else-if="parsedFileChanges" class="file-changes-list">
            <div class="file-changes-summary">
              <strong>{{ parsedFileChanges.totalChanges }}</strong> 
              {{ parsedFileChanges.totalChanges === 1 ? 'file' : 'files' }} changed
            </div>
            
            <div v-if="parsedFileChanges.added.length > 0" class="file-change-group">
              <div class="file-change-header added">
                <i class="pi pi-plus-circle"></i>
                <span>Added ({{ parsedFileChanges.added.length }})</span>
              </div>
              <div class="file-list">
                <div v-for="file in parsedFileChanges.added" :key="file" class="file-item">
                  <i class="pi pi-file text-xs"></i>
                  <span>{{ file }}</span>
                </div>
              </div>
            </div>

            <div v-if="parsedFileChanges.modified.length > 0" class="file-change-group">
              <div class="file-change-header modified">
                <i class="pi pi-pencil"></i>
                <span>Modified ({{ parsedFileChanges.modified.length }})</span>
              </div>
              <div class="file-list">
                <div v-for="file in parsedFileChanges.modified" :key="file" class="file-item">
                  <i class="pi pi-file text-xs"></i>
                  <span>{{ file }}</span>
                </div>
              </div>
            </div>

            <div v-if="parsedFileChanges.deleted.length > 0" class="file-change-group">
              <div class="file-change-header deleted">
                <i class="pi pi-minus-circle"></i>
                <span>Deleted ({{ parsedFileChanges.deleted.length }})</span>
              </div>
              <div class="file-list">
                <div v-for="file in parsedFileChanges.deleted" :key="file" class="file-item">
                  <i class="pi pi-file text-xs"></i>
                  <span>{{ file }}</span>
                </div>
              </div>
            </div>
          </div>
          <pre v-else class="file-changes">{{ selectedVersion.filesChangeSummary }}</pre>
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
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.snapshot-dialog {
  padding: 0.5rem 0;
}

.version-history-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.version-history-panel--page {
  background: var(--p-surface-0);
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

.version-history-panel--page .versions-list {
  padding: 1rem 1.25rem 1.25rem;
}

.scroll-sentinel {
  display: flex;
  justify-content: center;
  padding: 0.5rem 0;
  min-height: 1px;
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

.file-changes-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.file-changes-summary {
  font-size: 0.875rem;
  color: var(--p-text-color);
  padding: 0.5rem;
  background: var(--p-surface-50);
  border-radius: var(--p-border-radius);
  border-left: 3px solid var(--p-primary-500);
}

.file-change-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.file-change-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 600;
  font-size: 0.8125rem;
  padding: 0.25rem 0;
}

.file-change-header.added {
  color: var(--p-green-600);
}

.file-change-header.modified {
  color: var(--p-blue-600);
}

.file-change-header.deleted {
  color: var(--p-red-600);
}

.file-list {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding-left: 1.5rem;
}

.file-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8125rem;
  color: var(--p-text-color);
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  padding: 0.25rem 0;
}

.file-item i {
  color: var(--p-text-muted-color);
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
