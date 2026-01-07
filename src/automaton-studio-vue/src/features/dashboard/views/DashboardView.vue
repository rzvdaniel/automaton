<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'

// Mock data for recent executions
const recentExecutions = ref([
  { id: 1, name: 'Order Processing', status: 'success', time: '2 min ago' },
  { id: 2, name: 'Email Campaign', status: 'success', time: '5 min ago' },
  { id: 3, name: 'Data Sync', status: 'error', time: '12 min ago' },
  { id: 4, name: 'Report Gen', status: 'success', time: '15 min ago' },
  { id: 5, name: 'Backup Job', status: 'running', time: 'Running' },
])

const getStatusSeverity = (status: string) => {
  switch (status) {
    case 'success':
      return 'success'
    case 'error':
      return 'danger'
    case 'running':
      return 'info'
    default:
      return 'secondary'
  }
}

const recentFlows = ref([
  { name: 'Order Processing', lastRun: '2 min ago' },
  { name: 'Customer Onboarding', lastRun: '1 hour ago' },
  { name: 'Daily Reports', lastRun: '3 hours ago' },
])
</script>

<template>
  <div class="dashboard-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Dashboard</h1>
        <p class="text-color-secondary mt-1 mb-0">Welcome back! Here's what's happening.</p>
      </div>
      <Button label="Create Flow" icon="pi pi-plus" />
    </div>

    <!-- Stats Cards -->
    <div class="grid mb-5">
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="flex align-items-center justify-content-center border-round"
                style="
                  width: 48px;
                  height: 48px;
                  background-color: var(--p-primary-100);
                  color: var(--p-primary-600);
                "
              >
                <i class="pi pi-bolt text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Active Flows</div>
                <div class="text-2xl font-bold">12</div>
                <div class="text-color-secondary text-xs">+2 new this week</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="flex align-items-center justify-content-center border-round"
                style="
                  width: 48px;
                  height: 48px;
                  background-color: var(--p-green-100);
                  color: var(--p-green-600);
                "
              >
                <i class="pi pi-play text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Executions</div>
                <div class="text-2xl font-bold">1,234</div>
                <div class="text-color-secondary text-xs">Last 24 hours</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="flex align-items-center justify-content-center border-round"
                style="
                  width: 48px;
                  height: 48px;
                  background-color: var(--p-teal-100);
                  color: var(--p-teal-600);
                "
              >
                <i class="pi pi-check-circle text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Success Rate</div>
                <div class="text-2xl font-bold">94.2%</div>
                <div class="text-color-secondary text-xs">+1.2% from last week</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="flex align-items-center justify-content-center border-round"
                style="
                  width: 48px;
                  height: 48px;
                  background-color: var(--p-blue-100);
                  color: var(--p-blue-600);
                "
              >
                <i class="pi pi-desktop text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Runners</div>
                <div class="text-2xl font-bold">3 / 5</div>
                <div class="text-color-secondary text-xs">Online</div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <!-- Main Content Grid -->
    <div class="grid">
      <!-- Recent Executions -->
      <div class="col-12 lg:col-8">
        <Card>
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>Recent Executions</span>
              <Button label="View All" link size="small" />
            </div>
          </template>
          <template #content>
            <DataTable :value="recentExecutions" :rows="5" class="p-datatable-sm">
              <Column field="name" header="Flow Name"></Column>
              <Column field="status" header="Status">
                <template #body="{ data }">
                  <Tag :value="data.status" :severity="getStatusSeverity(data.status)" />
                </template>
              </Column>
              <Column field="time" header="Time"></Column>
            </DataTable>
          </template>
        </Card>
      </div>

      <!-- Quick Actions -->
      <div class="col-12 lg:col-4">
        <Card class="mb-4">
          <template #title>Quick Actions</template>
          <template #content>
            <div class="flex flex-column gap-3">
              <Button
                label="Generate Flow with AI"
                icon="pi pi-sparkles"
                class="w-full"
                @click="$router.push('/flows/generate')"
              />
              <Button
                label="Create App"
                icon="pi pi-globe"
                severity="secondary"
                outlined
                class="w-full"
                @click="$router.push('/apps')"
              />
              <Button
                label="New Step"
                icon="pi pi-puzzle-piece"
                severity="secondary"
                outlined
                class="w-full"
                @click="$router.push('/steps')"
              />
            </div>
          </template>
        </Card>

        <Card>
          <template #title>Recent Flows</template>
          <template #content>
            <div class="flex flex-column gap-3">
              <div
                v-for="flow in recentFlows"
                :key="flow.name"
                class="flex align-items-center justify-content-between p-2 border-round hover:surface-hover cursor-pointer"
              >
                <div class="flex align-items-center gap-2">
                  <i class="pi pi-bolt text-color-secondary"></i>
                  <span>{{ flow.name }}</span>
                </div>
                <span class="text-color-secondary text-sm">{{ flow.lastRun }}</span>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.dashboard-view {
  max-width: 1400px;
  margin: 0 auto;
}
</style>
