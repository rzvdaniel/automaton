<script setup lang="ts">
import { ref } from 'vue'

const yourProjects = ref([
  { 
    name: 'E-Commerce Automation', 
    members: 3,
    flows: 12,
    apps: 2,
    updatedAt: '2 days ago'
  },
  { 
    name: 'Marketing Campaign', 
    members: 5,
    flows: 8,
    apps: 1,
    updatedAt: '1 week ago'
  }
])

const sharedProjects = ref([
  { 
    name: 'Customer Support', 
    owner: 'John Doe',
    members: 7,
    flows: 15,
    apps: 3,
    updatedAt: '3 days ago'
  }
])
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h4 font-weight-bold">Projects</h1>
        <p class="text-body-2 text-medium-emphasis">Organize flows, apps, and steps into projects</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus">
        New Project
      </v-btn>
    </div>

    <!-- Your Projects -->
    <v-card class="mb-6">
      <v-card-title>Your Projects</v-card-title>
      <v-divider />
      <v-card-text>
        <v-row>
          <v-col v-for="project in yourProjects" :key="project.name" cols="12" md="6">
            <v-card variant="outlined">
              <v-card-title>{{ project.name }}</v-card-title>
              <v-card-text>
                <v-list density="compact">
                  <v-list-item>
                    <template #prepend>
                      <v-icon size="small">mdi-account-multiple</v-icon>
                    </template>
                    <v-list-item-title>{{ project.members }} members</v-list-item-title>
                  </v-list-item>
                  <v-list-item>
                    <template #prepend>
                      <v-icon size="small">mdi-flash</v-icon>
                    </template>
                    <v-list-item-title>{{ project.flows }} flows</v-list-item-title>
                  </v-list-item>
                  <v-list-item>
                    <template #prepend>
                      <v-icon size="small">mdi-web</v-icon>
                    </template>
                    <v-list-item-title>{{ project.apps }} apps</v-list-item-title>
                  </v-list-item>
                </v-list>
                <div class="text-body-2 text-medium-emphasis mt-4">
                  Updated {{ project.updatedAt }}
                </div>
              </v-card-text>
              <v-card-actions>
                <v-btn variant="text" size="small">Open</v-btn>
                <v-btn variant="text" size="small">Settings</v-btn>
                <v-spacer />
                <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
              </v-card-actions>
            </v-card>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>

    <!-- Shared Projects -->
    <v-card>
      <v-card-title>Shared with You</v-card-title>
      <v-divider />
      <v-card-text>
        <v-row>
          <v-col v-for="project in sharedProjects" :key="project.name" cols="12" md="6">
            <v-card variant="outlined">
              <v-card-title class="d-flex align-center justify-space-between">
                <span>{{ project.name }}</span>
                <v-chip size="small" variant="outlined">Shared</v-chip>
              </v-card-title>
              <v-card-text>
                <div class="text-body-2 text-medium-emphasis mb-4">
                  Owner: {{ project.owner }}
                </div>
                <v-list density="compact">
                  <v-list-item>
                    <template #prepend>
                      <v-icon size="small">mdi-account-multiple</v-icon>
                    </template>
                    <v-list-item-title>{{ project.members }} members</v-list-item-title>
                  </v-list-item>
                  <v-list-item>
                    <template #prepend>
                      <v-icon size="small">mdi-flash</v-icon>
                    </template>
                    <v-list-item-title>{{ project.flows }} flows</v-list-item-title>
                  </v-list-item>
                  <v-list-item>
                    <template #prepend>
                      <v-icon size="small">mdi-web</v-icon>
                    </template>
                    <v-list-item-title>{{ project.apps }} apps</v-list-item-title>
                  </v-list-item>
                </v-list>
                <div class="text-body-2 text-medium-emphasis mt-4">
                  Updated {{ project.updatedAt }}
                </div>
              </v-card-text>
              <v-card-actions>
                <v-btn variant="text" size="small">Open</v-btn>
                <v-spacer />
                <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
              </v-card-actions>
            </v-card>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>
  </div>
</template>
