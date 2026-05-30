<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'
import ProgressSpinner from 'primevue/progressspinner'
import { adminDashboardService } from '../services/adminDashboardService'
import type { AdminDashboardSummary } from '../types/admin.types'

const router = useRouter()

const isLoading = ref(false)
const error = ref('')
const summary = ref<AdminDashboardSummary | null>(null)

const quickLinks = [
  { label: 'Users', icon: 'pi pi-users', to: '/admin/users' },
  { label: 'Marketplace Apps', icon: 'pi pi-shop', to: '/admin/marketplace-apps' },
  { label: 'App Activity', icon: 'pi pi-chart-line', to: '/admin/app-activity' },
  { label: 'Server Logs', icon: 'pi pi-list', to: '/admin/logs' },
  { label: 'Hosted Runners', icon: 'pi pi-server', to: '/admin/hosted-runners' },
  { label: 'Subscription Plans', icon: 'pi pi-credit-card', to: '/admin/subscription-plans' },
]

const overviewCards = computed(() => {
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
    },
    {
      label: 'New registrations',
      value: current.newRegistrations,
      helper: 'This month',
      icon: 'pi pi-user-plus',
      to: '/admin/users',
    },
    {
      label: 'Published apps',
      value: current.publishedApps,
      helper: 'Currently published',
      icon: 'pi pi-shop',
      to: '/admin/marketplace-apps',
    },
    {
      label: 'Active runners',
      value: current.activeRunners,
      helper: 'Running hosted runners',
      icon: 'pi pi-server',
      to: '/admin/hosted-runners',
    },
    {
      label: 'Executions today',
      value: current.executionsToday,
      helper: 'Started since midnight UTC',
      icon: 'pi pi-play-circle',
    },
    {
      label: 'Failed executions',
      value: current.failedExecutions,
      helper: 'Errors since midnight UTC',
      icon: 'pi pi-exclamation-triangle',
      tone: 'danger',
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

function navigate(to?: string) {
  if (to) {
    router.push(to)
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
          <Card class="h-full overview-card" :class="{ 'overview-card--clickable': card.to }">
            <template #content>
              <button
                class="overview-card__button"
                type="button"
                :disabled="!card.to"
                @click="navigate(card.to)"
              >
                <div class="overview-card__content">
                  <div class="overview-card__icon" :class="{ 'overview-card__icon--danger': card.tone === 'danger' }">
                    <i :class="card.icon"></i>
                  </div>
                  <div class="overview-card__text">
                    <div class="text-color-secondary text-sm">{{ card.label }}</div>
                    <div class="overview-card__value">{{ formatMetric(card.value) }}</div>
                    <div class="text-color-secondary text-sm">{{ card.helper }}</div>
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
  height: 100%;
}

.overview-card :deep(.p-card-body),
.overview-card :deep(.p-card-content) {
  height: 100%;
}

.overview-card__button {
  background: transparent;
  border: 0;
  color: inherit;
  cursor: default;
  padding: 0;
  text-align: left;
  width: 100%;
}

.overview-card--clickable .overview-card__button {
  cursor: pointer;
}

.overview-card__content {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.overview-card__icon {
  align-items: center;
  color: var(--p-primary-color);
  display: inline-flex;
  font-size: 1.5rem;
  justify-content: center;
  min-width: 2rem;
}

.overview-card__icon--danger {
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

.quick-links {
  display: grid;
  gap: 0.25rem;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
}
</style>
