<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dropdown from 'primevue/dropdown'

const searchQuery = ref('')
const statusFilter = ref(null)
const statusOptions = ref([
  { label: 'All', value: null },
  { label: 'Published', value: 'published' },
  { label: 'Draft', value: 'draft' },
])

// Mock apps data
const apps = ref([
  {
    id: 1,
    name: 'Customer Portal',
    description: 'Internal tool for customer support team',
    status: 'published',
    files: 3,
    steps: 2,
    updated: '2h ago',
  },
  {
    id: 2,
    name: 'Admin Dashboard',
    description: 'Monitor system health and manage users',
    status: 'draft',
    files: 5,
    steps: 4,
    updated: '1d ago',
  },
  {
    id: 3,
    name: 'Feedback Form',
    description: 'Collect user feedback',
    status: 'published',
    files: 1,
    steps: 0,
    updated: '3d ago',
  },
  {
    id: 4,
    name: 'Workflow Viewer',
    description: 'View flow status and execution history',
    status: 'published',
    files: 4,
    steps: 1,
    updated: '5d ago',
  },
  {
    id: 5,
    name: 'Data Export Tool',
    description: 'Export reports to CSV/PDF',
    status: 'published',
    files: 2,
    steps: 3,
    updated: '1w ago',
  },
  {
    id: 6,
    name: 'API Tester',
    description: 'Test API endpoints',
    status: 'draft',
    files: 2,
    steps: 0,
    updated: '2w ago',
  },
])
</script>

<template>
  <div class="apps-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Apps</h1>
        <p class="text-color-secondary mt-1 mb-0">Build and publish web applications</p>
      </div>
      <Button label="New App" icon="pi pi-plus" @click="$router.push('/apps/new')" />
    </div>

    <!-- Search and Filters -->
    <div class="flex align-items-center gap-3 mb-5">
      <span class="p-input-icon-left flex-1" style="max-width: 400px">
        <i class="pi pi-search" />
        <InputText v-model="searchQuery" placeholder="Search apps..." class="w-full" />
      </span>
      <Dropdown
        v-model="statusFilter"
        :options="statusOptions"
        option-label="label"
        option-value="value"
        placeholder="Status"
        class="w-10rem"
      />
    </div>

    <!-- Apps Section -->
    <div class="mb-5">
      <div class="flex align-items-center gap-3 mb-4">
        <span class="font-semibold">My Apps</span>
        <Tag :value="`${apps.length} apps`" severity="secondary" />
      </div>

      <div class="grid">
        <div v-for="app in apps" :key="app.id" class="col-12 md:col-6 lg:col-4">
          <Card class="h-full app-card">
            <template #content>
              <div class="flex flex-column h-full">
                <div class="flex align-items-center justify-content-between mb-2">
                  <div class="flex align-items-center gap-2">
                    <i class="pi pi-globe" style="color: var(--p-primary-500)"></i>
                    <span class="font-semibold">{{ app.name }}</span>
                  </div>
                  <Button icon="pi pi-ellipsis-v" text rounded severity="secondary" size="small" />
                </div>

                <p class="text-color-secondary text-sm mt-0 mb-3 line-clamp-2">
                  {{ app.description }}
                </p>

                <div class="flex align-items-center gap-2 mb-3">
                  <Tag
                    :value="app.status === 'published' ? '✓ Published' : '📝 Draft'"
                    :severity="app.status === 'published' ? 'success' : 'secondary'"
                  />
                </div>

                <div class="text-color-secondary text-sm mb-4">
                  {{ app.files }} files, {{ app.steps }} steps • Updated {{ app.updated }}
                </div>

                <div class="flex gap-2 mt-auto">
                  <Button
                    label="Edit"
                    icon="pi pi-pencil"
                    size="small"
                    outlined
                    class="flex-1"
                    @click="$router.push(`/apps/${app.id}`)"
                  />
                  <Button
                    v-if="app.status === 'published'"
                    label="View"
                    icon="pi pi-external-link"
                    size="small"
                    outlined
                    severity="secondary"
                    class="flex-1"
                  />
                  <Button
                    v-else
                    label="Publish"
                    icon="pi pi-upload"
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

    <!-- Quick Start Section -->
    <Card>
      <template #title>
        <div class="flex align-items-center gap-2">
          <i class="pi pi-lightbulb" style="color: var(--p-yellow-500)"></i>
          <span>Quick Start</span>
        </div>
      </template>
      <template #content>
        <p class="text-color-secondary mt-0 mb-4">
          New to apps? Create your first web app with AI:
        </p>
        <ul class="text-color-secondary m-0 mb-4 pl-4">
          <li>Simple data entry form</li>
          <li>Dashboard with charts</li>
          <li>Customer feedback portal</li>
        </ul>
        <div class="flex gap-3">
          <Button label="Create with AI" icon="pi pi-sparkles" />
          <Button label="View Examples" outlined severity="secondary" />
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.apps-view {
  max-width: 1400px;
  margin: 0 auto;
}

.app-card {
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.app-card:hover {
  transform: translateY(-2px);
}

.line-clamp-2 {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
