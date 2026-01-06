<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'

// Mock runners data
const runners = ref([
  {
    id: 1,
    name: 'production-runner-01',
    status: 'online',
    os: 'Ubuntu 22.04',
    ip: '192.168.1.100',
    version: '2.1.0',
    lastSeen: 'Just now',
    executions: 1234,
    currentStatus: 'Idle',
  },
  {
    id: 2,
    name: 'staging-runner',
    status: 'online',
    os: 'Windows Server 2022',
    ip: '10.0.0.50',
    version: '2.1.0',
    lastSeen: '2 min ago',
    executions: 456,
    currentStatus: 'Idle',
  },
  {
    id: 3,
    name: 'dev-runner',
    status: 'offline',
    os: 'macOS 14',
    ip: '192.168.1.50',
    version: '2.0.5',
    lastSeen: '3 days ago',
    executions: 89,
    currentStatus: 'Offline',
  },
])

const getStatusColor = (status: string) => {
  return status === 'online' ? 'var(--p-green-500)' : 'var(--p-red-500)'
}

const getStatusSeverity = (status: string) => {
  return status === 'online' ? 'success' : 'danger'
}
</script>

<template>
  <div class="runners-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Runners</h1>
        <p class="text-color-secondary mt-1 mb-0">Manage remote execution agents</p>
      </div>
      <Button label="Add Runner" icon="pi pi-plus" />
    </div>

    <!-- Stats Cards -->
    <div class="grid mb-5">
      <div class="col-12 md:col-4">
        <Card>
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
                <i class="pi pi-check-circle text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Online</div>
                <div class="text-2xl font-bold">{{ runners.filter((r) => r.status === 'online').length }}</div>
              </div>
            </div>
          </template>
        </Card>
      </div>
      <div class="col-12 md:col-4">
        <Card>
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="flex align-items-center justify-content-center border-round"
                style="
                  width: 48px;
                  height: 48px;
                  background-color: var(--p-red-100);
                  color: var(--p-red-600);
                "
              >
                <i class="pi pi-times-circle text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Offline</div>
                <div class="text-2xl font-bold">{{ runners.filter((r) => r.status === 'offline').length }}</div>
              </div>
            </div>
          </template>
        </Card>
      </div>
      <div class="col-12 md:col-4">
        <Card>
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
                <i class="pi pi-bolt text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Total Executions</div>
                <div class="text-2xl font-bold">{{ runners.reduce((sum, r) => sum + r.executions, 0).toLocaleString() }}</div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <!-- Runners List -->
    <div class="flex flex-column gap-4">
      <Card v-for="runner in runners" :key="runner.id" class="runner-card">
        <template #content>
          <div class="flex align-items-center justify-content-between">
            <div class="flex align-items-center gap-4">
              <!-- Status Indicator -->
              <div
                class="status-indicator"
                :style="{ backgroundColor: getStatusColor(runner.status) }"
              ></div>

              <!-- Runner Info -->
              <div>
                <div class="flex align-items-center gap-2 mb-1">
                  <span class="font-semibold text-lg">{{ runner.name }}</span>
                  <Tag
                    :value="runner.status === 'online' ? 'Online' : 'Offline'"
                    :severity="getStatusSeverity(runner.status)"
                  />
                </div>
                <div class="text-color-secondary text-sm">
                  {{ runner.os }} • {{ runner.ip }} • v{{ runner.version }}
                </div>
              </div>
            </div>

            <!-- Runner Stats -->
            <div class="flex align-items-center gap-6">
              <div class="text-center">
                <div class="text-color-secondary text-sm">Last Seen</div>
                <div class="font-medium">{{ runner.lastSeen }}</div>
              </div>
              <div class="text-center">
                <div class="text-color-secondary text-sm">Executions</div>
                <div class="font-medium">{{ runner.executions.toLocaleString() }}</div>
              </div>
              <div class="text-center">
                <div class="text-color-secondary text-sm">Status</div>
                <div class="font-medium">{{ runner.currentStatus }}</div>
              </div>

              <!-- Actions -->
              <div class="flex gap-2">
                <Button label="View Logs" outlined size="small" />
                <Button
                  v-if="runner.status === 'online'"
                  label="Disconnect"
                  outlined
                  severity="secondary"
                  size="small"
                />
                <Button icon="pi pi-ellipsis-v" text rounded severity="secondary" />
              </div>
            </div>
          </div>
        </template>
      </Card>
    </div>

    <!-- Add Runner Info -->
    <Card class="mt-5">
      <template #title>
        <div class="flex align-items-center gap-2">
          <i class="pi pi-info-circle" style="color: var(--p-primary-500)"></i>
          <span>Adding a New Runner</span>
        </div>
      </template>
      <template #content>
        <p class="text-color-secondary mt-0 mb-3">
          To add a new runner, download and install the Automaton Runner on your target machine.
        </p>
        <div class="flex gap-3">
          <Button label="Download for Windows" icon="pi pi-microsoft" outlined />
          <Button label="Download for Linux" icon="pi pi-server" outlined />
          <Button label="Download for macOS" icon="pi pi-apple" outlined />
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.runners-view {
  max-width: 1400px;
  margin: 0 auto;
}

.runner-card {
  transition: transform 0.15s ease;
}

.runner-card:hover {
  transform: translateX(4px);
}

.status-indicator {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  flex-shrink: 0;
}
</style>
