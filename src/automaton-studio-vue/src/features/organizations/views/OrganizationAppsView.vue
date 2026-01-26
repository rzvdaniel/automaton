<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Tag from 'primevue/tag'
import EmptyState from '@/shared/components/EmptyState.vue'
import AppAssignmentDialog from '../components/AppAssignmentDialog.vue'

const route = useRoute()
const router = useRouter()

// State
const apps = ref<any[]>([])
const loading = ref(true)
const showAssignDialog = ref(false)
const selectedApp = ref<any>(null)

const organizationId = computed(() => route.params.id as string)

onMounted(async () => {
  await loadApps()
})

// Load organization apps (from projects)
async function loadApps() {
  try {
    loading.value = true
    // TODO: Load apps that belong to projects in this organization
    // For now, showing placeholder
    apps.value = []
  } catch (error) {
    // Error handled by service
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
      title="No apps available"
      description="Apps from organization projects will appear here for assignment to members"
    />

    <DataTable
      v-else
      :value="apps"
      :paginator="true"
      :rows="10"
      responsiveLayout="scroll"
      stripedRows
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
