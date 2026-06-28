<script setup lang="ts">
/**
 * AppRuntimeLogs.vue
 * Shows historical application/backend execution records for the current app.
 */
import { ref, onMounted, watch } from 'vue'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import InputText from 'primevue/inputtext'
import { appExecutionLogService } from '@/shared/services/api/app-execution-log.service'
import type { AppExecutionLog, AppExecutionLogFilter } from '@/shared/types/ai.types'

const props = defineProps<{
  appId: string
}>()

const logs = ref<AppExecutionLog[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const isLoading = ref(false)
const expandedLogId = ref<string | null>(null)

const filter = ref<AppExecutionLogFilter>({
  page: 1,
  pageSize: 50,
})
const controllerFilter = ref('')
const statusFilter = ref<'all' | 'success' | 'error'>('all')

async function loadLogs() {
  if (!props.appId) return
  isLoading.value = true
  try {
    const f: AppExecutionLogFilter = {
      ...filter.value,
      controllerName: controllerFilter.value || undefined,
      success: statusFilter.value === 'all' ? undefined : statusFilter.value === 'success',
    }
    const result = await appExecutionLogService.getLogs(props.appId, f)
    logs.value = result.items
    totalCount.value = result.totalCount
    totalPages.value = result.totalPages
  } catch (err) {
    console.error('Failed to load execution logs', err)
  } finally {
    isLoading.value = false
  }
}

function applyFilter() {
  filter.value.page = 1
  loadLogs()
}

function prevPage() {
  if ((filter.value.page ?? 1) > 1) {
    filter.value.page = (filter.value.page ?? 1) - 1
    loadLogs()
  }
}

function nextPage() {
  if ((filter.value.page ?? 1) < totalPages.value) {
    filter.value.page = (filter.value.page ?? 1) + 1
    loadLogs()
  }
}

function toggleLog(id: string) {
  expandedLogId.value = expandedLogId.value === id ? null : id
}

function formatDate(dateStr: string) {
  return new Date(dateStr).toLocaleString()
}

function formatDuration(ms: number) {
  if (ms >= 1000) return `${(ms / 1000).toFixed(2)}s`
  return `${ms}ms`
}

function tryFormatJson(raw?: string): string {
  if (!raw) return ''
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
}

function formatRuntimeEvent(event: { level: string; message: string }): string {
  return `${event.level.toUpperCase()} ${event.message}`
}

function getPrimarySummary(log: AppExecutionLog): string {
  const firstEvent = log.runtimeEvents[0]
  if (firstEvent) {
    return formatRuntimeEvent(firstEvent)
  }

  if (log.errorMessage?.trim()) {
    return log.errorMessage.trim()
  }

  return `${log.controllerName}.${log.actionName}()`
}

function getSecondarySummary(log: AppExecutionLog): string {
  const parts = [`${log.controllerName}.${log.actionName}()`]
  parts.push(log.success ? 'Success' : 'Failed')
  parts.push(formatDuration(log.durationMs))
  return parts.join(' • ')
}

function getOutputCountLabel(log: AppExecutionLog): string | null {
  if (log.runtimeEvents.length === 0) {
    return null
  }

  return `${log.runtimeEvents.length} ${log.runtimeEvents.length === 1 ? 'entry' : 'entries'}`
}

onMounted(loadLogs)
watch(() => props.appId, loadLogs)

defineExpose({ loadLogs })
</script>

<template>
  <div class="app-runtime-logs">
    <!-- Filter bar -->
    <div class="filter-bar">
      <div class="filter-left">
        <InputText
          v-model="controllerFilter"
          placeholder="Controller name"
          size="small"
          class="filter-input"
          @keyup.enter="applyFilter"
        />
        <div class="status-buttons">
          <button
            class="status-btn"
            :class="{ active: statusFilter === 'all' }"
            @click="statusFilter = 'all'; applyFilter()"
          >All</button>
          <button
            class="status-btn success"
            :class="{ active: statusFilter === 'success' }"
            @click="statusFilter = 'success'; applyFilter()"
          >Success</button>
          <button
            class="status-btn error"
            :class="{ active: statusFilter === 'error' }"
            @click="statusFilter = 'error'; applyFilter()"
          >Error</button>
        </div>
      </div>
      <div class="filter-right">
        <span class="text-color-secondary text-sm">{{ totalCount }} records</span>
        <Button
          v-tooltip.bottom="'Refresh'"
          icon="pi pi-refresh"
          text
          rounded
          severity="secondary"
          size="small"
          :loading="isLoading"
          @click="loadLogs"
        />
      </div>
    </div>

    <!-- Loading -->
    <div v-if="isLoading" class="flex justify-content-center align-items-center py-5">
      <ProgressSpinner style="width: 32px; height: 32px" />
    </div>

    <!-- Empty -->
    <div v-else-if="logs.length === 0" class="empty-state">
      <i class="pi pi-server text-4xl text-color-secondary mb-2"></i>
      <p class="text-color-secondary">No runtime logs yet.</p>
      <p class="text-color-secondary text-sm">Logs appear after the running application triggers backend actions.</p>
    </div>

    <!-- Log list -->
      <div v-else class="logs-list">
        <div
          v-for="log in logs"
          :key="log.id"
          class="log-row"
          :class="{ 'log-error': !log.success, expanded: expandedLogId === log.id }"
        @click="toggleLog(log.id)"
      >
        <!-- Summary row -->
        <div class="log-summary">
          <div class="summary-main">
            <i
              class="pi"
              :class="expandedLogId === log.id ? 'pi-chevron-down' : 'pi-chevron-right'"
              style="font-size: 0.7rem; flex-shrink: 0"
            ></i>
            <Tag
              :value="log.success ? 'OK' : 'ERR'"
              :severity="log.success ? 'success' : 'danger'"
              style="font-size: 0.65rem; flex-shrink: 0"
            />
            <span class="log-primary">{{ getPrimarySummary(log) }}</span>
          </div>
          <div class="summary-meta">
            <Tag
              v-if="getOutputCountLabel(log)"
              :value="getOutputCountLabel(log)!"
              severity="secondary"
              rounded
              style="font-size: 0.65rem; flex-shrink: 0"
            />
            <span class="log-date text-color-secondary">{{ formatDate(log.createdAt) }}</span>
          </div>
        </div>

        <div class="log-secondary">
          {{ getSecondarySummary(log) }}
        </div>

        <!-- Error line (collapsed) -->
        <div v-if="!log.success && expandedLogId !== log.id && log.errorMessage" class="log-error-hint">
          {{ log.errorMessage.slice(0, 150) }}{{ log.errorMessage.length > 150 ? '…' : '' }}
        </div>

        <!-- Expanded detail -->
        <div v-if="expandedLogId === log.id" class="log-detail" @click.stop>
          <div v-if="log.runtimeEvents.length > 0" class="detail-section">
            <div class="detail-label">Backend Output</div>
            <pre class="detail-code detail-output">{{ log.runtimeEvents.map(formatRuntimeEvent).join('\n') }}</pre>
          </div>
          <div v-if="log.errorMessage" class="detail-section error-section">
            <div class="detail-label">Error</div>
            <pre class="detail-code">{{ log.errorMessage }}</pre>
          </div>
          <div class="detail-section">
            <div class="detail-label">Execution</div>
            <div class="detail-grid">
              <span class="detail-grid-label">Action</span>
              <span>{{ log.controllerName }}.{{ log.actionName }}()</span>
              <span class="detail-grid-label">Status</span>
              <span>{{ log.success ? 'Success' : 'Failed' }}</span>
              <span class="detail-grid-label">Duration</span>
              <span>{{ formatDuration(log.durationMs) }}</span>
              <span class="detail-grid-label">Recorded</span>
              <span>{{ formatDate(log.createdAt) }}</span>
            </div>
          </div>
          <div v-if="log.requestPayload" class="detail-section">
            <div class="detail-label">Request</div>
            <pre class="detail-code">{{ tryFormatJson(log.requestPayload) }}</pre>
          </div>
          <div v-if="log.responsePayload" class="detail-section">
            <div class="detail-label">Response</div>
            <pre class="detail-code">{{ tryFormatJson(log.responsePayload) }}</pre>
          </div>
          <div class="detail-meta">
            <span v-if="log.ipAddress">IP: {{ log.ipAddress }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Pagination -->
    <div v-if="totalPages > 1" class="pagination">
      <Button icon="pi pi-chevron-left" text size="small" :disabled="(filter.page ?? 1) <= 1" @click="prevPage" />
      <span class="text-sm text-color-secondary">Page {{ filter.page }} of {{ totalPages }}</span>
      <Button icon="pi pi-chevron-right" text size="small" :disabled="(filter.page ?? 1) >= totalPages" @click="nextPage" />
    </div>
  </div>
</template>

<style scoped>
.app-runtime-logs {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.filter-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--p-surface-200);
  gap: 0.5rem;
  flex-shrink: 0;
}

.filter-left {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.filter-right {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.filter-input {
  width: 150px;
  font-size: 0.8rem;
}

.status-buttons {
  display: flex;
  gap: 2px;
}

.status-btn {
  padding: 2px 8px;
  font-size: 0.75rem;
  border: 1px solid var(--p-surface-300);
  border-radius: 4px;
  background: transparent;
  cursor: pointer;
  color: var(--p-text-color);
}

.status-btn:hover,
.status-btn.active {
  background: var(--p-surface-100);
}

.status-btn.success.active {
  border-color: var(--p-green-500);
  color: var(--p-green-500);
}

.status-btn.error.active {
  border-color: var(--p-red-500);
  color: var(--p-red-500);
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
  padding: 2rem;
  text-align: center;
}

.logs-list {
  flex: 1;
  overflow-y: auto;
}

.log-row {
  border-bottom: 1px solid var(--p-surface-100);
  cursor: pointer;
  padding: 0.55rem 0.75rem;
  transition: background 0.15s;
}

.log-row:hover {
  background: var(--p-surface-50);
}

.log-row.log-error {
  border-left: 3px solid var(--p-red-400);
}

.log-summary {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
  min-width: 0;
}

.summary-main {
  display: flex;
  align-items: center;
  gap: 0.45rem;
  min-width: 0;
}

.summary-meta {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-shrink: 0;
}

.log-primary {
  font-size: 0.82rem;
  font-weight: 600;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.log-date {
  font-size: 0.72rem;
  flex-shrink: 0;
}

.log-secondary {
  font-size: 0.74rem;
  color: var(--p-text-muted-color);
  padding: 0.15rem 0 0 1.55rem;
}

.log-error-hint {
  font-size: 0.72rem;
  color: var(--p-red-400);
  padding: 0.1rem 1.5rem 0.2rem;
}

.log-detail {
  padding: 0.5rem 1.5rem 0.5rem;
  background: var(--p-surface-50);
}

.detail-section {
  margin-bottom: 0.5rem;
}

.error-section .detail-code {
  color: var(--p-red-400);
}

.detail-label {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--p-text-muted-color);
  margin-bottom: 0.2rem;
}

.detail-code {
  font-size: 0.75rem;
  font-family: monospace;
  white-space: pre-wrap;
  word-break: break-all;
  background: var(--p-surface-100);
  border-radius: 4px;
  padding: 0.4rem 0.6rem;
  max-height: 200px;
  overflow-y: auto;
  margin: 0;
}

.detail-output {
  max-height: 260px;
}

.detail-grid {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.25rem 0.75rem;
  font-size: 0.78rem;
}

.detail-grid-label {
  color: var(--p-text-muted-color);
  font-weight: 600;
}

.detail-meta {
  font-size: 0.7rem;
  color: var(--p-text-muted-color);
}

.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 0.5rem;
  border-top: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}
</style>
