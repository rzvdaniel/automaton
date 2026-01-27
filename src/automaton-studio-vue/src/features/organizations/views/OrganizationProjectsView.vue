<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import EmptyState from '@/shared/components/EmptyState.vue'
import { organizationsService } from '../services/organizationsService'
import { OrganizationRole } from '../types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

// State
const projects = ref<any[]>([])
const loading = ref(true)
const currentUserRole = ref<string | null>(null)

const organizationId = computed(() => route.params.id as string)
const isAppUser = computed(() => currentUserRole.value === OrganizationRole.AppUser)

onMounted(async () => {
  // Check user role first
  try {
    const org = await organizationsService.getOrganization(organizationId.value)
    currentUserRole.value = org.currentUserRole || null
    
    // Redirect AppUsers - they shouldn't see projects
    if (isAppUser.value) {
      toast.add({
        severity: 'warn',
        summary: 'Access Restricted',
        detail: 'AppUsers cannot view organization projects.',
        life: 3000,
      })
      router.push({ name: 'organization-dashboard', params: { id: organizationId.value } })
      return
    }
  } catch (error) {
    // Error handled by service
  }
  
  await loadProjects()
})

// Load organization projects
async function loadProjects() {
  try {
    loading.value = true
    const projectsData = await organizationsService.getOrganizationProjects(organizationId.value)
    projects.value = projectsData
  } catch (error) {
    // Error handled by service
    projects.value = []
  } finally {
    loading.value = false
  }
}

function viewProject(project: any) {
  router.push({ name: 'project-settings', params: { id: project.id } })
}

function navigateBack() {
  router.push({ name: 'organization-dashboard', params: { id: organizationId.value } })
}

// Format date for display
const formatDate = (date: string) => {
  return new Date(date).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}
</script>

<template>
  <div class="organization-projects">
    <div class="page-header">
      <div class="flex align-items-center gap-3">
        <Button
          icon="pi pi-arrow-left"
          text
          rounded
          @click="navigateBack"
        />
        <div>
          <h1>Organization Projects</h1>
          <p class="text-muted">Projects belonging to this organization</p>
        </div>
      </div>
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading projects...</p>
    </div>

    <EmptyState
      v-else-if="projects.length === 0"
      icon="pi-folder"
      title="No projects in organization"
      description="Create projects and link them to this organization to see them here. When creating a project, set its Organization field to this organization."
    />

    <DataTable
      v-else
      :value="projects"
      :paginator="true"
      :rows="10"
      responsiveLayout="scroll"
      stripedRows
    >
      <Column field="displayName" header="Project Name" sortable>
        <template #body="{ data }">
          <div class="flex align-items-center gap-2">
            <i class="pi pi-folder"></i>
            <span class="font-semibold">{{ data.displayName }}</span>
          </div>
        </template>
      </Column>

      <Column field="description" header="Description">
        <template #body="{ data }">
          <span class="text-muted">{{ data.description || 'No description' }}</span>
        </template>
      </Column>

      <Column field="members" header="Members">
        <template #body="{ data }">
          <Tag :value="`${data.members?.length || 0} members`" severity="info" />
        </template>
      </Column>

      <Column field="updated" header="Last Updated" sortable>
        <template #body="{ data }">
          <span class="text-muted">{{ formatDate(data.updated) }}</span>
        </template>
      </Column>

      <Column header="Actions" style="width: 10rem">
        <template #body="{ data }">
          <Button
            label="View"
            icon="pi pi-eye"
            outlined
            size="small"
            @click="viewProject(data)"
          />
        </template>
      </Column>
    </DataTable>
  </div>
</template>

<style scoped>
.organization-projects {
  padding: 2rem;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: start;
  margin-bottom: 2rem;
}

.page-header h1 {
  margin: 0 0 0.5rem 0;
  font-size: 2rem;
  font-weight: 600;
}

.text-muted {
  color: var(--p-text-muted-color);
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 0;
  gap: 1rem;
}
</style>
