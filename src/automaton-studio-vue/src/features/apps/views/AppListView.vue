<script setup lang="ts">
import { ref } from 'vue'

const apps = ref([
  { id: '1', name: 'Customer Portal', description: 'Customer-facing web application', url: 'customer-portal', published: true },
  { id: '2', name: 'Admin Dashboard', description: 'Internal admin interface', url: 'admin', published: true },
  { id: '3', name: 'Metrics Viewer', description: 'System metrics display', url: 'metrics', published: false },
])
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h5 font-weight-bold">Apps</h1>
        <p class="text-body-2 text-medium-emphasis">Build and publish web applications</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus">
        Create App
      </v-btn>
    </div>

    <v-row>
      <v-col v-for="app in apps" :key="app.id" cols="12" md="6" lg="4">
        <v-card hover>
          <v-card-text>
            <div class="d-flex align-center justify-space-between mb-3">
              <v-chip :color="app.published ? 'success' : 'grey'" size="small" variant="tonal">
                {{ app.published ? 'Published' : 'Draft' }}
              </v-chip>
              <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
            </div>
            <h3 class="text-h6 font-weight-medium mb-1">{{ app.name }}</h3>
            <p class="text-body-2 text-medium-emphasis mb-4">{{ app.description }}</p>
            <div class="text-caption text-medium-emphasis">
              <v-icon icon="mdi-link" size="small" class="mr-1" />
              /web/{{ app.url }}
            </div>
          </v-card-text>
          <v-divider />
          <v-card-actions>
            <v-btn variant="text" color="primary" prepend-icon="mdi-open-in-new">
              Open
            </v-btn>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-pencil" :to="`/apps/${app.id}`">
              Edit
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
