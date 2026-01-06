<script setup lang="ts">
import { ref } from 'vue'

const searchQuery = ref('')
const filterType = ref('All')
const filterLanguage = ref('Python')

const mySteps = ref([
  { 
    name: 'ValidateEmail', 
    language: 'python',
    description: 'Validates email addresses using regex and DNS lookup',
    usedIn: 5,
    icon: '🐍'
  },
  { 
    name: 'SendSlackMsg', 
    language: 'python',
    description: 'Sends message to Slack channel',
    usedIn: 3,
    icon: '🐍'
  },
  { 
    name: 'RunQuery', 
    language: 'python',
    description: 'Executes SQL query',
    usedIn: 8,
    icon: '⚡'
  }
])

const aiGeneratedSteps = ref(24)
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h4 font-weight-bold">Steps</h1>
        <p class="text-body-2 text-medium-emphasis">Reusable automation building blocks</p>
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus">
        New Step
      </v-btn>
    </div>

    <!-- Search and Filters -->
    <v-row class="mb-6">
      <v-col cols="12" md="6">
        <v-text-field
          v-model="searchQuery"
          variant="outlined"
          prepend-inner-icon="mdi-magnify"
          placeholder="Search steps..."
          hide-details
        />
      </v-col>
      <v-col cols="6" md="3">
        <v-select
          v-model="filterType"
          :items="['All', 'My Steps', 'AI Generated']"
          variant="outlined"
          label="Filter by"
          hide-details
        />
      </v-col>
      <v-col cols="6" md="3">
        <v-select
          v-model="filterLanguage"
          :items="['All', 'Python', 'PowerShell', 'Batch']"
          variant="outlined"
          label="Language"
          hide-details
        />
      </v-col>
    </v-row>

    <!-- My Steps Section -->
    <v-card class="mb-6">
      <v-card-title>My Steps ({{ mySteps.length }})</v-card-title>
      <v-divider />
      <v-card-text>
        <v-row>
          <v-col v-for="step in mySteps" :key="step.name" cols="12" sm="6" md="4">
            <v-card variant="outlined">
              <v-card-title class="d-flex align-center">
                <span class="mr-2">{{ step.icon }}</span>
                {{ step.name }}
              </v-card-title>
              <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-4">
                  {{ step.description }}
                </p>
                <div class="text-body-2">
                  Used in: <strong>{{ step.usedIn }} flows</strong>
                </div>
              </v-card-text>
              <v-card-actions>
                <v-btn variant="text" size="small">Edit</v-btn>
                <v-btn variant="text" size="small">Test</v-btn>
                <v-spacer />
                <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
              </v-card-actions>
            </v-card>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>

    <!-- AI Generated Steps Section -->
    <v-card>
      <v-card-title class="d-flex align-center justify-space-between">
        <span>AI-Generated Steps ({{ aiGeneratedSteps }})</span>
        <v-btn variant="text" size="small">Show/Hide</v-btn>
      </v-card-title>
      <v-divider />
      <v-card-text>
        <p class="text-body-2 text-medium-emphasis">
          Steps created by AI that can be reused
        </p>
      </v-card-text>
    </v-card>
  </div>
</template>
