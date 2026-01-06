<script setup lang="ts">
import { ref } from 'vue'

const steps = ref([
  { id: '1', name: 'ValidateEmail', language: 'python', description: 'Validates email addresses', usedIn: 5 },
  { id: '2', name: 'SendSlackMsg', language: 'python', description: 'Sends Slack messages', usedIn: 3 },
  { id: '3', name: 'RunQuery', language: 'python', description: 'Executes SQL queries', usedIn: 8 },
])

function getLanguageIcon(lang: string) {
  switch (lang) {
    case 'python': return 'mdi-language-python'
    case 'powershell': return 'mdi-powershell'
    case 'batch': return 'mdi-console'
    default: return 'mdi-code-braces'
  }
}
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h5 font-weight-bold">Custom Steps</h1>
        <p class="text-body-2 text-medium-emphasis">Reusable automation building blocks</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus">
        New Step
      </v-btn>
    </div>

    <!-- Search -->
    <v-card class="mb-4">
      <v-card-text>
        <v-row align="center">
          <v-col cols="12" md="6">
            <v-text-field
              prepend-inner-icon="mdi-magnify"
              placeholder="Search steps..."
              hide-details
            />
          </v-col>
          <v-col cols="12" md="6" class="d-flex gap-2 justify-end">
            <v-select
              :items="['All', 'Python', 'PowerShell', 'Batch']"
              model-value="All"
              label="Language"
              hide-details
              style="max-width: 150px"
            />
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>

    <!-- Steps Grid -->
    <v-row>
      <v-col v-for="step in steps" :key="step.id" cols="12" md="6" lg="4">
        <v-card hover>
          <v-card-text>
            <div class="d-flex align-center mb-3">
              <v-avatar color="primary" size="40" variant="tonal" class="mr-3">
                <v-icon :icon="getLanguageIcon(step.language)" />
              </v-avatar>
              <div>
                <h3 class="text-body-1 font-weight-medium">{{ step.name }}</h3>
                <span class="text-caption text-medium-emphasis">{{ step.language }}</span>
              </div>
            </div>
            <p class="text-body-2 text-medium-emphasis mb-3">{{ step.description }}</p>
            <div class="text-caption text-medium-emphasis">
              Used in {{ step.usedIn }} flows
            </div>
          </v-card-text>
          <v-divider />
          <v-card-actions>
            <v-btn variant="text" prepend-icon="mdi-pencil" :to="`/steps/${step.id}`">
              Edit
            </v-btn>
            <v-btn variant="text" prepend-icon="mdi-play">
              Test
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
