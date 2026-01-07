<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'

const searchQuery = ref('')

// Mock projects data
const myProjects = ref([
  {
    id: 1,
    name: 'Acme Corp',
    icon: '🏢',
    role: 'Admin',
    members: 5,
    flows: 12,
    apps: 3,
    isPersonal: false,
  },
  {
    id: 2,
    name: 'Personal Workspace',
    icon: '👤',
    role: 'Owner',
    members: 1,
    flows: 8,
    apps: 2,
    isPersonal: true,
  },
])

const sharedProjects = ref([
  {
    id: 3,
    name: 'Client Project X',
    icon: '🏢',
    role: 'Developer',
    owner: 'john@acme.com',
    flows: 5,
    apps: 1,
  },
])
</script>

<template>
  <div class="projects-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Projects</h1>
        <p class="text-color-secondary mt-1 mb-0">Manage your projects and team collaboration</p>
      </div>
      <Button label="Create Project" icon="pi pi-plus" />
    </div>

    <!-- Search -->
    <div class="mb-5">
      <span class="p-input-icon-left" style="max-width: 400px">
        <i class="pi pi-search" />
        <InputText v-model="searchQuery" placeholder="Search projects..." class="w-full" />
      </span>
    </div>

    <!-- Your Projects -->
    <div class="mb-5">
      <div class="flex align-items-center gap-3 mb-4">
        <span class="font-semibold">Your Projects</span>
        <Tag :value="`${myProjects.length} projects`" severity="secondary" />
      </div>

      <div class="grid">
        <div v-for="project in myProjects" :key="project.id" class="col-12 md:col-6 lg:col-4">
          <Card class="h-full project-card">
            <template #content>
              <div class="flex flex-column h-full">
                <div class="flex align-items-center justify-content-between mb-3">
                  <div class="flex align-items-center gap-2">
                    <span class="text-2xl">{{ project.icon }}</span>
                    <span class="font-semibold">{{ project.name }}</span>
                  </div>
                  <Button icon="pi pi-ellipsis-v" text rounded severity="secondary" size="small" />
                </div>

                <div class="mb-3">
                  <Tag :value="`Role: ${project.role}`" severity="info" />
                </div>

                <div class="text-color-secondary text-sm mb-4">
                  <div class="mb-1">Members: {{ project.members }}</div>
                  <div class="mb-1">Flows: {{ project.flows }}</div>
                  <div>Apps: {{ project.apps }}</div>
                </div>

                <div class="flex gap-2 mt-auto">
                  <Button
                    label="Open"
                    icon="pi pi-folder-open"
                    size="small"
                    outlined
                    class="flex-1"
                  />
                  <Button
                    label="Settings"
                    icon="pi pi-cog"
                    size="small"
                    outlined
                    severity="secondary"
                    class="flex-1"
                    @click="$router.push(`/projects/${project.id}/settings`)"
                  />
                </div>
              </div>
            </template>
          </Card>
        </div>
      </div>
    </div>

    <!-- Shared With You -->
    <div>
      <div class="flex align-items-center gap-3 mb-4">
        <span class="font-semibold">Shared With You</span>
        <Tag :value="`${sharedProjects.length} projects`" severity="secondary" />
      </div>

      <div class="grid">
        <div v-for="project in sharedProjects" :key="project.id" class="col-12 md:col-6 lg:col-4">
          <Card class="h-full project-card">
            <template #content>
              <div class="flex flex-column h-full">
                <div class="flex align-items-center justify-content-between mb-3">
                  <div class="flex align-items-center gap-2">
                    <span class="text-2xl">{{ project.icon }}</span>
                    <span class="font-semibold">{{ project.name }}</span>
                  </div>
                </div>

                <div class="mb-3">
                  <Tag :value="`Role: ${project.role}`" severity="secondary" />
                </div>

                <div class="text-color-secondary text-sm mb-4">
                  <div class="mb-1">Owner: {{ project.owner }}</div>
                  <div class="mb-1">Flows: {{ project.flows }}</div>
                  <div>Apps: {{ project.apps }}</div>
                </div>

                <div class="flex gap-2 mt-auto">
                  <Button label="Open" icon="pi pi-folder-open" size="small" outlined class="w-full" />
                </div>
              </div>
            </template>
          </Card>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.projects-view {
  max-width: 1400px;
  margin: 0 auto;
}

.project-card {
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.project-card:hover {
  transform: translateY(-2px);
}
</style>
