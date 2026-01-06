<script setup lang="ts">
import { ref } from 'vue'

// Mock data for stubs
const stats = ref([
  { title: 'Active Flows', value: '12', change: '↑ 2 new', icon: 'mdi-flash', color: 'primary' },
  { title: 'Executions', value: '1,234', change: 'Last 24h', icon: 'mdi-play-circle', color: 'success' },
  { title: 'Success Rate', value: '94.2%', change: '↑ 1.2%', icon: 'mdi-check-circle', color: 'success' },
  { title: 'Runners', value: '3 / 5', change: 'Online', icon: 'mdi-server', color: 'info' }
])

const recentExecutions = ref([
  { name: 'Order Processing', status: 'success', time: '2m ago', icon: 'mdi-check-circle', color: 'success' },
  { name: 'Email Campaign', status: 'success', time: '5m ago', icon: 'mdi-check-circle', color: 'success' },
  { name: 'Data Sync', status: 'error', time: '12m ago', icon: 'mdi-close-circle', color: 'error' },
  { name: 'Report Generation', status: 'success', time: '15m ago', icon: 'mdi-check-circle', color: 'success' },
  { name: 'Backup Job', status: 'running', time: 'Running', icon: 'mdi-loading', color: 'warning' }
])

const recentFlows = ref([
  'Order Processing',
  'Customer Onboarding',
  'Daily Reports'
])
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h4 font-weight-bold">Dashboard</h1>
        <p class="text-body-2 text-medium-emphasis">Welcome to Automaton Studio</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus" size="large">
        Create Flow
      </v-btn>
    </div>

    <!-- Stats Cards -->
    <v-row class="mb-6">
      <v-col v-for="stat in stats" :key="stat.title" cols="12" sm="6" md="3">
        <v-card>
          <v-card-text>
            <div class="d-flex align-center justify-space-between mb-2">
              <span class="text-body-2 text-medium-emphasis">{{ stat.title }}</span>
              <v-icon :color="stat.color">{{ stat.icon }}</v-icon>
            </div>
            <div class="text-h4 font-weight-bold mb-1">{{ stat.value }}</div>
            <div class="text-body-2 text-medium-emphasis">{{ stat.change }}</div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <!-- Main Content -->
    <v-row>
      <!-- Recent Executions -->
      <v-col cols="12" md="8">
        <v-card>
          <v-card-title class="d-flex align-center justify-space-between">
            <span>Recent Executions</span>
            <v-btn variant="text" size="small">View All →</v-btn>
          </v-card-title>
          <v-divider />
          <v-card-text>
            <v-list>
              <v-list-item
                v-for="(execution, index) in recentExecutions"
                :key="index"
                :prepend-icon="execution.icon"
              >
                <v-list-item-title>{{ execution.name }}</v-list-item-title>
                <v-list-item-subtitle>{{ execution.time }}</v-list-item-subtitle>
                <template #append>
                  <v-chip :color="execution.color" size="small">
                    {{ execution.status }}
                  </v-chip>
                </template>
              </v-list-item>
            </v-list>
          </v-card-text>
        </v-card>

        <!-- Execution Trend Chart -->
        <v-card class="mt-4">
          <v-card-title>Execution Trend (7 days)</v-card-title>
          <v-divider />
          <v-card-text>
            <v-skeleton-loader type="image" height="200" />
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Quick Actions & Recent Flows -->
      <v-col cols="12" md="4">
        <v-card>
          <v-card-title>Quick Actions</v-card-title>
          <v-divider />
          <v-card-text>
            <v-btn block color="primary" class="mb-3" prepend-icon="mdi-robot">
              🤖 Generate Flow
            </v-btn>
            <v-btn block variant="outlined" class="mb-3" prepend-icon="mdi-cellphone">
              📱 Create App
            </v-btn>
            <v-btn block variant="outlined" prepend-icon="mdi-puzzle">
              🧩 New Step
            </v-btn>
          </v-card-text>
        </v-card>

        <v-card class="mt-4">
          <v-card-title>Recent Flows</v-card-title>
          <v-divider />
          <v-card-text>
            <v-list density="compact">
              <v-list-item
                v-for="(flow, index) in recentFlows"
                :key="index"
                :title="flow"
                prepend-icon="mdi-flash"
              />
            </v-list>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
