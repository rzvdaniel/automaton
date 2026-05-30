<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'
import ProgressSpinner from 'primevue/progressspinner'
import { adminDashboardService } from '../services/adminDashboardService'
import type { AdminDashboardSummary } from '../types/admin.types'

const router = useRouter()

const isLoading = ref(false)
const error = ref('')
const summary = ref<AdminDashboardSummary | null>(null)

type OverviewCardTone = 'primary' | 'success' | 'warning' | 'danger'

interface DashboardLink {
  label: string
  icon: string
  to: RouteLocationRaw
}

interface OverviewCard extends DashboardLink {
  value: number
  helper: string
  actionLabel: string
  tone?: OverviewCardTone
}

const quickLinks = [
  { label: 'Users', icon: 'pi pi-users', to: '/admin/users' },
  { label: 'Marketplace Apps', icon: 'pi pi-shop', to: '/admin/marketplace-apps' },
  { label: 'App Activity', icon: 'pi pi-chart-line', to: '/admin/app-activity' },
  { label: 'Server Logs', icon: 'pi pi-list', to: '/admin/logs' },
  { label: 'Hosted Runners', icon: 'pi pi-server', to: '/admin/hosted-runners' },
  { label: 'Subscription Plans', icon: 'pi pi-credit-card', to: '/admin/subscription-plans' },
] satisfies DashboardLink[]

function getFailedExecutionsTone(failedExecutions: number): OverviewCardTone {
  if (failedExecutions === 0) {
    return 'success'
  }

  if (failedExecutions >= 10) {
    return 'danger'
  }

  return 'warning'
}

const overviewCards = computed<OverviewCard[]>(() => {
  const current = summary.value
  if (!current) {
    return []
  }

  return [
    {
      label: 'Total users',
      value: current.totalUsers,
      helper: 'All registered accounts',
      icon: 'pi pi-users',
      to: '/admin/users',
      actionLabel: 'Open users',
    },
    {
      label: 'Registrations this month',
      value: current.newRegistrations,
      helper: 'Created since the start of the month',
      icon: 'pi pi-user-plus',
      to: '/admin/users',
      actionLabel: 'Review users',
    },
    {
      label: 'Published apps',
      value: current.publishedApps,
      helper: 'Currently published',
      icon: 'pi pi-shop',
      to: '/admin/marketplace-apps',
      actionLabel: 'Open marketplace apps',
    },
    {
      label: 'Active runners',
      value: current.activeRunners,
      helper: 'Running hosted runners',
      icon: 'pi pi-server',
      to: '/admin/hosted-runners',
      actionLabel: 'Open hosted runners',
      tone: current.activeRunners === 0 ? 'danger' : 'success',
    },
    {
      label: 'Executions today',
      value: current.executionsToday,
      helper: 'Started since midnight UTC',
      icon: 'pi pi-play-circle',
      to: '/admin/app-activity',
      actionLabel: 'Open app activity',
    },
    {
      label: 'Failed executions',
      value: current.failedExecutions,
      helper: 'Errors since midnight UTC',
      icon: 'pi pi-exclamation-triangle',
      to: {
        path: '/admin/logs',
        query: {
          level: 'Error',
        },
      },
      actionLabel: 'Open error logs',
      tone: getFailedExecutionsTone(current.failedExecutions),
    },
  ]
})

async function loadSummary() {
  isLoading.value = true
  error.value = ''

  try {
    summary.value = await adminDashboardService.getSummary()
  } catch {
    error.value = 'Failed to load the administration overview.'
  } finally {
    isLoading.value = false
  }
}

function formatMetric(value: number) {
  return value.toLocaleString()
}

function navigate(to: RouteLocationRaw) {
  void router.push(to)
}

function getToneClass(tone?: OverviewCardTone) {
  switch (tone) {
    case 'success':
      return 'overview-card--tone-success'
    case 'warning':
      return 'overview-card--tone-warning'
    case 'danger':
      return 'overview-card--tone-danger'
    default:
      return 'overview-card--tone-primary'
  }
}

function getIconToneClass(tone?: OverviewCardTone) {
  switch (tone) {
    case 'success':
      return 'overview-card__icon--success'
    case 'warning':
      return 'overview-card__icon--warning'
    case 'danger':
      return 'overview-card__icon--danger'
    default:
      return 'overview-card__icon--primary'
  }
}

function getValueToneClass(tone?: OverviewCardTone) {
  switch (tone) {
    case 'success':
      return 'overview-card__value--success'
    case 'warning':
      return 'overview-card__value--warning'
    case 'danger':
      return 'overview-card__value--danger'
    default:
      return 'overview-card__value--primary'
  }
}

function getActionToneClass(tone?: OverviewCardTone) {
  switch (tone) {
    case 'success':
      return 'overview-card__action--success'
    case 'warning':
      return 'overview-card__action--warning'
    case 'danger':
      return 'overview-card__action--danger'
    default:
      return 'overview-card__action--primary'
  }
}

function navigateToSection(to?: RouteLocationRaw) {
  if (to) {
    navigate(to)
  }
}

onMounted(() => {
  void loadSummary()
})
</script>

<template>
  <div class="admin-dashboard">
    <div class="page-header">
      <div>
        <h2 class="page-title">Dashboard</h2>
        <p class="page-subtitle text-color-secondary">
          A single operational overview for the administration area.
        </p>
      </div>

      <Button
        icon="pi pi-refresh"
        label="Refresh"
        outlined
        :loading="isLoading"
        @click="loadSummary"
      />
    </div>

    <div v-if="isLoading && !summary" class="loading-state">
      <ProgressSpinner style="width: 40px; height: 40px" />
    </div>

    <template v-else>
      <div v-if="error" class="error-banner">
        <span>{{ error }}</span>
      </div>

      <div class="grid">
        <div v-for="card in overviewCards" :key="card.label" class="col-12 md:col-6 xl:col-4">
          <Card
            class="h-full overview-card overview-card--clickable"
            :class="getToneClass(card.tone)"
          >
            <template #content>
              <button
                class="overview-card__button"
                type="button"
                @click="navigateToSection(card.to)"
              >
                <div class="overview-card__content">
                  <div class="overview-card__main">
                    <div class="overview-card__icon" :class="getIconToneClass(card.tone)">
                      <i :class="card.icon"></i>
                    </div>
                    <div class="overview-card__text">
                      <div class="text-color-secondary text-sm">{{ card.label }}</div>
                      <div class="overview-card__value" :class="getValueToneClass(card.tone)">
                        {{ formatMetric(card.value) }}
                      </div>
                      <div class="text-color-secondary text-sm">{{ card.helper }}</div>
                    </div>
                  </div>
                  <div class="overview-card__action" :class="getActionToneClass(card.tone)">
                    <span>{{ card.actionLabel }}</span>
                    <i class="pi pi-arrow-right"></i>
                  </div>
                </div>
              </button>
            </template>
          </Card>
        </div>
      </div>

      <Card>
        <template #title>Quick links</template>
        <template #subtitle>Jump straight into the admin areas behind each metric.</template>
        <template #content>
          <div class="quick-links">
            <Button
              v-for="item in quickLinks"
              :key="item.to"
              :label="item.label"
              :icon="item.icon"
              text
              class="justify-content-start"
              @click="navigate(item.to)"
            />
          </div>
        </template>
      </Card>

      <div class="text-color-secondary text-sm">
        Revenue reporting can be added here when billing metrics are ready.
      </div>
    </template>
  </div>
</template>

<style scoped>
.admin-dashboard {
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
  font-size: 1.5rem;
  font-weight: 700;
  margin: 0 0 0.25rem;
}

.page-subtitle {
  margin: 0;
}

.loading-state {
  display: flex;
  justify-content: center;
  padding: 4rem 0;
}

.error-banner {
  border: 1px solid var(--p-red-200);
  border-radius: 0.75rem;
  color: var(--p-red-500);
  padding: 0.875rem 1rem;
}

.overview-card {
  border: 1px solid var(--p-surface-200);
  height: 100%;
  transition:
    border-color 0.15s ease,
    box-shadow 0.15s ease,
    transform 0.15s ease;
}

.overview-card--clickable:hover {
  box-shadow: 0 10px 24px rgba(15, 23, 42, 0.08);
  transform: translateY(-2px);
}

.overview-card--tone-primary {
  border-color: var(--p-primary-100);
}

.overview-card--tone-success {
  border-color: var(--p-green-200);
}

.overview-card--tone-warning {
  border-color: var(--p-orange-200);
}

.overview-card--tone-danger {
  border-color: var(--p-red-200);
}

.overview-card :deep(.p-card-body),
.overview-card :deep(.p-card-content) {
  height: 100%;
}

.overview-card__button {
  background: transparent;
  border: 0;
  color: inherit;
  cursor: pointer;
  padding: 0;
  text-align: left;
  width: 100%;
}

.overview-card__button:focus-visible {
  border-radius: 0.75rem;
  outline: 2px solid var(--p-primary-color);
  outline-offset: 0.25rem;
}

.overview-card__content {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.overview-card__main {
  align-items: center;
  display: flex;
  gap: 1rem;
}

.overview-card__icon {
  align-items: center;
  border-radius: 999px;
  display: inline-flex;
  font-size: 1.5rem;
  height: 3rem;
  justify-content: center;
  min-width: 3rem;
  width: 3rem;
}

.overview-card__icon--primary {
  background: var(--p-primary-50);
  color: var(--p-primary-color);
}

.overview-card__icon--success {
  background: var(--p-green-100);
  color: var(--p-green-600);
}

.overview-card__icon--warning {
  background: var(--p-orange-100);
  color: var(--p-orange-600);
}

.overview-card__icon--danger {
  background: var(--p-red-100);
  color: var(--p-red-500);
}

.overview-card__text {
  min-width: 0;
}

.overview-card__value {
  font-size: 1.875rem;
  font-weight: 700;
  line-height: 1.15;
  margin: 0.125rem 0;
}

.overview-card__value--primary {
  color: var(--p-primary-color);
}

.overview-card__value--success {
  color: var(--p-green-600);
}

.overview-card__value--warning {
  color: var(--p-orange-600);
}

.overview-card__value--danger {
  color: var(--p-red-500);
}

.overview-card__action {
  align-items: center;
  display: inline-flex;
  font-size: 0.875rem;
  font-weight: 600;
  gap: 0.5rem;
}

.overview-card__action--primary {
  color: var(--p-primary-color);
}

.overview-card__action--success {
  color: var(--p-green-600);
}

.overview-card__action--warning {
  color: var(--p-orange-600);
}

.overview-card__action--danger {
  color: var(--p-red-500);
}

.quick-links {
  display: grid;
  gap: 0.25rem;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
}
</style>
