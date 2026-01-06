<script setup lang="ts">
import { ref } from 'vue'

const search = ref('')
const flows = ref([
  { id: '1', name: 'Order Processing', description: 'Process incoming orders', status: 'active', steps: 5, lastRun: '2m ago' },
  { id: '2', name: 'Email Campaign', description: 'Send automated emails', status: 'active', steps: 3, lastRun: '5m ago' },
  { id: '3', name: 'Data Sync', description: 'Sync data between systems', status: 'error', steps: 4, lastRun: '12m ago' },
  { id: '4', name: 'Backup Process', description: 'Daily backup workflow', status: 'inactive', steps: 2, lastRun: '1 day ago' },
])

function getStatusColor(status: string) {
  switch (status) {
    case 'active': return 'success'
    case 'error': return 'error'
    case 'inactive': return 'grey'
    default: return 'grey'
  }
}
</script>

<template>
  <div>
    <!-- AI-First Flow Generation Input -->
    <v-card class="mb-6 bg-surface-light">
      <v-card-text class="text-center py-8">
        <v-icon icon="mdi-robot" size="48" color="secondary" class="mb-4" />
        <h2 class="text-h5 font-weight-medium mb-2">What would you like to automate?</h2>
        <v-textarea
          v-model="search"
          variant="outlined"
          placeholder="Describe your workflow in plain English... e.g., 'When a new order comes in via webhook, validate the payment with Stripe, send confirmation email, and update inventory'"
          rows="3"
          class="mx-auto"
          style="max-width: 800px"
          hide-details
        />
        <div class="mt-4">
          <v-btn color="secondary" size="large" prepend-icon="mdi-sparkles" to="/flows/generate">
            Generate Flow with AI
          </v-btn>
        </div>
        <p class="text-caption text-medium-emphasis mt-4">
          Examples: "Send Slack notification when GitHub PR merged" • "Backup database daily and upload to S3"
        </p>
      </v-card-text>
    </v-card>

    <!-- Header -->
    <div class="d-flex align-center justify-space-between mb-4">
      <h2 class="text-h6 font-weight-medium">Your Flows</h2>
      <div class="d-flex align-center gap-2">
        <v-text-field
          density="compact"
          variant="outlined"
          placeholder="Search flows..."
          prepend-inner-icon="mdi-magnify"
          hide-details
          style="width: 250px"
        />
        <v-btn-toggle variant="outlined" divided density="compact">
          <v-btn icon="mdi-view-grid" size="small" />
          <v-btn icon="mdi-view-list" size="small" />
        </v-btn-toggle>
      </div>
    </div>

    <!-- Flows Grid -->
    <v-row>
      <v-col v-for="flow in flows" :key="flow.id" cols="12" md="6" lg="4">
        <v-card hover>
          <v-card-text>
            <div class="d-flex align-center justify-space-between mb-3">
              <v-chip :color="getStatusColor(flow.status)" size="small" variant="tonal">
                {{ flow.status }}
              </v-chip>
              <v-menu>
                <template #activator="{ props }">
                  <v-btn v-bind="props" icon="mdi-dots-vertical" variant="text" size="small" />
                </template>
                <v-list density="compact" width="150">
                  <v-list-item prepend-icon="mdi-play" title="Run" />
                  <v-list-item prepend-icon="mdi-pencil" title="Edit" :to="`/flows/${flow.id}`" />
                  <v-list-item prepend-icon="mdi-content-copy" title="Duplicate" />
                  <v-divider class="my-1" />
                  <v-list-item prepend-icon="mdi-delete" title="Delete" class="text-error" />
                </v-list>
              </v-menu>
            </div>
            <h3 class="text-h6 font-weight-medium mb-1">{{ flow.name }}</h3>
            <p class="text-body-2 text-medium-emphasis mb-4">{{ flow.description }}</p>
            <div class="d-flex align-center justify-space-between text-caption text-medium-emphasis">
              <span>{{ flow.steps }} steps</span>
              <span>
                <v-icon icon="mdi-clock-outline" size="small" class="mr-1" />
                {{ flow.lastRun }}
              </span>
            </div>
          </v-card-text>
          <v-divider />
          <v-card-actions>
            <v-btn variant="text" color="primary" prepend-icon="mdi-play">
              Run Now
            </v-btn>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-pencil" :to="`/flows/${flow.id}`">
              Edit
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
