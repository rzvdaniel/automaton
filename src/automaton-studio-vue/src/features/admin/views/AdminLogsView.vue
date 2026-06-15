<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import apiClient from '@/shared/services/api/axios-instance'

interface AuditEventEntry {
  id: string
  action: string
  entityType: string
  entityId?: string | null
  userId?: string | null
  targetUserId?: string | null
  userName?: string | null
  outcome: string
  eventType: string
  message: string
  metadataJson?: string | null
  ipAddress?: string | null
  createdAtUtc: string
}

interface AuditEventPage {
  items: AuditEventEntry[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

const route = useRoute()
const logs = ref<AuditEventEntry[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const isLoading = ref(false)
const expandedLogId = ref<string | null>(null)
const availableLevels = ref<string[]>([])
const autoRefresh = ref(false)
let autoRefreshTimer: ReturnType<typeof setInterval> | null = null

const filter = ref({
  level: null as string | null,
  from: null as Date | null,
  to: null as Date | null,
  search: '',
  page: 1,
  pageSize: 100,
})

function getQueryValue(value: unknown): string | null {
  if (typeof value === 'string' && value.trim().length > 0) {
    return value
  }

  return null
}

function syncFilterFromRoute() {
  filter.value.level = getQueryValue(route.query.outcome)
  filter.value.search = getQueryValue(route.query.search) ?? ''
  filter.value.page = 1
}

async function loadLevels() {
  try {
    const { data } = await apiClient.get<string[]>('/api/admin/logs/levels')
    availableLevels.value = data
  } catch (err) {
    console.warn('Failed to load log levels', err)
  }
}

async function loadLogs() {
  isLoading.value = true
  try {
    const params: Record<string, number | string> = {
      page: filter.value.page,
      pageSize: filter.value.pageSize,
    }
    if (filter.value.level) params.outcome = filter.value.level
    if (filter.value.from) params.fromUtc = filter.value.from.toISOString()
    if (filter.value.to) params.toUtc = filter.value.to.toISOString()
    if (filter.value.search) params.search = filter.value.search

    const { data } = await apiClient.get<AuditEventPage>('/api/admin/logs/audit-events', { params })
    logs.value = data.items
    totalCount.value = data.totalCount
    totalPages.value = data.totalPages
  } catch (err) {
    console.error('Failed to load logs', err)
  } finally {
    isLoading.value = false
  }
}

function applyFilter() {
  filter.value.page = 1
  loadLogs()
}

function prevPage() {
  if (filter.value.page > 1) {
    filter.value.page--
    loadLogs()
  }
}

function nextPage() {
  if (filter.value.page < totalPages.value) {
    filter.value.page++
    loadLogs()
  }
}

function toggleLog(id: string) {
  expandedLogId.value = expandedLogId.value === id ? null : id
}

function getOutcomeSeverity(outcome?: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
  switch (outcome?.toLowerCase()) {
    case 'success': return 'success'
    case 'failed': return 'danger'
    case 'denied': return 'warn'
    default: return 'secondary'
  }
}

function getOutcomeLabel(outcome?: string) {
  return outcome?.toUpperCase() ?? '?'
}

function formatDate(dateStr?: string) {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleString()
}

function tryFormatJson(raw?: string): string {
  if (!raw) return ''
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
}

function toggleAutoRefresh() {
  autoRefresh.value = !autoRefresh.value
  if (autoRefresh.value) {
    autoRefreshTimer = setInterval(loadLogs, 10000)
  } else if (autoRefreshTimer) {
    clearInterval(autoRefreshTimer)
    autoRefreshTimer = null
  }
}

onMounted(() => {
  syncFilterFromRoute()
  void loadLevels()
  void loadLogs()
})

onUnmounted(() => {
  if (autoRefreshTimer) clearInterval(autoRefreshTimer)
})

watch(
  () => [route.query.outcome, route.query.search],
  ([outcome, search], [previousOutcome, previousSearch]) => {
    if (outcome === previousOutcome && search === previousSearch) {
      return
    }

    syncFilterFromRoute()
    void loadLogs()
  },
)
</script>

<template>
  <div class="admin-logs-view">
    <div class="logs-header">
      <h2 class="logs-title">
        <i class="pi pi-list mr-2"></i>Audit Events
      </h2>
      <div class="logs-actions">
        <Button
          :icon="autoRefresh ? 'pi pi-pause' : 'pi pi-play'"
          :label="autoRefresh ? 'Auto-refresh ON' : 'Auto-refresh'"
          :severity="autoRefresh ? 'success' : 'secondary'"
          text
          size="small"
          @click="toggleAutoRefresh"
        />
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

    <!-- Filter bar -->
    <div class="filter-bar">
      <Select
        v-model="filter.level"
        :options="[{ label: 'All levels', value: null }, ...availableLevels.map(l => ({ label: l, value: l }))]"
        option-label="label"
        option-value="value"
        placeholder="Outcome"
        size="small"
        class="filter-level"
        @change="applyFilter"
      />
      <DatePicker
        v-model="filter.from"
        placeholder="From date"
        show-time
        hour-format="24"
        size="small"
        class="filter-date"
        @date-select="applyFilter"
      />
      <DatePicker
        v-model="filter.to"
        placeholder="To date"
        show-time
        hour-format="24"
        size="small"
        class="filter-date"
        @date-select="applyFilter"
      />
      <InputText
        v-model="filter.search"
        placeholder="Search events, entities, users…"
        size="small"
        class="filter-search"
        @keyup.enter="applyFilter"
      />
      <Button label="Search" size="small" @click="applyFilter" />
      <span class="text-color-secondary text-sm ml-auto">{{ totalCount.toLocaleString() }} entries</span>
    </div>

    <!-- Loading -->
    <div v-if="isLoading && logs.length === 0" class="flex justify-content-center align-items-center py-5">
      <ProgressSpinner style="width: 40px; height: 40px" />
    </div>

    <!-- Empty -->
    <div v-else-if="logs.length === 0" class="empty-state">
      <i class="pi pi-list text-4xl text-color-secondary mb-2"></i>
      <p class="text-color-secondary">No audit events found.</p>
    </div>

    <!-- Log table -->
    <div v-else class="logs-table-wrapper">
      <table class="logs-table">
        <thead>
          <tr>
            <th class="col-level">Outcome</th>
            <th class="col-time">Timestamp</th>
            <th class="col-type">Event</th>
            <th class="col-user">User</th>
            <th class="col-message">Message</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="log in logs" :key="log.id">
            <tr
              class="log-row"
              :class="{ 'log-error': log.outcome === 'Failed', expanded: expandedLogId === log.id }"
              @click="toggleLog(log.id)"
            >
              <td class="col-level">
                <Tag
                  :value="getOutcomeLabel(log.outcome)"
                  :severity="getOutcomeSeverity(log.outcome)"
                  style="font-size: 0.65rem"
                />
              </td>
              <td class="col-time text-sm text-color-secondary">{{ formatDate(log.createdAtUtc) }}</td>
              <td class="col-type text-sm text-color-secondary">{{ log.eventType }}</td>
              <td class="col-user text-sm text-color-secondary">{{ log.userName || '—' }}</td>
              <td class="col-message text-sm">
                {{ log.message?.slice(0, 150) }}{{ (log.message?.length ?? 0) > 150 ? '…' : '' }}
              </td>
            </tr>
            <tr v-if="expandedLogId === log.id" class="log-detail-row">
              <td colspan="5">
                <div class="log-detail">
                  <div class="detail-grid">
                    <div><span class="detail-label-inline">Action:</span> {{ log.action }}</div>
                    <div><span class="detail-label-inline">Entity:</span> {{ log.entityType }}<span v-if="log.entityId"> · {{ log.entityId }}</span></div>
                    <div><span class="detail-label-inline">Actor:</span> {{ log.userId || '—' }}</div>
                    <div><span class="detail-label-inline">Target:</span> {{ log.targetUserId || '—' }}</div>
                    <div><span class="detail-label-inline">IP:</span> {{ log.ipAddress || '—' }}</div>
                  </div>
                  <div v-if="log.message" class="detail-section">
                    <div class="detail-label">Message</div>
                    <pre class="detail-code">{{ log.message }}</pre>
                  </div>
                  <div v-if="log.metadataJson" class="detail-section">
                    <div class="detail-label">Metadata</div>
                    <pre class="detail-code">{{ tryFormatJson(log.metadataJson) }}</pre>
                  </div>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>

    <!-- Pagination -->
    <div v-if="totalPages > 1" class="pagination">
      <Button icon="pi pi-chevron-left" text size="small" :disabled="filter.page <= 1" @click="prevPage" />
      <span class="text-sm text-color-secondary">Page {{ filter.page }} of {{ totalPages }}</span>
      <Button icon="pi pi-chevron-right" text size="small" :disabled="filter.page >= totalPages" @click="nextPage" />
    </div>
  </div>
</template>

<style scoped>
.admin-logs-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.logs-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1rem;
}

.logs-title {
  font-size: 1.25rem;
  font-weight: 600;
  margin: 0;
}

.logs-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.filter-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-bottom: 1rem;
}

.filter-level {
  width: 140px;
}

.filter-date {
  width: 170px;
}

.filter-search {
  flex: 1;
  min-width: 200px;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 3rem;
}

.logs-table-wrapper {
  flex: 1;
  overflow-y: auto;
  border: 1px solid var(--p-surface-200);
  border-radius: 6px;
}

.logs-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
}

.logs-table thead tr {
  background: var(--p-surface-100);
  position: sticky;
  top: 0;
  z-index: 1;
}

.logs-table th {
  padding: 0.5rem 0.75rem;
  text-align: left;
  font-weight: 600;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  border-bottom: 1px solid var(--p-surface-200);
}

.log-row {
  cursor: pointer;
  border-bottom: 1px solid var(--p-surface-100);
}

.log-row:hover {
  background: var(--p-surface-50);
}

.log-row.log-error {
  border-left: 3px solid var(--p-red-400);
}

.log-detail-row {
  background: var(--p-surface-50);
}

.log-row td,
.log-detail-row td {
  padding: 0.35rem 0.75rem;
  vertical-align: top;
}

.col-level { width: 70px; }
.col-time { width: 160px; white-space: nowrap; }
.col-type { width: 220px; }
.col-user { width: 120px; }
.col-message { min-width: 0; }

.log-detail {
  padding: 0.5rem 0;
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 0.5rem 1rem;
  margin-bottom: 0.75rem;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}

.detail-label-inline {
  font-weight: 600;
  color: var(--p-text-color);
}

.detail-section {
  margin-bottom: 0.5rem;
}

.detail-label {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--p-text-muted-color);
  margin-bottom: 0.2rem;
}

.error-label {
  color: var(--p-red-400);
}

.detail-code {
  font-size: 0.75rem;
  font-family: monospace;
  white-space: pre-wrap;
  word-break: break-all;
  background: var(--p-surface-100);
  border-radius: 4px;
  padding: 0.4rem 0.6rem;
  max-height: 250px;
  overflow-y: auto;
  margin: 0;
}

.error-code {
  color: var(--p-red-400);
}

.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 0.5rem;
  border-top: 1px solid var(--p-surface-200);
}
</style>
