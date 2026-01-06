<script setup lang="ts">
import { ref } from 'vue'

const runners = ref([
  { 
    name: 'Production-1', 
    status: 'online',
    location: 'AWS us-east-1',
    lastSeen: '1 minute ago',
    version: '1.2.0',
    activeJobs: 2
  },
  { 
    name: 'Production-2', 
    status: 'online',
    location: 'AWS us-west-2',
    lastSeen: '2 minutes ago',
    version: '1.2.0',
    activeJobs: 1
  },
  { 
    name: 'Dev-Runner', 
    status: 'offline',
    location: 'Local machine',
    lastSeen: '2 hours ago',
    version: '1.1.5',
    activeJobs: 0
  }
])
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h4 font-weight-bold">Runners</h1>
        <p class="text-body-2 text-medium-emphasis">Manage execution runners</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus">
        Add Runner
      </v-btn>
    </div>

    <!-- Runners List -->
    <v-row>
      <v-col v-for="runner in runners" :key="runner.name" cols="12" md="6" lg="4">
        <v-card>
          <v-card-title class="d-flex align-center justify-space-between">
            <span>{{ runner.name }}</span>
            <v-chip :color="runner.status === 'online' ? 'success' : 'error'" size="small">
              {{ runner.status }}
            </v-chip>
          </v-card-title>
          <v-divider />
          <v-card-text>
            <v-list density="compact">
              <v-list-item>
                <template #prepend>
                  <v-icon size="small">mdi-map-marker</v-icon>
                </template>
                <v-list-item-title>{{ runner.location }}</v-list-item-title>
              </v-list-item>
              <v-list-item>
                <template #prepend>
                  <v-icon size="small">mdi-clock</v-icon>
                </template>
                <v-list-item-title>Last seen: {{ runner.lastSeen }}</v-list-item-title>
              </v-list-item>
              <v-list-item>
                <template #prepend>
                  <v-icon size="small">mdi-package</v-icon>
                </template>
                <v-list-item-title>Version: {{ runner.version }}</v-list-item-title>
              </v-list-item>
              <v-list-item>
                <template #prepend>
                  <v-icon size="small">mdi-briefcase</v-icon>
                </template>
                <v-list-item-title>Active jobs: {{ runner.activeJobs }}</v-list-item-title>
              </v-list-item>
            </v-list>
          </v-card-text>
          <v-card-actions>
            <v-btn variant="text" size="small">View Logs</v-btn>
            <v-btn variant="text" size="small">Settings</v-btn>
            <v-spacer />
            <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Runner Setup Instructions -->
    <v-card class="mt-6">
      <v-card-title>Add New Runner</v-card-title>
      <v-divider />
      <v-card-text>
        <p class="text-body-2 mb-4">
          Download and configure a runner to execute your flows
        </p>
        <v-tabs>
          <v-tab value="windows">Windows</v-tab>
          <v-tab value="linux">Linux</v-tab>
          <v-tab value="macos">macOS</v-tab>
        </v-tabs>
        <v-window>
          <v-window-item value="windows">
            <v-sheet class="pa-4 mt-4" color="surface-variant">
              <pre class="text-body-2"><code># Download and install
./install-runner.ps1 -ApiKey "your-api-key"</code></pre>
            </v-sheet>
          </v-window-item>
        </v-window>
      </v-card-text>
    </v-card>
  </div>
</template>
