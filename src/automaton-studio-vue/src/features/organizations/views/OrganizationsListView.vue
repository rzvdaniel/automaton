<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { organizationsService } from '../services/organizationsService'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import EmptyState from '@/shared/components/EmptyState.vue'
import type { CreateOrganizationRequest, OrganizationListItem } from '../types'
import { OrganizationRole } from '../types'

const router = useRouter()
const toast = useToast()
const confirm = useConfirm()

// State
const organizations = ref<OrganizationListItem[]>([])
const loading = ref(true)

// Computed
const hasOrganizations = computed(() => organizations.value.length > 0)

// Dialog and form states
const showCreateDialog = ref(false)
const isCreatingOrganization = ref(false)
const createForm = ref<CreateOrganizationRequest>({
  name: '',
  description: '',
  allowPublicSignup: false,
})

// Load organizations on mount
onMounted(async () => {
  await loadOrganizations()
})

// Load organizations
async function loadOrganizations() {
  try {
    loading.value = true
    organizations.value = await organizationsService.listOrganizations()
  } catch (error) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load organizations',
      life: 3000,
    })
  } finally {
    loading.value = false
  }
}

// Role badge severity map
const getRoleSeverity = (role: string) => {
  const severityMap: Record<string, string> = {
    [OrganizationRole.OrgAdmin]: 'danger',
    [OrganizationRole.Developer]: 'warn',
    [OrganizationRole.AppUser]: 'info',
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

// Handle create organization
async function handleCreateOrganization() {
  if (!createForm.value.name) {
    toast.add({
      severity: 'error',
      summary: 'Validation Error',
      detail: 'Organization name is required',
      life: 3000,
    })
    return
  }

  try {
    isCreatingOrganization.value = true
    await organizationsService.createOrganization(createForm.value)
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Organization created successfully',
      life: 3000,
    })
    resetCreateForm()
    await loadOrganizations()
  } catch (error) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to create organization',
      life: 3000,
    })
  } finally {
    isCreatingOrganization.value = false
    showCreateDialog.value = false
  }
}

// Handle delete organization
function handleDeleteOrganization(organizationId: string, organizationName: string) {
  confirm.require({
    message: `Are you sure you want to delete "${organizationName}"? This will deactivate the organization.`,
    header: 'Delete Organization',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await organizationsService.deleteOrganization(organizationId)
        toast.add({
          severity: 'success',
          summary: 'Success',
          detail: 'Organization deleted successfully',
          life: 3000,
        })
        await loadOrganizations()
      } catch (error) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: 'Failed to delete organization',
          life: 3000,
        })
      }
    },
  })
}

// Navigate to organization dashboard
function navigateToOrganization(organizationId: string) {
  router.push({ name: 'organization-dashboard', params: { id: organizationId } })
}

// Reset create form
function resetCreateForm() {
  createForm.value = {
    name: '',
    description: '',
    allowPublicSignup: false,
  }
}
</script>

<template>
  <div class="organizations-container">
    <div class="page-header">
      <div>
        <h1>Organizations</h1>
        <p class="text-muted">Manage your organizations and team collaboration</p>
      </div>
      <Button
        label="Create Organization"
        icon="pi pi-plus"
        @click="showCreateDialog = true"
      />
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading organizations...</p>
    </div>

    <EmptyState
      v-else-if="!hasOrganizations"
      icon="pi-building"
      title="No organizations yet"
      message="Create your first organization to enable team collaboration and app assignment"
    >
      <Button
        label="Create Your First Organization"
        icon="pi pi-plus"
        @click="showCreateDialog = true"
      />
    </EmptyState>

    <DataTable
      v-else
      :value="organizations"
      :paginator="true"
      :rows="10"
      :rows-per-page-options="[10, 20, 50]"
      responsive-layout="scroll"
      striped-rows
    >
      <Column field="name" header="Name" sortable>
        <template #body="{ data }">
          <div class="flex align-items-center gap-2">
            <i v-if="data.logoUrl" class="pi pi-image"></i>
            <i v-else class="pi pi-building"></i>
            <span class="font-semibold">{{ data.name }}</span>
          </div>
        </template>
      </Column>

      <Column field="description" header="Description">
        <template #body="{ data }">
          <span class="text-muted">{{ data.description || 'No description' }}</span>
        </template>
      </Column>

      <Column field="role" header="Your Role" sortable>
        <template #body="{ data }">
          <Tag :value="data.role" :severity="getRoleSeverity(data.role)" />
        </template>
      </Column>

      <Column field="memberCount" header="Members" sortable>
        <template #body="{ data }">
          <i class="pi pi-users mr-1"></i>
          {{ data.memberCount }}
        </template>
      </Column>

      <Column field="projectCount" header="Projects" sortable>
        <template #body="{ data }">
          <i class="pi pi-folder mr-1"></i>
          {{ data.projectCount }}
        </template>
      </Column>

      <Column field="updatedAt" header="Last Updated" sortable>
        <template #body="{ data }">
          {{ formatDate(data.updatedAt) }}
        </template>
      </Column>

      <Column header="Actions" style="width: 12rem">
        <template #body="{ data }">
          <div class="flex gap-2">
            <Button
              v-tooltip="'View Details'"
              icon="pi pi-eye"
              text
              rounded
              severity="secondary"
              @click="navigateToOrganization(data.id)"
            />
            <Button
              v-if="data.role === OrganizationRole.OrgAdmin"
              v-tooltip="'Delete Organization'"
              icon="pi pi-trash"
              text
              rounded
              severity="danger"
              @click="handleDeleteOrganization(data.id, data.name)"
            />
          </div>
        </template>
      </Column>
    </DataTable>

    <!-- Create Organization Dialog -->
    <Dialog
      v-model:visible="showCreateDialog"
      modal
      header="Create New Organization"
      :style="{ width: '30rem' }"
    >
      <div class="flex flex-column gap-3 pt-3">
        <div class="flex flex-column gap-2">
          <label for="org-name" class="font-semibold">Organization Name *</label>
          <InputText
            id="org-name"
            v-model="createForm.name"
            placeholder="Enter organization name"
            autofocus
          />
        </div>

        <div class="flex flex-column gap-2">
          <label for="org-description" class="font-semibold">Description</label>
          <Textarea
            id="org-description"
            v-model="createForm.description"
            rows="3"
            placeholder="Enter organization description (optional)"
          />
        </div>
      </div>

      <template #footer>
        <Button
          label="Cancel"
          text
          severity="secondary"
          :disabled="isCreatingOrganization"
          @click="showCreateDialog = false"
        />
        <Button
          label="Create"
          icon="pi pi-check"
          :loading="isCreatingOrganization"
          :disabled="isCreatingOrganization"
          @click="handleCreateOrganization"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.organizations-container {
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
