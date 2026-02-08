<script setup lang="ts">
/**
 * AppAssignmentDialog.vue
 * Dialog for assigning apps to organization members
 */
import { ref, computed, watch } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Checkbox from 'primevue/checkbox'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import { organizationsService } from '../services/organizationsService'
import { useToast } from 'primevue/usetoast'
import type { OrganizationMemberDto } from '../types'
import { OrganizationRole } from '../types'

const props = defineProps<{
  visible: boolean
  organizationId: string
  appId: string
  appName: string
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  'assigned': []
}>()

const toast = useToast()

// State
const members = ref<OrganizationMemberDto[]>([])
const assignedUserIds = ref<Set<string>>(new Set())
const selectedUserIds = ref<string[]>([])
const loading = ref(false)
const saving = ref(false)
const searchQuery = ref('')

// Computed visibility
const isVisible = computed({
  get: () => props.visible,
  set: (value) => emit('update:visible', value),
})

// Filter members - only show AppUsers
const filteredMembers = computed(() => {
  const appUsers = members.value.filter(m => m.role === OrganizationRole.AppUser)
  
  if (!searchQuery.value) return appUsers
  
  const query = searchQuery.value.toLowerCase()
  return appUsers.filter(m => 
    m.userName.toLowerCase().includes(query) || 
    m.email.toLowerCase().includes(query)
  )
})

// Check if user is already assigned
const isUserAssigned = (userId: string) => {
  return assignedUserIds.value.has(userId)
}

// Load data when dialog opens
watch(isVisible, async (visible) => {
  if (visible) {
    selectedUserIds.value = []
    searchQuery.value = ''
    await loadData()
  }
})

// Load members and current assignments
async function loadData() {
  try {
    loading.value = true
    
    // Load all members
    members.value = await organizationsService.getMembers(props.organizationId)
    
    // Load currently assigned users
    const assignedUsers = await organizationsService.getAppAssignedUsers(props.organizationId, props.appId)
    assignedUserIds.value = new Set(assignedUsers.map((u: any) => u.id))
    
  } catch (error) {
    // Error already shown by service
  } finally {
    loading.value = false
  }
}

// Handle assign selected users
async function handleAssign() {
  if (selectedUserIds.value.length === 0) {
    toast.add({
      severity: 'warn',
      summary: 'No Selection',
      detail: 'Please select at least one user to assign',
      life: 3000,
    })
    return
  }

  try {
    saving.value = true
    
    // Assign app to each selected user
    for (const userId of selectedUserIds.value) {
      if (!assignedUserIds.value.has(userId)) {
        await organizationsService.assignApp(props.organizationId, props.appId, { userId })
      }
    }
    
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: `App assigned to ${selectedUserIds.value.length} user(s)`,
      life: 3000,
    })
    
    emit('assigned')
    isVisible.value = false
    
  } catch (error) {
    // Error already shown by service
  } finally {
    saving.value = false
  }
}

// Handle unassign user
async function handleUnassign(userId: string, userName: string) {
  try {
    await organizationsService.unassignApp(props.organizationId, props.appId, userId)
    
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: `App unassigned from ${userName}`,
      life: 3000,
    })
    
    // Reload data
    await loadData()
    emit('assigned')
    
  } catch (error) {
    // Error already shown by service
  }
}
</script>

<template>
  <Dialog
    v-model:visible="isVisible"
    modal
    :header="`Assign: ${appName}`"
    :style="{ width: '50rem' }"
  >
    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading members...</p>
    </div>

    <div v-else class="dialog-content">
      <Message severity="info" :closable="false">
        Select AppUser members to assign this app to. Only AppUsers can be assigned to apps.
      </Message>

      <!-- Search -->
      <div class="search-box">
        <span class="p-input-icon-left w-full">
          <i class="pi pi-search"></i>
          <InputText
            v-model="searchQuery"
            placeholder="Search members by name or email..."
            class="w-full"
          />
        </span>
      </div>

      <!-- Members table -->
      <DataTable
        v-model:selection="selectedUserIds"
        :value="filteredMembers"
        data-key="userId"
        :paginator="true"
        :rows="5"
        responsive-layout="scroll"
        striped-rows
        class="members-table"
      >
        <Column selection-mode="multiple" header-style="width: 3rem">
          <template #body="{ data }">
            <Checkbox
              v-model="selectedUserIds"
              :value="data.userId"
              :disabled="isUserAssigned(data.userId)"
            />
          </template>
        </Column>

        <Column field="userName" header="User">
          <template #body="{ data }">
            <div class="flex flex-column">
              <span class="font-semibold">{{ data.userName }}</span>
              <span class="text-sm text-muted">{{ data.email }}</span>
            </div>
          </template>
        </Column>

        <Column header="Status">
          <template #body="{ data }">
            <span v-if="isUserAssigned(data.userId)" class="status-badge assigned">
              <i class="pi pi-check-circle mr-1"></i>
              Already Assigned
            </span>
            <span v-else class="status-badge available">
              <i class="pi pi-circle mr-1"></i>
              Available
            </span>
          </template>
        </Column>

        <Column header="Actions" style="width: 8rem">
          <template #body="{ data }">
            <Button
              v-if="isUserAssigned(data.userId)"
              v-tooltip="'Unassign'"
              icon="pi pi-times"
              text
              rounded
              severity="danger"
              @click="handleUnassign(data.userId, data.userName)"
            />
          </template>
        </Column>
      </DataTable>

      <div v-if="filteredMembers.length === 0" class="empty-message">
        <i class="pi pi-users" style="font-size: 2rem; color: var(--p-text-muted-color)"></i>
        <p class="text-muted">
          {{ searchQuery ? 'No members found matching your search' : 'No AppUser members available' }}
        </p>
      </div>
    </div>

    <template #footer>
      <Button
        label="Cancel"
        text
        severity="secondary"
        @click="isVisible = false"
      />
      <Button
        label="Assign Selected"
        icon="pi pi-check"
        :loading="saving"
        :disabled="selectedUserIds.length === 0"
        @click="handleAssign"
      />
    </template>
  </Dialog>
</template>

<style scoped>
.dialog-content {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding-top: 1rem;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 3rem 0;
  gap: 1rem;
}

.search-box {
  margin-bottom: 0.5rem;
}

.members-table {
  margin-top: 0.5rem;
}

.text-muted {
  color: var(--p-text-muted-color);
}

.text-sm {
  font-size: 0.875rem;
}

.status-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.25rem 0.75rem;
  border-radius: 1rem;
  font-size: 0.875rem;
  font-weight: 500;
}

.status-badge.assigned {
  background: var(--p-green-100);
  color: var(--p-green-700);
}

.status-badge.available {
  background: var(--p-surface-100);
  color: var(--p-text-muted-color);
}

.empty-message {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2rem;
  text-align: center;
  gap: 0.5rem;
}
</style>
