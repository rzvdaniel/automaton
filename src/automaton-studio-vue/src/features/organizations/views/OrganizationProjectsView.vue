<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Dropdown from 'primevue/dropdown'
import EmptyState from '@/shared/components/EmptyState.vue'
import { organizationsService } from '../services/organizationsService'
import { projectsService } from '@/features/projects/services/projectsService'
import { OrganizationRole } from '../types'
import type { ProjectListItem } from '@/features/projects/types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

// State
const projects = ref<any[]>([])
const loading = ref(true)
const currentUserRole = ref<string | null>(null)
const showLinkDialog = ref(false)
const availableProjects = ref<ProjectListItem[]>([])
const selectedProject = ref<ProjectListItem | null>(null)
const linking = ref(false)

const organizationId = computed(() => route.params.id as string)
const isAppUser = computed(() => currentUserRole.value === OrganizationRole.AppUser)
const isOrgAdmin = computed(() => currentUserRole.value === OrganizationRole.OrgAdmin)

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

async function openLinkDialog() {
  try {
    // Load all user's projects
    const allProjects = await projectsService.listProjects()
    // Filter to only personal projects (no organizationId) where user is admin
    availableProjects.value = allProjects.filter(
      p => !p.organizationId && (p.role === 'ProjectAdmin')
    )
    
    if (availableProjects.value.length === 0) {
      toast.add({
        severity: 'info',
        summary: 'No Projects Available',
        detail: 'You have no personal projects to link. Create a personal project first.',
        life: 5000,
      })
      return
    }
    
    showLinkDialog.value = true
  } catch (error) {
    // Error handled by service
  }
}

async function linkProject() {
  if (!selectedProject.value) return
  
  try {
    linking.value = true
    await organizationsService.linkProject(organizationId.value, selectedProject.value.id)
    showLinkDialog.value = false
    selectedProject.value = null
    await loadProjects() // Reload to show the new project
  } catch (error) {
    // Error handled by service
  } finally {
    linking.value = false
  }
}

async function unlinkProject(project: any) {
  if (!confirm(`Are you sure you want to unlink "${project.displayName}"? It will become a personal project again.`)) {
    return
  }
  
  try {
    await organizationsService.unlinkProject(organizationId.value, project.id)
    await loadProjects() // Reload to remove the unlinked project
  } catch (error) {
    // Error handled by service
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
      <Button
        v-if="isOrgAdmin"
        label="Link Project"
        icon="pi pi-link"
        @click="openLinkDialog"
      />
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading projects...</p>
    </div>

    <EmptyState
      v-else-if="projects.length === 0"
      icon="pi-folder"
      title="No projects in organization"
      description="Link existing personal projects or create new projects under this organization."
    >
      <Button
        v-if="isOrgAdmin"
        label="Link Project"
        icon="pi pi-link"
        @click="openLinkDialog"
      />
    </EmptyState>

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

      <Column header="Actions" style="width: 12rem">
        <template #body="{ data }">
          <div class="flex gap-2">
            <Button
              label="View"
              icon="pi pi-eye"
              outlined
              size="small"
              @click="viewProject(data)"
            />
            <Button
              v-if="isOrgAdmin"
              icon="pi pi-chain"
              severity="danger"
              outlined
              size="small"
              @click="unlinkProject(data)"
              v-tooltip.top="'Unlink from organization'"
            />
          </div>
        </template>
      </Column>
    </DataTable>

    <!-- Link Project Dialog -->
    <Dialog
      v-model:visible="showLinkDialog"
      header="Link Project to Organization"
      :modal="true"
      :style="{ width: '500px' }"
    >
      <div class="flex flex-col gap-4 py-4">
        <p class="text-muted">
          Select a personal project to link to this organization. You must be a ProjectAdmin to link a project.
        </p>
        
        <div class="field">
          <label for="project-select">Project</label>
          <Dropdown
            id="project-select"
            v-model="selectedProject"
            :options="availableProjects"
            optionLabel="displayName"
            placeholder="Select a project"
            class="w-full"
          >
            <template #option="{ option }">
              <div>
                <div class="font-semibold">{{ option.displayName }}</div>
                <div class="text-sm text-muted">{{ option.description || 'No description' }}</div>
              </div>
            </template>
          </Dropdown>
        </div>
      </div>

      <template #footer>
        <Button
          label="Cancel"
          text
          @click="showLinkDialog = false"
        />
        <Button
          label="Link Project"
          icon="pi pi-link"
          :loading="linking"
          :disabled="!selectedProject"
          @click="linkProject"
        />
      </template>
    </Dialog>
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
