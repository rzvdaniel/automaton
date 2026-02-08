<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useProjectsStore } from '@/features/projects/stores/projectsStore'
import Dropdown from 'primevue/dropdown'

const store = useProjectsStore()

// Load projects on component mount
onMounted(async () => {
  if (store.projects.length === 0) {
    await store.loadProjects()
  }
})

// Build options list with projects + "All projects" option
const optionsList = computed(() => {
  return [
    {
      id: null,
      displayName: 'Personal',
      displayLabel: 'Personal',
    },
    ...store.projects.map((p) => ({
      id: p.id,
      displayName: p.displayName,
      displayLabel: p.displayName,
    })),
  ]
})

// Get display label for current selection
const displayLabel = computed(() => {
  if (!store.activeProjectId) {
    return 'Personal'
  }
  const current = store.activeProject
  return current?.displayName || 'Select a project'
})
</script>

<template>
  <div class="project-switcher">
    <Dropdown
      v-model="store.activeProjectId"
      v-tooltip.bottom="'Personal: shows resources you own. Project: shows items in the selected project you can access.'"
      :options="optionsList"
      option-label="displayLabel"
      option-value="id"
      :placeholder="displayLabel"
      filter
      show-clear
      class="w-full"
      :loading="store.loading"
      @change="store.setActiveProject(store.activeProjectId)"
    />
  </div>
</template>

<style scoped>
.project-switcher {
  min-width: 200px;
  max-width: 280px;
}
</style>
