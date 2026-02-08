<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import EmptyState from '@/shared/components/EmptyState.vue'
import AppAssignmentDialog from '../components/AppAssignmentDialog.vue'
import { organizationsService } from '../services/organizationsService'
import { OrganizationRole } from '../types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

// State
const apps = ref<any[]>([])
const loading = ref(true)
const showAssignDialog = ref(false)
const selectedApp = ref<any>(null)
const currentUserRole = ref<string | null>(null)

const organizationId = computed(() => route.params.id as string)
const isAppUser = computed(() => currentUserRole.value === OrganizationRole.AppUser)

onMounted(async () => {
  // Check user role first
  try {
    const org = await organizationsService.getOrganization(organizationId.value)
    currentUserRole.value = org.currentUserRole || null
    
    // Redirect AppUsers - they should use My Apps instead
    if (isAppUser.value) {
      toast.add({
        severity: 'warn',
        summary: 'Access Restricted',
        detail: 'AppUsers cannot manage app assignments. Use "My Apps" to view your assigned apps.',
        life: 4000,
      })
      router.push({ name: 'assigned-apps' })
      return
    }
  } catch (error) {
    // Error handled by service
  }
  
  await loadApps()
})

// Load organization apps (from projects)
async function loadApps() {
  try {
    loading.value = true
    const appsData = await organizationsService.getOrganizationApps(organizationId.value)
    
    // Get assignment counts for each app
    const appsWithCounts = await Promise.all(
      appsData.map(async (app) => {
        try {
          const assignedUsers = await organizationsService.getAppAssignedUsers(organizationId.value, app.id)
          return {
            ...app,
            assignedUsers: assignedUsers.length
          }
        } catch (error) {
          // If error getting assignments, just set to 0
          return {
            ...app,
            assignedUsers: 0
          }
        }
      })
    )
    
    apps.value = appsWithCounts
  } catch (error) {
    // Error handled by service
    apps.value = []
  } finally {
    loading.value = false
  }
}

function openAssignDialog(app: any) {
  selectedApp.value = app
  showAssignDialog.value = true
}

function handleAssigned() {
  // Reload apps or assignments
  loadApps()
}

function navigateBack() {
  router.push({ name: 'organization-dashboard', params: { id: organizationId.value } })
}
</script>

<template>
  <div class="organization-apps">
    <div class="page-header">
      <div class="flex align-items-center gap-3">
        <Button
          icon="pi pi-arrow-left"
          text
          rounded
          @click="navigateBack"
        />
        <div>
          <h1>App Assignments</h1>
          <p class="text-muted">Manage app access for organization members</p>
        </div>
      </div>
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading apps...</p>
    </div>

    <EmptyState
      v-else-if="apps.length === 0"
      icon="pi-th-large"
      title="No apps in organization"
      message="Create apps in organization projects first, then they will appear here for assignment to members. Apps must belong to projects that are part of this organization."
    />

    <DataTable
      v-else
      :value="apps"
      :paginator="true"
      :rows="10"
      responsive-layout="scroll"
      striped-rows
    >
      <Column field="name" header="App Name" sortable>
        <template #body="{ data }">
          <div class="flex align-items-center gap-2">
            <i class="pi pi-th-large"></i>
            <span class="font-semibold">{{ data.name }}</span>
          </div>
        </template>
      </Column>

      <Column field="description" header="Description">
        <template #body="{ data }">
          <span class="text-muted">{{ data.description || 'No description' }}</span>
        </template>
      </Column>

      <Column field="assignedUsers" header="Assigned Users">
        <template #body="{ data }">
          <Tag :value="`${data.assignedUsers || 0} users`" severity="info" />
        </template>
      </Column>

      <Column header="Actions" style="width: 12rem">
        <template #body="{ data }">
          <Button
            label="Manage Access"
            icon="pi pi-users"
            outlined
            size="small"
            @click="openAssignDialog(data)"
          />
        </template>
      </Column>
    </DataTable>

    <!-- App Assignment Dialog -->
    <AppAssignmentDialog
      v-if="selectedApp"
      v-model:visible="showAssignDialog"
      :organization-id="organizationId"
      :app-id="selectedApp.id"
      :app-name="selectedApp.name"
      @assigned="handleAssigned"
    />
  </div>
</template>

<style scoped>
.organization-apps {
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
