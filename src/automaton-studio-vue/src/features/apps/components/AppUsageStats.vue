<script setup lang="ts">
/**
 * AppUsageStats.vue
 * Usage dashboard for a single app: summary cards + 3 charts.
 * Charts: tokens-per-session (bar), prompts/API calls per session (line), token split (doughnut).
 */
import { ref, computed, onMounted } from 'vue'
import { Bar, Line, Doughnut } from 'vue-chartjs'
import {
  Chart as ChartJS,
  Title,
  Tooltip,
  Legend,
  BarElement,
  LineElement,
  PointElement,
  ArcElement,
  CategoryScale,
  LinearScale,
  Filler,
  type ChartOptions,
  type ChartData,
} from 'chart.js'
import Card from 'primevue/card'
import ProgressSpinner from 'primevue/progressspinner'
import { aiChatHistoryService } from '@/shared/services/api/ai-chat-history.service'
import type { ChatSession, AppChatStats } from '@/shared/types/ai.types'

ChartJS.register(
  Title, Tooltip, Legend,
  BarElement, LineElement, PointElement, ArcElement,
  CategoryScale, LinearScale, Filler,
)

const props = defineProps<{ appId: string }>()

const isLoading = ref(false)
const sessions = ref<ChatSession[]>([])
const stats = ref<AppChatStats | null>(null)

onMounted(loadData)

async function loadData() {
  if (!props.appId) return
  isLoading.value = true
  try {
    const [sessionsResult, statsResult] = await Promise.all([
      aiChatHistoryService.getSessions('2', props.appId, 1, 50),
      aiChatHistoryService.getAppChatStats(props.appId),
    ])
    sessions.value = sessionsResult.items
    stats.value = statsResult
  } catch (err) {
    console.error('Failed to load usage stats', err)
  } finally {
    isLoading.value = false
  }
}

function formatTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`
  return String(n)
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
}

function formatPercent(value: number): string {
  return `${value.toFixed(1)}%`
}

// Ordered by creation for chronological charts
const orderedSessions = computed(() =>
  [...sessions.value].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()),
)

const sessionLabels = computed(() => orderedSessions.value.map((s, i) => `#${i + 1} ${formatDate(s.createdAt)}`))

// Chart 1: tokens per session (stacked bar — input vs output)
const tokensChartData = computed<ChartData<'bar'>>(() => ({
  labels: sessionLabels.value,
  datasets: [
    {
      label: 'Input Tokens',
      data: orderedSessions.value.map(s => s.totalInputTokens),
      backgroundColor: 'rgba(99, 102, 241, 0.75)',
      borderColor: 'rgb(99, 102, 241)',
      borderWidth: 1,
      borderRadius: 3,
    },
    {
      label: 'Output Tokens',
      data: orderedSessions.value.map(s => s.totalOutputTokens),
      backgroundColor: 'rgba(34, 197, 94, 0.75)',
      borderColor: 'rgb(34, 197, 94)',
      borderWidth: 1,
      borderRadius: 3,
    },
  ],
}))

const tokensChartOptions = computed<ChartOptions<'bar'>>(() => ({
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: { position: 'top', labels: { usePointStyle: true, padding: 16 } },
    tooltip: { mode: 'index', intersect: false },
  },
  scales: {
    x: { stacked: true, grid: { display: false } },
    y: { stacked: true, beginAtZero: true, ticks: { callback: (v) => formatTokens(Number(v)) } },
  },
}))

// Chart 2: prompts + API calls per session (line)
const activityChartData = computed<ChartData<'line'>>(() => ({
  labels: sessionLabels.value,
  datasets: [
    {
      label: 'API Calls',
      data: orderedSessions.value.map(s => s.totalApiCalls),
      borderColor: 'rgb(249, 115, 22)',
      backgroundColor: 'rgba(249, 115, 22, 0.1)',
      borderWidth: 2,
      pointRadius: 4,
      tension: 0.3,
      fill: true,
    },
    {
      label: 'Prompts',
      data: orderedSessions.value.map(s => s.totalPromptsSubmitted),
      borderColor: 'rgb(99, 102, 241)',
      backgroundColor: 'rgba(99, 102, 241, 0.1)',
      borderWidth: 2,
      pointRadius: 4,
      tension: 0.3,
      fill: true,
    },
  ],
}))

const activityChartOptions = computed<ChartOptions<'line'>>(() => ({
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: { position: 'top', labels: { usePointStyle: true, padding: 16 } },
    tooltip: { mode: 'index', intersect: false },
  },
  scales: {
    x: { grid: { display: false } },
    y: { beginAtZero: true, ticks: { stepSize: 1, precision: 0 } },
  },
}))

// Chart 3: total token split doughnut
const doughnutData = computed<ChartData<'doughnut'>>(() => ({
  labels: ['Input Tokens', 'Output Tokens'],
  datasets: [{
    data: [stats.value?.inputTokens ?? 0, stats.value?.outputTokens ?? 0],
    backgroundColor: ['rgba(99, 102, 241, 0.8)', 'rgba(34, 197, 94, 0.8)'],
    borderColor: ['rgb(99, 102, 241)', 'rgb(34, 197, 94)'],
    borderWidth: 2,
  }],
}))

const doughnutOptions = computed<ChartOptions<'doughnut'>>(() => ({
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: { position: 'bottom', labels: { usePointStyle: true, padding: 16 } },
    tooltip: {
      callbacks: {
        label: (ctx) => ` ${ctx.label}: ${formatTokens(ctx.parsed)}`,
      },
    },
  },
  cutout: '65%',
}))

const hasData = computed(() => sessions.value.length > 0)
</script>

<template>
  <div class="app-usage-stats">
    <div v-if="isLoading" class="loading-state">
      <ProgressSpinner style="width: 40px; height: 40px" />
    </div>

    <template v-else-if="hasData">
      <!-- Summary Cards -->
      <div class="stats-grid">
        <div class="stat-card">
          <div class="stat-icon">
            <i class="pi pi-comments"></i>
          </div>
          <div class="stat-body">
            <div class="stat-value">{{ stats?.totalPromptsSubmitted ?? 0 }}</div>
            <div class="stat-label">Prompts</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon">
            <i class="pi pi-sync"></i>
          </div>
          <div class="stat-body">
            <div class="stat-value">{{ stats?.totalApiCalls ?? 0 }}</div>
            <div class="stat-label">API Calls</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon">
            <i class="pi pi-bolt"></i>
          </div>
          <div class="stat-body">
            <div class="stat-value">{{ formatTokens(stats?.totalTokens ?? 0) }}</div>
            <div class="stat-label">Total Tokens</div>
          </div>
        </div>
        <div class="stat-card">
          <div class="stat-icon">
            <i class="pi pi-history"></i>
          </div>
          <div class="stat-body">
            <div class="stat-value">{{ stats?.sessionCount ?? 0 }}</div>
            <div class="stat-label">Sessions</div>
          </div>
        </div>
        <div v-if="(stats?.totalCacheReadTokens ?? 0) > 0" class="stat-card stat-card--accent">
          <div class="stat-icon">
            <i class="pi pi-database"></i>
          </div>
          <div class="stat-body">
            <div class="stat-value">{{ formatTokens(stats?.totalCacheReadTokens ?? 0) }}</div>
            <div class="stat-label">Cache Saved</div>
          </div>
        </div>
        <div v-if="(stats?.totalCacheReadTokens ?? 0) > 0" class="stat-card stat-card--accent">
          <div class="stat-icon">
            <i class="pi pi-percentage"></i>
          </div>
          <div class="stat-body">
            <div class="stat-value">{{ formatPercent(stats?.cacheHitPercent ?? 0) }}</div>
            <div class="stat-label">Cache Hit</div>
          </div>
        </div>
        <div v-if="(stats?.totalCacheReadTokens ?? 0) > 0" class="stat-card">
          <div class="stat-icon">
            <i class="pi pi-filter"></i>
          </div>
          <div class="stat-body">
            <div class="stat-value">{{ formatTokens(stats?.effectiveInputTokens ?? 0) }}</div>
            <div class="stat-label">Fresh Input</div>
          </div>
        </div>
      </div>

      <!-- Charts Row 1: Tokens per session + Token split -->
      <div class="charts-row">
        <Card class="chart-card chart-card--wide">
          <template #title>
            <span class="chart-title"><i class="pi pi-chart-bar mr-2"></i>Tokens per Session</span>
          </template>
          <template #content>
            <div class="chart-container">
              <Bar :data="tokensChartData" :options="tokensChartOptions" />
            </div>
          </template>
        </Card>

        <Card class="chart-card chart-card--narrow">
          <template #title>
            <span class="chart-title"><i class="pi pi-chart-pie mr-2"></i>Token Split</span>
          </template>
          <template #content>
            <div class="chart-container chart-container--doughnut">
              <Doughnut :data="doughnutData" :options="doughnutOptions" />
            </div>
            <div class="doughnut-center-label">
              <div class="doughnut-total">{{ formatTokens(stats?.totalTokens ?? 0) }}</div>
              <div class="doughnut-total-label">total</div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Chart Row 2: Activity per session -->
      <Card class="chart-card">
        <template #title>
          <span class="chart-title"><i class="pi pi-chart-line mr-2"></i>Activity per Session</span>
        </template>
        <template #content>
          <div class="chart-container chart-container--tall">
            <Line :data="activityChartData" :options="activityChartOptions" />
          </div>
        </template>
      </Card>
    </template>

    <div v-else class="empty-state">
      <i class="pi pi-chart-bar" style="font-size: 2.5rem; color: var(--p-text-muted-color)"></i>
      <p class="mt-3 text-color-secondary">No AI build sessions yet. Start chatting with the AI assistant to see usage stats here.</p>
    </div>
  </div>
</template>

<style scoped>
.app-usage-stats {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.loading-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 280px;
  text-align: center;
}

/* Summary cards */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 1rem;
}

@media (max-width: 900px) {
  .stats-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1rem 1.25rem;
  border-radius: 8px;
  background: var(--p-surface-card);
  border: 1px solid var(--p-surface-border);
}

.stat-card--accent {
  border-color: var(--p-green-300);
  background: color-mix(in srgb, var(--p-green-500) 8%, var(--p-surface-card));
}

.stat-icon {
  width: 2.5rem;
  height: 2.5rem;
  border-radius: 8px;
  background: var(--p-surface-ground);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.1rem;
  color: var(--p-primary-color);
  flex-shrink: 0;
}

.stat-value {
  font-size: 1.5rem;
  font-weight: 700;
  line-height: 1.2;
}

.stat-label {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

/* Charts */
.charts-row {
  display: grid;
  grid-template-columns: 1fr 280px;
  gap: 1.25rem;
}

@media (max-width: 900px) {
  .charts-row {
    grid-template-columns: 1fr;
  }
}

.chart-card :deep(.p-card-title) {
  font-size: 0.9rem;
  font-weight: 600;
  padding-bottom: 0;
}

.chart-title {
  display: flex;
  align-items: center;
}

.chart-container {
  width: 100%;
  height: 220px;
}

.chart-container--tall {
  height: 240px;
}

.chart-container--doughnut {
  height: 180px;
}

/* Doughnut center label overlay */
.chart-card--narrow {
  position: relative;
}

.doughnut-center-label {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -10%);
  text-align: center;
  pointer-events: none;
}

.doughnut-total {
  font-size: 1.2rem;
  font-weight: 700;
  line-height: 1.2;
}

.doughnut-total-label {
  font-size: 0.7rem;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
}
</style>
