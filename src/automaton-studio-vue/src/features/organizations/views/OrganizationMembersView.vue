<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { organizationsService } from '../services/organizationsService'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Dropdown from 'primevue/dropdown'
import Tag from 'primevue/tag'
import type { OrganizationMemberDto, InviteMemberRequest } from '../types'
import { OrganizationRole } from '../types'

const route = useRoute()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()

// State
const members = ref<OrganizationMemberDto[]>([])
const loading = ref(true)
const currentUserRole = ref<string | null>(null)

// Computed
const organizationId = computed(() => route.params.id as string)
const isAppUser = computed(() => currentUserRole.value === OrganizationRole.AppUser)
const canInvite = computed(() => 
  currentUserRole.value === OrganizationRole.OrgAdmin || 
  currentUserRole.value === OrganizationRole.Developer
)

// Dialog and form states
const showInviteDialog = ref(false)
const inviteForm = ref<InviteMemberRequest>({
  email: '',
  role: OrganizationRole.AppUser,
})

// Role options for dropdown
const roleOptions = [
  { label: 'Org Admin', value: OrganizationRole.OrgAdmin },
  { label: 'Developer', value: OrganizationRole.Developer },
  { label: 'App User', value: OrganizationRole.AppUser },
]

// Load members on mount
onMounted(async () => {
  await loadMembers()
  // Get current organization to determine user role
  try {
    const org = await organizationsService.getOrganization(organizationId.value)
    currentUserRole.value = org.currentUserRole || null
    
    // Redirect AppUsers back to dashboard
    if (isAppUser.value) {
      toast.add({
        severity: 'warn',
        summary: 'Access Restricted',
        detail: 'AppUsers cannot view member management',
        life: 3000,
      })
      router.push({ name: 'organization-dashboard', params: { id: organizationId.value } })
    }
  } catch (error) {
    // Error handled by service
  }
})

// Load organization members
async function loadMembers() {
  try {
    loading.value = true
    members.value = await organizationsService.getMembers(organizationId.value)
  } catch (error) {
    // Error is handled by the service
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

// Handle invite member
async function handleInviteMember() {
  if (!inviteForm.value.email) {
    toast.add({
      severity: 'error',
      summary: 'Validation Error',
      detail: 'Email is required',
      life: 3000,
    })
    return
  }

  try {
    await organizationsService.inviteMember(organizationId.value, inviteForm.value)
    showInviteDialog.value = false
    resetInviteForm()
    await loadMembers()
  } catch (error) {
    // Error toast is already shown by service
  }
}

// Handle remove member
function handleRemoveMember(userId: string, userName: string, isOwner: boolean) {
  if (isOwner) {
    toast.add({
      severity: 'error',
      summary: 'Cannot Remove Owner',
      detail: 'The organization owner cannot be removed',
      life: 3000,
    })
    return
  }

  confirm.require({
    message: `Are you sure you want to remove "${userName}" from this organization?`,
    header: 'Remove Member',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await organizationsService.removeMember(organizationId.value, userId)
        await loadMembers()
      } catch (error) {
        // Error toast is already shown by service
      }
    },
  })
}

// Reset invite form
function resetInviteForm() {
  inviteForm.value = {
    email: '',
    role: OrganizationRole.AppUser,
  }
}

// Navigate back to organization
function navigateBack() {
  router.push({ name: 'organization-dashboard', params: { id: organizationId.value } })
}
</script>

<template>
  <div class="organization-members">
    <div class="page-header">
      <div class="flex align-items-center gap-3">
        <Button
          icon="pi pi-arrow-left"
          text
          rounded
          @click="navigateBack"
        />
        <div>
          <h1>Members</h1>
          <p class="text-muted">Manage organization members and their roles</p>
        </div>
      </div>
      <Button
        v-if="canInvite"
        label="Invite Member"
        icon="pi pi-user-plus"
        @click="showInviteDialog = true"
      />
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading members...</p>
    </div>

    <DataTable
      v-else
      :value="members"
      :paginator="true"
      :rows="10"
      :rowsPerPageOptions="[10, 20, 50]"
      responsiveLayout="scroll"
      stripedRows
    >
      <Column field="userName" header="User" sortable>
        <template #body="{ data }">
          <div class="flex flex-column">
            <span class="font-semibold">{{ data.userName }}</span>
            <span class="text-sm text-muted">{{ data.email }}</span>
          </div>
        </template>
      </Column>

      <Column field="role" header="Role" sortable>
        <template #body="{ data }">
          <Tag :value="data.role" :severity="getRoleSeverity(data.role)" />
          <Tag v-if="data.isOwner" value="Owner" severity="contrast" class="ml-2" />
        </template>
      </Column>

      <Column field="joinedAt" header="Joined" sortable>
        <template #body="{ data }">
          {{ formatDate(data.joinedAt) }}
        </template>
      </Column>

      <Column field="invitedByName" header="Invited By">
        <template #body="{ data }">
          <span class="text-muted">{{ data.invitedByName || '-' }}</span>
        </template>
      </Column>

      <Column header="Actions" style="width: 8rem">
        <template #body="{ data }">
          <Button
            icon="pi pi-trash"
            text
            rounded
            severity="danger"
            :disabled="data.isOwner"
            @click="handleRemoveMember(data.userId, data.userName, data.isOwner)"
            v-tooltip="data.isOwner ? 'Cannot remove owner' : 'Remove member'"
          />
        </template>
      </Column>
    </DataTable>

    <!-- Invite Member Dialog -->
    <Dialog
      v-model:visible="showInviteDialog"
      modal
      header="Invite Member"
      :style="{ width: '30rem' }"
    >
      <div class="flex flex-column gap-3 pt-3">
        <div class="flex flex-column gap-2">
          <label for="member-email" class="font-semibold">Email Address *</label>
          <InputText
            id="member-email"
            v-model="inviteForm.email"
            placeholder="member@example.com"
            autofocus
          />
        </div>

        <div class="flex flex-column gap-2">
          <label for="member-role" class="font-semibold">Role *</label>
          <Dropdown
            id="member-role"
            v-model="inviteForm.role"
            :options="roleOptions"
            optionLabel="label"
            optionValue="value"
            placeholder="Select role"
          />
          <small class="text-muted">
            <strong>Org Admin:</strong> Full control<br>
            <strong>Developer:</strong> Create projects and apps<br>
            <strong>App User:</strong> Execute assigned apps only
          </small>
        </div>
      </div>

      <template #footer>
        <Button
          label="Cancel"
          text
          severity="secondary"
          @click="showInviteDialog = false"
        />
        <Button
          label="Send Invitation"
          icon="pi pi-send"
          @click="handleInviteMember"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.organization-members {
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

.text-sm {
  font-size: 0.875rem;
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
