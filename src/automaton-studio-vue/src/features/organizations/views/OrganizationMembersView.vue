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
import type { OrganizationMemberDto, InviteMemberRequest, UpdateOrganizationMemberRequest } from '../types'
import { OrganizationRole } from '../types'

const route = useRoute()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()

// State
const members = ref<OrganizationMemberDto[]>([])
const loading = ref(true)
const invitingMember = ref(false)
const currentUserRole = ref<string | null>(null)
const editingMemberId = ref<string | null>(null)
const updatingRole = ref(false)

// Computed
const organizationId = computed(() => route.params.id as string)
const isAppUser = computed(() => currentUserRole.value === OrganizationRole.AppUser)
const canInvite = computed(() => currentUserRole.value === OrganizationRole.OrgAdmin)
const canEditRoles = computed(() => currentUserRole.value === OrganizationRole.OrgAdmin)

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

  invitingMember.value = true
  try {
    await organizationsService.inviteMember(organizationId.value, inviteForm.value)
    toast.add({
      severity: 'success',
      summary: 'Invitation Sent',
      detail: `Invitation sent to ${inviteForm.value.email}`,
      life: 3000,
    })
    showInviteDialog.value = false
    resetInviteForm()
    await loadMembers()
  } catch (error: any) {
    let errorMessage = 'Failed to send invitation'
    if (error?.response?.status === 403) {
      errorMessage = 'You do not have permission to invite members'
    } else if (error?.response?.status === 404) {
      errorMessage = 'Organization not found'
    } else if (error?.response?.data?.message) {
      errorMessage = error.response.data.message
    }
    toast.add({
      severity: 'error',
      summary: 'Invitation Failed',
      detail: errorMessage,
      life: 5000,
    })
  } finally {
    invitingMember.value = false
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

// Handle role update
async function handleUpdateRole(member: OrganizationMemberDto, newRole: string) {
  if (newRole === member.role) {
    editingMemberId.value = null
    return
  }

  updatingRole.value = true
  try {
    const request: UpdateOrganizationMemberRequest = { role: newRole as any }
    await organizationsService.updateMemberRole(organizationId.value, member.userId, request)
    
    toast.add({
      severity: 'success',
      summary: 'Role Updated',
      detail: `${member.userName}'s role updated to ${newRole}`,
      life: 3000,
    })
    
    editingMemberId.value = null
    await loadMembers()
  } catch (error: any) {
    let errorMessage = 'Failed to update role'
    if (error?.response?.status === 403) {
      errorMessage = 'You do not have permission to update member roles'
    } else if (error?.response?.data?.message) {
      errorMessage = error.response.data.message
    }
    toast.add({
      severity: 'error',
      summary: 'Update Failed',
      detail: errorMessage,
      life: 5000,
    })
  } finally {
    updatingRole.value = false
  }
}

// Start editing a member's role
function startEditingRole(memberId: string) {
  editingMemberId.value = memberId
}

// Cancel editing
function cancelEditingRole() {
  editingMemberId.value = null
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
          <div v-if="editingMemberId === data.id" class="flex gap-2 align-items-center">
            <Dropdown
              :modelValue="data.role"
              :options="roleOptions"
              optionLabel="label"
              optionValue="value"
              placeholder="Select role"
              :disabled="updatingRole"
              @update:modelValue="(newRole: string) => handleUpdateRole(data, newRole)"
              autofocus
            />
            <Button
              icon="pi pi-times"
              text
              rounded
              severity="secondary"
              size="small"
              :disabled="updatingRole"
              @click="cancelEditingRole"
              v-tooltip="'Cancel'"
            />
          </div>
          <div v-else class="flex gap-2">
            <Tag :value="data.role" :severity="getRoleSeverity(data.role)" />
            <Tag v-if="data.isOwner" value="Owner" severity="contrast" />
          </div>
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

      <Column header="Actions" style="width: 10rem">
        <template #body="{ data }">
          <div class="flex gap-1">
            <Button
              v-if="canEditRoles && editingMemberId !== data.id"
              icon="pi pi-pencil"
              text
              rounded
              severity="secondary"
              :disabled="data.isOwner || editingMemberId !== null"
              @click="startEditingRole(data.id)"
              v-tooltip="data.isOwner ? 'Cannot edit owner role' : 'Edit role'"
            />
            <Button
              v-if="canEditRoles && !data.isOwner"
              icon="pi pi-trash"
              text
              rounded
              severity="danger"
              :disabled="editingMemberId !== null"
              @click="handleRemoveMember(data.userId, data.userName, data.isOwner)"
              v-tooltip="'Remove member'"
            />
          </div>
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
          :disabled="invitingMember"
          @click="showInviteDialog = false"
        />
        <Button
          label="Send Invitation"
          :icon="invitingMember ? 'pi pi-spin pi-spinner' : 'pi pi-send'"
          :loading="invitingMember"
          :disabled="invitingMember"
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
