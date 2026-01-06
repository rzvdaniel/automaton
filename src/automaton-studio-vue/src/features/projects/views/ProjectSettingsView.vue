<script setup lang="ts">
import { ref } from 'vue'

const activeTab = ref('general')
const projectName = ref('E-Commerce Automation')
const projectDescription = ref('Automation workflows for e-commerce operations')

const members = ref([
  { name: 'John Doe', email: 'john@example.com', role: 'Owner' },
  { name: 'Jane Smith', email: 'jane@example.com', role: 'Editor' },
  { name: 'Bob Johnson', email: 'bob@example.com', role: 'Viewer' }
])

const resources = ref({
  flows: ['Order Processing', 'Email Campaign', 'Data Sync'],
  apps: ['Customer Portal', 'Admin Dashboard'],
  steps: ['ValidateEmail', 'SendSlackMsg', 'RunQuery']
})
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div class="d-flex align-center">
        <v-btn icon="mdi-arrow-left" variant="text" class="mr-2" />
        <div>
          <h1 class="text-h5 font-weight-bold">{{ projectName }}</h1>
          <p class="text-body-2 text-medium-emphasis">Project Settings</p>
        </div>
      </div>
      <v-btn color="primary">Save Changes</v-btn>
    </div>

    <!-- Tabs Layout -->
    <v-card>
      <v-tabs v-model="activeTab">
        <v-tab value="general">General</v-tab>
        <v-tab value="members">Members</v-tab>
        <v-tab value="resources">Resources</v-tab>
      </v-tabs>

      <v-divider />

      <v-window v-model="activeTab">
        <!-- General Tab -->
        <v-window-item value="general">
          <v-card-text class="pa-6">
            <v-text-field
              v-model="projectName"
              label="Project Name"
              variant="outlined"
              class="mb-4"
            />
            <v-textarea
              v-model="projectDescription"
              label="Description"
              variant="outlined"
              rows="3"
              class="mb-4"
            />
            
            <v-divider class="my-6" />
            
            <h3 class="text-h6 font-weight-bold mb-4">Shared Variables</h3>
            <p class="text-body-2 text-medium-emphasis mb-4">
              Variables shared across all flows in this project
            </p>
            <v-btn variant="outlined" prepend-icon="mdi-plus">Add Variable</v-btn>

            <v-divider class="my-6" />

            <h3 class="text-h6 font-weight-bold mb-4 text-error">Danger Zone</h3>
            <v-btn color="error" variant="outlined">Delete Project</v-btn>
          </v-card-text>
        </v-window-item>

        <!-- Members Tab -->
        <v-window-item value="members">
          <v-card-text class="pa-6">
            <div class="d-flex align-center justify-space-between mb-4">
              <h3 class="text-h6 font-weight-bold">Team Members</h3>
              <v-btn variant="outlined" prepend-icon="mdi-account-plus">Invite Member</v-btn>
            </div>

            <v-list>
              <v-list-item v-for="member in members" :key="member.email">
                <template #prepend>
                  <v-avatar color="primary">
                    <span class="text-white">{{ member.name.charAt(0) }}</span>
                  </v-avatar>
                </template>
                <v-list-item-title>{{ member.name }}</v-list-item-title>
                <v-list-item-subtitle>{{ member.email }}</v-list-item-subtitle>
                <template #append>
                  <v-chip size="small" class="mr-2">{{ member.role }}</v-chip>
                  <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
                </template>
              </v-list-item>
            </v-list>
          </v-card-text>
        </v-window-item>

        <!-- Resources Tab -->
        <v-window-item value="resources">
          <v-card-text class="pa-6">
            <v-row>
              <v-col cols="12" md="4">
                <h3 class="text-subtitle-1 font-weight-bold mb-2">Flows ({{ resources.flows.length }})</h3>
                <v-list density="compact">
                  <v-list-item v-for="flow in resources.flows" :key="flow" prepend-icon="mdi-flash">
                    <v-list-item-title>{{ flow }}</v-list-item-title>
                  </v-list-item>
                </v-list>
                <v-btn variant="outlined" size="small" block class="mt-2">Add Flow</v-btn>
              </v-col>

              <v-col cols="12" md="4">
                <h3 class="text-subtitle-1 font-weight-bold mb-2">Apps ({{ resources.apps.length }})</h3>
                <v-list density="compact">
                  <v-list-item v-for="app in resources.apps" :key="app" prepend-icon="mdi-web">
                    <v-list-item-title>{{ app }}</v-list-item-title>
                  </v-list-item>
                </v-list>
                <v-btn variant="outlined" size="small" block class="mt-2">Add App</v-btn>
              </v-col>

              <v-col cols="12" md="4">
                <h3 class="text-subtitle-1 font-weight-bold mb-2">Steps ({{ resources.steps.length }})</h3>
                <v-list density="compact">
                  <v-list-item v-for="step in resources.steps" :key="step" prepend-icon="mdi-puzzle">
                    <v-list-item-title>{{ step }}</v-list-item-title>
                  </v-list-item>
                </v-list>
                <v-btn variant="outlined" size="small" block class="mt-2">Add Step</v-btn>
              </v-col>
            </v-row>
          </v-card-text>
        </v-window-item>
      </v-window>
    </v-card>
  </div>
</template>
