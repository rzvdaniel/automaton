<script setup lang="ts">
import { ref } from 'vue'

// Mock data for dashboard - will be replaced with API calls
const stats = ref([
  { title: 'Active Flows', value: 12, icon: 'mdi-flash', color: 'primary', change: '+2 new' },
  { title: 'Executions', value: '1,234', icon: 'mdi-play-circle', color: 'success', change: 'Last 24h' },
  { title: 'Success Rate', value: '94.2%', icon: 'mdi-check-circle', color: 'info', change: '↑ 1.2%' },
  { title: 'Runners', value: '3 / 5', icon: 'mdi-desktop-classic', color: 'warning', change: 'Online' },
])

const recentExecutions = ref([
  { id: 1, flow: 'Order Processing', status: 'success', time: '2m ago' },
  { id: 2, flow: 'Email Campaign', status: 'success', time: '5m ago' },
  { id: 3, flow: 'Data Sync', status: 'error', time: '12m ago' },
  { id: 4, flow: 'Report Gen', status: 'success', time: '15m ago' },
  { id: 5, flow: 'Backup Job', status: 'running', time: 'Running' },
])

function getStatusColor(status: string) {
  switch (status) {
    case 'success': return 'success'
    case 'error': return 'error'
    case 'running': return 'info'
    default: return 'grey'
  }
}

function getStatusIcon(status: string) {
  switch (status) {
    case 'success': return 'mdi-check-circle'
    case 'error': return 'mdi-alert-circle'
    case 'running': return 'mdi-loading'
    default: return 'mdi-circle'
  }
}
</script>

<template>
  <div>
    <!-- Header with Create Flow button -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h5 font-weight-bold">Dashboard</h1>
        <p class="text-body-2 text-medium-emphasis">Welcome back to Automaton Studio</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus" to="/flows/generate">
        Create Flow
      </v-btn>
    </div>

    <!-- Stats Cards -->
    <v-row class="mb-6">
      <v-col v-for="stat in stats" :key="stat.title" cols="12" sm="6" lg="3">
        <v-card>
          <v-card-text>
            <div class="d-flex align-center justify-space-between">
              <div>
                <div class="text-overline text-medium-emphasis mb-1">{{ stat.title }}</div>
                <div class="text-h4 font-weight-bold">{{ stat.value }}</div>
                <div class="text-caption text-medium-emphasis mt-1">{{ stat.change }}</div>
              </div>
              <v-avatar :color="stat.color" size="56" variant="tonal">
                <v-icon :icon="stat.icon" size="28" />
              </v-avatar>
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-row>
      <!-- Recent Executions -->
      <v-col cols="12" lg="8">
        <v-card>
          <v-card-title class="d-flex align-center">
            <v-icon icon="mdi-history" class="mr-2" />
            Recent Executions
            <v-spacer />
            <v-btn variant="text" size="small" color="primary">
              View All
            </v-btn>
          </v-card-title>
          <v-divider />
          <v-list lines="two">
            <v-list-item v-for="execution in recentExecutions" :key="execution.id">
              <template #prepend>
                <v-icon
                  :icon="getStatusIcon(execution.status)"
                  :color="getStatusColor(execution.status)"
                  :class="{ 'mdi-spin': execution.status === 'running' }"
                />
              </template>
              <v-list-item-title>{{ execution.flow }}</v-list-item-title>
              <v-list-item-subtitle>{{ execution.time }}</v-list-item-subtitle>
              <template #append>
                <v-chip :color="getStatusColor(execution.status)" size="small" variant="tonal">
                  {{ execution.status }}
                </v-chip>
              </template>
            </v-list-item>
          </v-list>
        </v-card>
      </v-col>

      <!-- Quick Actions -->
      <v-col cols="12" lg="4">
        <v-card>
          <v-card-title class="d-flex align-center">
            <v-icon icon="mdi-lightning-bolt" class="mr-2" />
            Quick Actions
          </v-card-title>
          <v-divider />
          <v-card-text>
            <v-btn
              block
              color="secondary"
              class="mb-3"
              prepend-icon="mdi-robot"
              to="/flows/generate"
            >
              Generate Flow
            </v-btn>
            <v-btn block variant="tonal" class="mb-3" prepend-icon="mdi-earth" to="/apps">
              Create App
            </v-btn>
            <v-btn block variant="outlined" prepend-icon="mdi-puzzle" to="/steps">
              New Step
            </v-btn>
          </v-card-text>
        </v-card>

        <!-- Recent Flows -->
        <v-card class="mt-4">
          <v-card-title class="d-flex align-center">
            <v-icon icon="mdi-flash" class="mr-2" />
            Recent Flows
          </v-card-title>
          <v-divider />
          <v-list density="compact">
            <v-list-item title="Order Processing" subtitle="5 steps" />
            <v-list-item title="Customer Onboarding" subtitle="3 steps" />
            <v-list-item title="Daily Reports" subtitle="4 steps" />
          </v-list>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
