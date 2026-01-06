<script setup lang="ts">
import { ref } from 'vue'

const runners = ref([
  { id: '1', name: 'production-runner-01', status: 'online', os: 'Ubuntu 22.04', lastSeen: 'Just now', executions: 1234 },
  { id: '2', name: 'staging-runner', status: 'online', os: 'Windows Server 2022', lastSeen: '2 min ago', executions: 456 },
  { id: '3', name: 'dev-runner', status: 'offline', os: 'macOS 14', lastSeen: '3 days ago', executions: 89 },
])

function getStatusColor(status: string) {
  return status === 'online' ? 'success' : 'grey'
}

function getOsIcon(os: string) {
  if (os.includes('Ubuntu') || os.includes('Linux')) return 'mdi-linux'
  if (os.includes('Windows')) return 'mdi-microsoft-windows'
  if (os.includes('macOS')) return 'mdi-apple'
  return 'mdi-desktop-classic'
}
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h5 font-weight-bold">Runners</h1>
        <p class="text-body-2 text-medium-emphasis">Manage your execution agents</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-download">
        Download Runner
      </v-btn>
    </div>

    <!-- Stats -->
    <v-row class="mb-4">
      <v-col cols="12" sm="4">
        <v-card>
          <v-card-text class="d-flex align-center">
            <v-avatar color="success" size="48" class="mr-4">
              <v-icon icon="mdi-check-circle" />
            </v-avatar>
            <div>
              <div class="text-h5 font-weight-bold">2</div>
              <div class="text-caption text-medium-emphasis">Online</div>
            </div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="12" sm="4">
        <v-card>
          <v-card-text class="d-flex align-center">
            <v-avatar color="grey" size="48" class="mr-4">
              <v-icon icon="mdi-power-off" />
            </v-avatar>
            <div>
              <div class="text-h5 font-weight-bold">1</div>
              <div class="text-caption text-medium-emphasis">Offline</div>
            </div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="12" sm="4">
        <v-card>
          <v-card-text class="d-flex align-center">
            <v-avatar color="primary" size="48" class="mr-4">
              <v-icon icon="mdi-play-circle" />
            </v-avatar>
            <div>
              <div class="text-h5 font-weight-bold">1,779</div>
              <div class="text-caption text-medium-emphasis">Total Executions</div>
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <!-- Runners List -->
    <v-card>
      <v-card-title>Registered Runners</v-card-title>
      <v-divider />
      <v-list lines="two">
        <v-list-item v-for="runner in runners" :key="runner.id">
          <template #prepend>
            <v-avatar :color="getStatusColor(runner.status)" size="40" variant="tonal">
              <v-icon :icon="getOsIcon(runner.os)" />
            </v-avatar>
          </template>
          <v-list-item-title class="font-weight-medium">{{ runner.name }}</v-list-item-title>
          <v-list-item-subtitle>
            {{ runner.os }} • Last seen: {{ runner.lastSeen }} • {{ runner.executions }} executions
          </v-list-item-subtitle>
          <template #append>
            <v-chip :color="getStatusColor(runner.status)" size="small" variant="tonal" class="mr-2">
              {{ runner.status }}
            </v-chip>
            <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
          </template>
        </v-list-item>
      </v-list>
    </v-card>
  </div>
</template>
