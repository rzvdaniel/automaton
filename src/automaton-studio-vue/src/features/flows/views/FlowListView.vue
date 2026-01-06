<script setup lang="ts">
import { ref } from 'vue'

const flows = ref([
  { 
    name: 'Order Processing', 
    steps: 5, 
    status: 'success', 
    lastRun: '2m ago',
    description: 'Process customer orders automatically'
  },
  { 
    name: 'Email Campaign', 
    steps: 3, 
    status: 'success', 
    lastRun: '5m ago',
    description: 'Send automated email campaigns'
  },
  { 
    name: 'Data Sync', 
    steps: 4, 
    status: 'error', 
    lastRun: 'Failed 12m',
    description: 'Sync data between systems'
  }
])

const viewMode = ref('grid')
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h4 font-weight-bold">Flows</h1>
        <p class="text-body-2 text-medium-emphasis">AI-powered workflow automation</p>
      </div>
      <div class="d-flex gap-2">
        <v-btn-group variant="outlined" divided>
          <v-btn :variant="viewMode === 'grid' ? 'flat' : 'outlined'" @click="viewMode = 'grid'">
            <v-icon>mdi-view-grid</v-icon>
          </v-btn>
          <v-btn :variant="viewMode === 'list' ? 'flat' : 'outlined'" @click="viewMode = 'list'">
            <v-icon>mdi-view-list</v-icon>
          </v-btn>
        </v-btn-group>
        <v-btn color="primary" prepend-icon="mdi-plus">
          New Flow
        </v-btn>
      </div>
    </div>

    <!-- AI Input Card -->
    <v-card class="mb-6">
      <v-card-text class="pa-6">
        <div class="d-flex align-center mb-4">
          <v-icon size="large" color="primary" class="mr-3">mdi-robot</v-icon>
          <h2 class="text-h6">What would you like to automate?</h2>
        </div>
        
        <v-textarea
          variant="outlined"
          placeholder="When a new order comes in via webhook, validate the payment with Stripe, send confirmation email, and update inventory in our database..."
          rows="3"
          class="mb-4"
        />

        <div class="d-flex justify-space-between align-center">
          <div class="text-body-2 text-medium-emphasis">
            <strong>Examples:</strong> "Send Slack notification when GitHub PR merged" • "Backup database daily and upload to S3"
          </div>
          <v-btn color="primary" size="large">
            Generate Flow
          </v-btn>
        </div>
      </v-card-text>
    </v-card>

    <!-- Flows List -->
    <v-card>
      <v-card-title class="d-flex align-center justify-space-between">
        <span>Your Flows</span>
        <v-text-field
          variant="outlined"
          density="compact"
          prepend-inner-icon="mdi-magnify"
          placeholder="Search flows..."
          hide-details
          style="max-width: 300px"
        />
      </v-card-title>
      <v-divider />
      <v-card-text>
        <v-row v-if="viewMode === 'grid'">
          <v-col v-for="flow in flows" :key="flow.name" cols="12" sm="6" md="4">
            <v-card variant="outlined">
              <v-card-title>{{ flow.name }}</v-card-title>
              <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-4">{{ flow.description }}</p>
                <div class="d-flex justify-space-between align-center mb-2">
                  <span class="text-body-2">{{ flow.steps }} steps</span>
                  <v-chip :color="flow.status === 'success' ? 'success' : 'error'" size="small">
                    {{ flow.status === 'success' ? '✅' : '❌' }} Last: {{ flow.lastRun }}
                  </v-chip>
                </div>
              </v-card-text>
              <v-card-actions>
                <v-btn variant="text" size="small">Run</v-btn>
                <v-btn variant="text" size="small">Edit</v-btn>
                <v-spacer />
                <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
              </v-card-actions>
            </v-card>
          </v-col>
        </v-row>

        <v-list v-else>
          <v-list-item v-for="flow in flows" :key="flow.name">
            <v-list-item-title>{{ flow.name }}</v-list-item-title>
            <v-list-item-subtitle>{{ flow.description }} • {{ flow.steps }} steps</v-list-item-subtitle>
            <template #append>
              <v-chip :color="flow.status === 'success' ? 'success' : 'error'" size="small" class="mr-2">
                {{ flow.status === 'success' ? '✅' : '❌' }} {{ flow.lastRun }}
              </v-chip>
              <v-btn variant="text" size="small">Run</v-btn>
              <v-btn variant="text" size="small">Edit</v-btn>
            </template>
          </v-list-item>
        </v-list>
      </v-card-text>
    </v-card>
  </div>
</template>
