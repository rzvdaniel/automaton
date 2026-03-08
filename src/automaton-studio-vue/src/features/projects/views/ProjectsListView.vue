<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useProjectsStore } from '@/features/projects/stores/projectsStore'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Dropdown from 'primevue/dropdown'
import Card from 'primevue/card'
import Tag from 'primevue/tag'
import EmptyState from '@/shared/components/EmptyState.vue'
import { organizationsService } from '@/features/organizations/services/organizationsService'
import type { CreateProjectRequest } from '@/features/projects/types'
import type { OrganizationListItem } from '@/features/organizations/types'
import { ProjectRole } from '@/features/projects/types'
import { usePlanFeatures } from '@/shared/composables/usePlanFeatures'
import { usePlanLimit } from '@/shared/composables/usePlanLimit'

const store = useProjectsStore()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const { canCreateProjects } = usePlanFeatures()
const { showUpgrade } = usePlanLimit()

// Computed
const hasProjects = computed(() => store.projects.length > 0)

// Dialog and form states
const showCreateDialog = ref(false)
const createForm = ref<CreateProjectRequest>({
  name: '',
  displayName: '',
  description: '',
  organizationId: undefined,
})
const userOrganizations = ref<OrganizationListItem[]>([])
const selectedOrganization = ref<OrganizationListItem | null>(null)

// Load projects on mount
onMounted(async () => {
  try {
    await store.loadProjects()
    // Load user's organizations for the create dialog
    userOrganizations.value = await organizationsService.listOrganizations()
  } catch (error) {
    // Error is handled by the store/service
  }
})

// Role badge severity map
const getRoleSeverity = (role: string) => {
  const severityMap: Record<string, string> = {
    [ProjectRole.ProjectAdmin]: 'danger',
    [ProjectRole.Developer]: 'warning',
    [ProjectRole.AppUser]: 'info',
    [ProjectRole.Viewer]: 'secondary',
  }
  return severityMap[role] || 'secondary'
}

// Format date for display
const formatDate = (date: string) => {
  return new Date(date).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

// Handle create project
function openCreateDialog() {
  if (!canCreateProjects.value) {
    showUpgrade('CanCreateProjects', 'Starter')
    return
  }
  showCreateDialog.value = true
}

async function handleCreateProject() {
  if (!createForm.value.name || !createForm.value.displayName) {
    toast.add({
      severity: 'error',
      summary: 'Validation Error',
      detail: 'Name and Display Name are required',
      life: 3000,
    })
    return
  }

  try {
    // Set organizationId from selected organization
    createForm.value.organizationId = selectedOrganization.value?.id
    await store.createProject(createForm.value)
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Project created successfully',
      life: 3000,
    })
    showCreateDialog.value = false
    resetCreateForm()
  } catch (error) {
    // Error toast is already shown by service
  }
}

// Handle delete project
function handleDeleteProject(projectId: string, projectName: string) {
  confirm.require({
    message: `Are you sure you want to delete "${projectName}"? This will deactivate the project.`,
    header: 'Delete Project',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await store.deleteProject(projectId)
        toast.add({
          severity: 'success',
          summary: 'Success',
          detail: 'Project deleted successfully',
          life: 3000,
        })
        console.log('Project deleted, projects list:', store.projects)
      } catch (error: any) {
        console.error('Failed to delete project:', error)
        let errorMessage = 'Failed to delete project';
        if (error.response?.status === 403) {
          errorMessage = 'You do not have permission to delete this project. Only Project Admins can delete projects.';
        } else if (error.response?.status === 404) {
          errorMessage = 'Project not found';
        } else if (error.response?.data?.message) {
          errorMessage = error.response.data.message;
        }
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: errorMessage,
          life: 5000,
        })
      }
    },
  })
}

// Handle reactivate project
async function handleReactivateProject(projectId: string, _projectName: string) {
  try {
    await store.reactivateProject(projectId)
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Project reactivated successfully',
      life: 3000,
    })
    console.log('Project reactivated, projects list:', store.projects)
  } catch (error: any) {
    console.error('Failed to reactivate project:', error)
    let errorMessage = 'Failed to reactivate project';
    if (error.response?.status === 403) {
      errorMessage = 'You do not have permission to reactivate this project. Only Project Admins can reactivate projects.';
    } else if (error.response?.status === 404) {
      errorMessage = 'Project not found';
    } else if (error.response?.data?.message) {
      errorMessage = error.response.data.message;
    }
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: errorMessage,
      life: 5000,
    })
  }
}

// Navigate to project settings
function handleOpenSettings(projectId: string) {
  router.push(`/projects/${projectId}/settings`)
}

// Reset create form
function resetCreateForm() {
  createForm.value = {
    name: '',
    displayName: '',
    description: '',
    organizationId: undefined,
  }
  selectedOrganization.value = null
}

// Close dialog and reset form
function closeCreateDialog() {
  showCreateDialog.value = false
  resetCreateForm()
}
</script>

<template>
  <div class="projects-list-view">
    <!-- Header -->
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-3xl font-bold">Projects</h1>
        <p class="text-color-secondary mt-1">Manage and organize your projects</p>
      </div>
      <Button
        icon="pi pi-plus"
        label="Create Project"
        @click="openCreateDialog"
      />
    </div>

    <!-- Projects DataTable -->
    <Card>
      <template #content>
        <DataTable
          v-if="hasProjects"
          :value="store.projects"
          :loading="store.loading"
          paginator
          :rows="10"
          responsive-layout="scroll"
          class="p-datatable-sm"
        >
          <Column field="displayName" header="Name" sortable>
            <template #body="slotProps">
              <div>
                <div class="flex items-center gap-2">
                  <span>{{ slotProps.data.displayName }}</span>
                  <Tag
                    v-if="!slotProps.data.isActive"
                    value="Inactive"
                    severity="danger"
                    icon="pi pi-ban"
                  />
                </div>
                <Tag
                  v-if="slotProps.data.organizationName"
                  :value="slotProps.data.organizationName"
                  severity="success"
                  class="mt-1"
                  icon="pi pi-users"
                />
              </div>
            </template>
          </Column>
          <Column field="description" header="Description" />
          <Column field="role" header="Role" sortable>
            <template #body="slotProps">
              <Tag :value="slotProps.data.role" :severity="getRoleSeverity(slotProps.data.role)" />
            </template>
          </Column>
          <Column field="memberCount" header="Members" sortable />
          <Column field="updatedAt" header="Updated" sortable>
            <template #body="slotProps">
              {{ formatDate(slotProps.data.updatedAt) }}
            </template>
          </Column>
          <Column header="Actions" style="width: 150px">
            <template #body="slotProps">
              <div class="flex gap-2">
                <Button
                  v-if="!slotProps.data.isActive"
                  v-tooltip.top="'Reactivate Project'"
                  icon="pi pi-check-circle"
                  severity="success"
                  text
                  rounded
                  @click="handleReactivateProject(slotProps.data.id, slotProps.data.displayName)"
                />
                <Button
                  v-if="slotProps.data.isActive"
                  v-tooltip.top="'Settings'"
                  icon="pi pi-cog"
                  severity="secondary"
                  text
                  rounded
                  @click="handleOpenSettings(slotProps.data.id)"
                />
                <Button
                  v-if="slotProps.data.isActive"
                  v-tooltip.top="'Delete Project'"
                  icon="pi pi-trash"
                  severity="danger"
                  text
                  rounded
                  @click="handleDeleteProject(slotProps.data.id, slotProps.data.displayName)"
                />
              </div>
            </template>
          </Column>
        </DataTable>

        <!-- Empty State -->
        <EmptyState
          v-else
          icon="pi pi-inbox"
          title="No Projects"
          message="You haven't created any projects yet. Create one to get started."
        >
          <Button
            icon="pi pi-plus"
            label="Create Project"
            @click="openCreateDialog"
          />
        </EmptyState>
      </template>
    </Card>

    <!-- Create Project Dialog -->
    <Dialog
      v-model:visible="showCreateDialog"
      header="Create Project"
      modal
      :style="{ width: '450px' }"
      @hide="closeCreateDialog"
    >
      <div class="space-y-4">
        <div>
          <label for="project-name" class="block text-sm font-medium mb-2">
            Name <span class="text-red-500">*</span>
          </label>
          <InputText
            id="project-name"
            v-model="createForm.name"
            class="w-full"
            placeholder="e.g., my-project"
          />
          <p class="text-xs text-color-secondary mt-1">
            Used internally, no spaces allowed
          </p>
        </div>

        <div>
          <label for="project-display-name" class="block text-sm font-medium mb-2">
            Display Name <span class="text-red-500">*</span>
          </label>
          <InputText
            id="project-display-name"
            v-model="createForm.displayName"
            class="w-full"
            placeholder="e.g., My Project"
          />
        </div>

        <div>
          <label for="project-description" class="block text-sm font-medium mb-2">
            Description
          </label>
          <Textarea
            id="project-description"
            v-model="createForm.description"
            class="w-full"
            :auto-resize="true"
            rows="3"
            placeholder="Optional project description"
          />
        </div>
        
        <div v-if="userOrganizations.length > 0">
          <label for="project-organization" class="block text-sm font-medium mb-2">
            Organization (Optional)
          </label>
          <Dropdown
            id="project-organization"
            v-model="selectedOrganization"
            :options="userOrganizations"
            option-label="name"
            placeholder="Personal Project (no organization)"
            class="w-full"
            :show-clear="true"
          />
          <p class="text-xs text-color-secondary mt-1">
            Leave empty for a personal project, or select an organization to create the project under
          </p>
        </div>
      </div>

      <template #footer>
        <Button
          label="Cancel"
          severity="secondary"
          @click="closeCreateDialog"
        />
        <Button
          label="Create"
          @click="handleCreateProject"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.projects-list-view {
  padding: 2rem;
}

.space-y-4 > * + * {
  margin-top: 1rem;
}
</style>
