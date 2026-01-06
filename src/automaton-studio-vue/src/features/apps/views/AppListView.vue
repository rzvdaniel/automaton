<script setup lang="ts">
import { ref } from 'vue'

const apps = ref([
  { 
    name: 'Customer Portal', 
    status: 'published', 
    url: '/web/customer-portal',
    updatedAt: '2 days ago'
  },
  { 
    name: 'Admin Dashboard', 
    status: 'draft', 
    url: null,
    updatedAt: '1 week ago'
  },
  { 
    name: 'Order Tracking', 
    status: 'published', 
    url: '/web/order-tracking',
    updatedAt: '3 days ago'
  }
])
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h4 font-weight-bold">Apps</h1>
        <p class="text-body-2 text-medium-emphasis">Build web applications with integrated backend</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus">
        New App
      </v-btn>
    </div>

    <!-- Apps Grid -->
    <v-row>
      <v-col v-for="app in apps" :key="app.name" cols="12" sm="6" md="4">
        <v-card>
          <v-card-title class="d-flex align-center justify-space-between">
            <span>{{ app.name }}</span>
            <v-chip :color="app.status === 'published' ? 'success' : 'warning'" size="small">
              {{ app.status }}
            </v-chip>
          </v-card-title>
          <v-card-text>
            <v-skeleton-loader type="image" height="150" class="mb-4" />
            <div class="text-body-2 text-medium-emphasis mb-2">
              Updated {{ app.updatedAt }}
            </div>
            <div v-if="app.url" class="text-body-2">
              <v-icon size="small" class="mr-1">mdi-link</v-icon>
              <a :href="app.url" class="text-primary">{{ app.url }}</a>
            </div>
          </v-card-text>
          <v-card-actions>
            <v-btn variant="text" size="small">Edit</v-btn>
            <v-btn variant="text" size="small">Preview</v-btn>
            <v-spacer />
            <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
