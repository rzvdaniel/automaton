<script setup lang="ts">
/**
 * AdminAppActivityView.vue
 * Admin dashboard showing AI chat activity aggregated per app across all users.
 * Sortable table with color-coded high-usage rows and CSV export.
 */
import { ref, computed, onMounted } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import { adminAppActivityService } from '@/shared/services/api/admin-app-activity.service'
import type { AppActivityItem } from '@/shared/types/ai.types'

const isLoading = ref(false)
const items = ref<AppActivityItem[]>([])
const search = ref('')

onMounted(loadData)

async function loadData() {
  isLoading.value = true
  try {
    items.value = await adminAppActivityService.getAppActivity()
  } catch (err) {
    console.error('Failed to load app activity', err)
  } finally {
    isLoading.value = false
  }
}

const filtered = computed(() => {
  if (!search.value.trim()) return items.value
  const q = search.value.toLowerCase()
  return items.value.filter(i => i.appName.toLowerCase().includes(q))
})

// Top 20% threshold for high-usage highlighting
const highUsageThreshold = computed(() => {
  if (items.value.length < 5) return Infinity
  const sorted = [...items.value].sort((a, b) => b.totalTokens - a.totalTokens)
  const topN = Math.max(1, Math.ceil(sorted.length * 0.2))
  return sorted[topN - 1]?.totalTokens ?? Infinity
})

function isHighUsage(item: AppActivityItem): boolean {
  return item.totalTokens >= highUsageThreshold.value && item.totalTokens > 0
}

function formatTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`
  return String(n)
}

function formatDate(iso?: string): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

function exportCsv() {
  const headers = ['App Name', 'Sessions', 'Prompts', 'API Calls', 'Total Tokens', 'Input Tokens', 'Output Tokens', 'Last Activity']
  const rows = filtered.value.map(i => [
    `"${i.appName.replace(/"/g, '""')}"`,
    i.sessionCount,
    i.totalPromptsSubmitted,
    i.totalApiCalls,
    i.totalTokens,
    i.inputTokens,
    i.outputTokens,
    formatDate(i.lastActivityAt),
  ])
  const csv = [headers.join(','), ...rows.map(r => r.join(','))].join('\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `app-activity-${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
}
</script>

<template>
  <div class="admin-app-activity">
    <!-- Header -->
    <div class="page-header">
      <div>
        <h2 class="page-title"><i class="pi pi-chart-line mr-2"></i>App AI Activity</h2>
        <p class="page-subtitle text-color-secondary">AI chat session usage across all apps and users</p>
      </div>
      <div class="header-actions">
        <InputText
          v-model="search"
          placeholder="Search apps..."
          class="search-input"
        />
        <Button
          icon="pi pi-download"
          label="Export CSV"
          outlined
          size="small"
          :disabled="filtered.length === 0"
          @click="exportCsv"
        />
        <Button
          icon="pi pi-refresh"
          outlined
          size="small"
          :loading="isLoading"
          @click="loadData"
        />
      </div>
    </div>

    <!-- Loading -->
    <div v-if="isLoading" class="loading-state">
      <ProgressSpinner style="width: 40px; height: 40px" />
    </div>

    <!-- Table -->
    <DataTable
      v-else
      :value="filtered"
      :sortField="'totalTokens'"
      :sortOrder="-1"
      removableSort
      striped-rows
      :row-class="(row: AppActivityItem) => ({ 'high-usage-row': isHighUsage(row) })"
      class="activity-table"
    >
      <template #empty>
        <div class="empty-state">
          <i class="pi pi-chart-bar" style="font-size: 2rem; color: var(--p-text-muted-color)"></i>
          <p class="mt-2 text-color-secondary">No app activity data found.</p>
        </div>
      </template>

      <Column field="appName" header="App" sortable style="min-width: 200px">
        <template #body="{ data }: { data: AppActivityItem }">
          <div class="app-name-cell">
            <span class="app-name">{{ data.appName }}</span>
            <Tag v-if="isHighUsage(data)" value="High Usage" severity="warn" class="ml-2" />
          </div>
        </template>
      </Column>

      <Column field="sessionCount" header="Sessions" sortable style="width: 100px; text-align: right">
        <template #body="{ data }: { data: AppActivityItem }">
          <span class="num-cell">{{ data.sessionCount }}</span>
        </template>
      </Column>

      <Column field="totalPromptsSubmitted" header="Prompts" sortable style="width: 100px; text-align: right">
        <template #body="{ data }: { data: AppActivityItem }">
          <span class="num-cell">{{ data.totalPromptsSubmitted }}</span>
        </template>
      </Column>

      <Column field="totalApiCalls" header="API Calls" sortable style="width: 110px; text-align: right">
        <template #body="{ data }: { data: AppActivityItem }">
          <span class="num-cell">{{ data.totalApiCalls }}</span>
        </template>
      </Column>

      <Column field="totalTokens" header="Total Tokens" sortable style="width: 130px; text-align: right">
        <template #body="{ data }: { data: AppActivityItem }">
          <span class="num-cell num-cell--primary">{{ formatTokens(data.totalTokens) }}</span>
        </template>
      </Column>

      <Column header="In / Out" style="width: 140px; text-align: right">
        <template #body="{ data }: { data: AppActivityItem }">
          <span class="token-split">
            <span class="token-in">{{ formatTokens(data.inputTokens) }}</span>
            <span class="token-sep"> / </span>
            <span class="token-out">{{ formatTokens(data.outputTokens) }}</span>
          </span>
        </template>
      </Column>

      <Column field="lastActivityAt" header="Last Activity" sortable style="width: 160px">
        <template #body="{ data }: { data: AppActivityItem }">
          <span class="text-color-secondary text-sm">{{ formatDate(data.lastActivityAt) }}</span>
        </template>
      </Column>
    </DataTable>
  </div>
</template>

<style scoped>
.admin-app-activity {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.page-title {
  font-size: 1.25rem;
  font-weight: 700;
  margin: 0 0 0.25rem;
  display: flex;
  align-items: center;
}

.page-subtitle {
  font-size: 0.875rem;
  margin: 0;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.search-input {
  width: 200px;
}

.loading-state {
  display: flex;
  justify-content: center;
  padding: 4rem;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 3rem;
  text-align: center;
}

.app-name-cell {
  display: flex;
  align-items: center;
}

.app-name {
  font-weight: 500;
}

.num-cell {
  font-variant-numeric: tabular-nums;
}

.num-cell--primary {
  font-weight: 600;
  color: var(--p-primary-color);
}

.token-split {
  font-variant-numeric: tabular-nums;
  font-size: 0.875rem;
}

.token-in {
  color: rgb(99, 102, 241);
}

.token-out {
  color: rgb(34, 197, 94);
}

.token-sep {
  color: var(--p-text-muted-color);
}

/* High-usage row highlight */
:deep(.high-usage-row) td {
  background-color: rgba(234, 179, 8, 0.06) !important;
}

:deep(.high-usage-row:hover) td {
  background-color: rgba(234, 179, 8, 0.12) !important;
}
</style>
