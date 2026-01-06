<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dropdown from 'primevue/dropdown'

const searchQuery = ref('')
const languageFilter = ref(null)
const languageOptions = ref([
  { label: 'All', value: null },
  { label: 'Python', value: 'python' },
  { label: 'PowerShell', value: 'powershell' },
  { label: 'Batch', value: 'batch' },
])

// Mock steps data
const steps = ref([
  {
    id: 1,
    name: 'ValidateEmail',
    description: 'Validates email addresses using regex and DNS lookup',
    language: 'python',
    usedIn: 5,
    isAiGenerated: false,
  },
  {
    id: 2,
    name: 'SendSlackMsg',
    description: 'Sends message to Slack channel',
    language: 'python',
    usedIn: 3,
    isAiGenerated: false,
  },
  {
    id: 3,
    name: 'RunQuery',
    description: 'Executes SQL query against database',
    language: 'python',
    usedIn: 8,
    isAiGenerated: false,
  },
  {
    id: 4,
    name: 'StripePayment',
    description: 'Process payment via Stripe API',
    language: 'python',
    usedIn: 2,
    isAiGenerated: true,
  },
  {
    id: 5,
    name: 'S3Upload',
    description: 'Upload files to AWS S3 bucket',
    language: 'python',
    usedIn: 4,
    isAiGenerated: true,
  },
  {
    id: 6,
    name: 'WinService',
    description: 'Manage Windows services',
    language: 'powershell',
    usedIn: 1,
    isAiGenerated: false,
  },
])

const getLanguageIcon = (lang: string) => {
  switch (lang) {
    case 'python':
      return '🐍'
    case 'powershell':
      return '⚡'
    case 'batch':
      return '📜'
    default:
      return '📄'
  }
}
</script>

<template>
  <div class="steps-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Steps</h1>
        <p class="text-color-secondary mt-1 mb-0">Create and manage custom automation steps</p>
      </div>
      <Button label="New Step" icon="pi pi-plus" @click="$router.push('/steps/new')" />
    </div>

    <!-- Search and Filters -->
    <div class="flex align-items-center gap-3 mb-5">
      <span class="p-input-icon-left flex-1" style="max-width: 400px">
        <i class="pi pi-search" />
        <InputText v-model="searchQuery" placeholder="Search steps..." class="w-full" />
      </span>
      <Dropdown
        v-model="languageFilter"
        :options="languageOptions"
        option-label="label"
        option-value="value"
        placeholder="Language"
        class="w-10rem"
      />
    </div>

    <!-- My Steps Section -->
    <div class="mb-5">
      <div class="flex align-items-center gap-3 mb-4">
        <span class="font-semibold">My Steps</span>
        <Tag :value="`${steps.filter((s) => !s.isAiGenerated).length} steps`" severity="secondary" />
      </div>

      <div class="grid">
        <div
          v-for="step in steps.filter((s) => !s.isAiGenerated)"
          :key="step.id"
          class="col-12 md:col-6 lg:col-4"
        >
          <Card class="h-full step-card">
            <template #content>
              <div class="flex flex-column h-full">
                <div class="flex align-items-center justify-content-between mb-2">
                  <div class="flex align-items-center gap-2">
                    <span class="text-xl">{{ getLanguageIcon(step.language) }}</span>
                    <span class="font-semibold">{{ step.name }}</span>
                  </div>
                  <Button icon="pi pi-ellipsis-v" text rounded severity="secondary" size="small" />
                </div>

                <p class="text-color-secondary text-sm mt-0 mb-3">
                  {{ step.description }}
                </p>

                <div class="text-color-secondary text-sm mb-4">Used in: {{ step.usedIn }} flows</div>

                <div class="flex gap-2 mt-auto">
                  <Button
                    label="Edit"
                    icon="pi pi-pencil"
                    size="small"
                    outlined
                    class="flex-1"
                    @click="$router.push(`/steps/${step.id}`)"
                  />
                  <Button
                    label="Test"
                    icon="pi pi-play"
                    size="small"
                    outlined
                    severity="secondary"
                    class="flex-1"
                  />
                </div>
              </div>
            </template>
          </Card>
        </div>
      </div>
    </div>

    <!-- AI-Generated Steps Section -->
    <Card>
      <template #title>
        <div class="flex align-items-center justify-content-between">
          <div class="flex align-items-center gap-2">
            <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
            <span>AI-Generated Steps</span>
            <Tag :value="`${steps.filter((s) => s.isAiGenerated).length} steps`" severity="secondary" />
          </div>
          <Button label="Show/Hide" text size="small" />
        </div>
      </template>
      <template #subtitle>
        <span class="text-color-secondary">Steps created by AI that can be reused across flows</span>
      </template>
      <template #content>
        <div class="grid">
          <div
            v-for="step in steps.filter((s) => s.isAiGenerated)"
            :key="step.id"
            class="col-12 md:col-6 lg:col-4"
          >
            <div class="p-3 surface-ground border-round flex align-items-center justify-content-between">
              <div class="flex align-items-center gap-2">
                <span class="text-xl">{{ getLanguageIcon(step.language) }}</span>
                <div>
                  <div class="font-semibold">{{ step.name }}</div>
                  <div class="text-color-secondary text-sm">Used in {{ step.usedIn }} flows</div>
                </div>
              </div>
              <Button icon="pi pi-pencil" text rounded severity="secondary" size="small" />
            </div>
          </div>
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.steps-view {
  max-width: 1400px;
  margin: 0 auto;
}

.step-card {
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.step-card:hover {
  transform: translateY(-2px);
}
</style>
