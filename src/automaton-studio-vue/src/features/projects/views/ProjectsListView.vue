<script setup lang="ts">
import { ref } from 'vue'

const projects = ref([
  { id: '1', name: 'Acme Corp', role: 'Admin', members: 5, flows: 12, apps: 3 },
  { id: '2', name: 'Personal Workspace', role: 'Owner', members: 1, flows: 8, apps: 2 },
])

const sharedProjects = ref([
  { id: '3', name: 'Client Project X', role: 'Developer', owner: 'john@acme.com', flows: 5 },
])
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h5 font-weight-bold">Projects</h1>
        <p class="text-body-2 text-medium-emphasis">Organize your workflows and team</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus">
        Create Project
      </v-btn>
    </div>

    <!-- Your Projects -->
    <h2 class="text-h6 font-weight-medium mb-4">Your Projects</h2>
    <v-row class="mb-6">
      <v-col v-for="project in projects" :key="project.id" cols="12" md="6" lg="4">
        <v-card hover>
          <v-card-text>
            <div class="d-flex align-center mb-4">
              <v-avatar color="primary" size="48" variant="tonal" class="mr-3">
                <v-icon :icon="project.name === 'Personal Workspace' ? 'mdi-account' : 'mdi-domain'" />
              </v-avatar>
              <div>
                <h3 class="text-body-1 font-weight-medium">{{ project.name }}</h3>
                <span class="text-caption text-medium-emphasis">Role: {{ project.role }}</span>
              </div>
            </div>
            <div class="d-flex gap-4 text-caption text-medium-emphasis">
              <span>
                <v-icon icon="mdi-account-group" size="small" class="mr-1" />
                {{ project.members }} members
              </span>
              <span>
                <v-icon icon="mdi-flash" size="small" class="mr-1" />
                {{ project.flows }} flows
              </span>
              <span>
                <v-icon icon="mdi-earth" size="small" class="mr-1" />
                {{ project.apps }} apps
              </span>
            </div>
          </v-card-text>
          <v-divider />
          <v-card-actions>
            <v-btn variant="text" color="primary">Open</v-btn>
            <v-spacer />
            <v-btn variant="text" :to="`/projects/${project.id}/settings`">
              Settings
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Shared With You -->
    <h2 class="text-h6 font-weight-medium mb-4">Shared With You</h2>
    <v-row>
      <v-col v-for="project in sharedProjects" :key="project.id" cols="12" md="6" lg="4">
        <v-card hover>
          <v-card-text>
            <div class="d-flex align-center mb-4">
              <v-avatar color="secondary" size="48" variant="tonal" class="mr-3">
                <v-icon icon="mdi-domain" />
              </v-avatar>
              <div>
                <h3 class="text-body-1 font-weight-medium">{{ project.name }}</h3>
                <span class="text-caption text-medium-emphasis">Role: {{ project.role }}</span>
              </div>
            </div>
            <div class="text-caption text-medium-emphasis">
              <div>Owner: {{ project.owner }}</div>
              <div>{{ project.flows }} flows</div>
            </div>
          </v-card-text>
          <v-divider />
          <v-card-actions>
            <v-btn variant="text" color="primary">Open</v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
