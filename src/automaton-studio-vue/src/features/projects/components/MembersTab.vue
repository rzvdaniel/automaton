<template>
  <div class="members-tab">
    <!-- Invite Member Form -->
    <Card v-if="canInviteMembers" class="mb-4">
      <template #title>
        <div class="flex items-center gap-2">
          <i class="pi pi-user-plus"></i>
          <span>Invite Member</span>
        </div>
      </template>
      <template #content>
        <div class="flex gap-3">
          <div class="flex-1">
            <label for="member-email" class="block text-sm font-medium mb-2">
              Email Address
            </label>
            <InputText
              id="member-email"
              v-model="inviteForm.email"
              placeholder="user@example.com"
              class="w-full"
            />
          </div>
          <div class="flex-1">
            <label for="member-role" class="block text-sm font-medium mb-2">Role</label>
            <Dropdown
              id="member-role"
              v-model="inviteForm.role"
              :options="roleOptions"
              option-label="label"
              option-value="value"
              placeholder="Select role"
              class="w-full"
            />
          </div>
          <div class="flex items-end">
            <Button
              label="Invite"
              icon="pi pi-send"
              @click="handleInvite"
              :disabled="!inviteForm.email || !inviteForm.role"
            />
          </div>
        </div>
      </template>
    </Card>

    <!-- Members DataTable -->
    <DataTable
      :value="members"
      :loading="loading"
      paginator
      :rows="10"
      dataKey="userId"
      emptyMessage="No members in this project"
    >
      <Column field="userName" header="Name" sortable>
        <template #body="{ data }">
          <div class="flex items-center gap-2">
            <i class="pi pi-user"></i>
            <span>{{ data.userName }}</span>
            <Tag v-if="data.isOwner" severity="contrast" value="Owner" />
          </div>
        </template>
      </Column>
      <Column field="email" header="Email" sortable />
      <Column field="role" header="Role" sortable>
        <template #body="{ data }">
          <Tag :severity="getRoleSeverity(data.role)" :value="data.role" />
        </template>
      </Column>
      <Column field="joinedAt" header="Joined" sortable>
        <template #body="{ data }">
          {{ formatDate(data.joinedAt) }}
        </template>
      </Column>
      <Column field="invitedByName" header="Invited By" />
      <Column header="Actions" style="width: 180px">
        <template #body="{ data }">
          <div class="flex gap-2">
            <Dropdown
              v-if="canManageMembers && !data.isOwner"
              v-model="data.role"
              :options="roleOptions"
              option-label="label"
              option-value="value"
              @change="handleUpdateRole(data)"
              class="w-32"
            />
            <Button
              v-if="canManageMembers && !data.isOwner"
              icon="pi pi-trash"
              severity="danger"
              text
              rounded
              @click="handleRemove(data)"
              aria-label="Remove member"
            />
          </div>
        </template>
      </Column>
    </DataTable>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { useToast } from 'primevue/usetoast';
import { useConfirm } from 'primevue/useconfirm';
import { useProjectsStore } from '../stores/projectsStore';
import { ProjectRole, type ProjectMemberDto } from '../types';
import Card from 'primevue/card';
import DataTable from 'primevue/datatable';
import Column from 'primevue/column';
import InputText from 'primevue/inputtext';
import Dropdown from 'primevue/dropdown';
import Tag from 'primevue/tag';
import Button from 'primevue/button';

interface Props {
  projectId: string;
  currentUserRole?: string;
}

const props = defineProps<Props>();

const toast = useToast();
const confirm = useConfirm();
const store = useProjectsStore();

const inviteForm = ref({
  email: '',
  role: ProjectRole.Viewer,
});

const roleOptions = [
  { label: 'Project Admin', value: ProjectRole.ProjectAdmin },
  { label: 'Developer', value: ProjectRole.Developer },
  { label: 'App User', value: ProjectRole.AppUser },
  { label: 'Viewer', value: ProjectRole.Viewer },
];

const members = computed(() => store.members);
const loading = computed(() => store.loading);

const canInviteMembers = computed(() => {
  return props.currentUserRole === ProjectRole.ProjectAdmin;
});

const canManageMembers = computed(() => {
  return props.currentUserRole === ProjectRole.ProjectAdmin;
});

function getRoleSeverity(role: string): 'danger' | 'warn' | 'info' | 'secondary' {
  switch (role) {
    case ProjectRole.ProjectAdmin:
      return 'danger';
    case ProjectRole.Developer:
      return 'warn';
    case ProjectRole.AppUser:
      return 'info';
    case ProjectRole.Viewer:
      return 'secondary';
    default:
      return 'secondary';
  }
}

function formatDate(dateString: string): string {
  if (!dateString) return 'N/A';
  const date = new Date(dateString);
  return date.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

async function handleInvite() {
  if (!inviteForm.value.email || !inviteForm.value.role) {
    toast.add({
      severity: 'error',
      summary: 'Validation Error',
      detail: 'Please provide an email and select a role',
      life: 3000,
    });
    return;
  }

  try {
    await store.addMember(props.projectId, {
      email: inviteForm.value.email,
      role: inviteForm.value.role,
      createAccountIfNotExists: true,
    });

    inviteForm.value.email = '';
    inviteForm.value.role = ProjectRole.Viewer;
  } catch (error) {
    // Error handled by service
  }
}

async function handleUpdateRole(member: ProjectMemberDto) {
  try {
    await store.updateMemberRole(props.projectId, member.userId, { role: member.role });
  } catch (error) {
    // Error handled by service
  }
}

function handleRemove(member: ProjectMemberDto) {
  confirm.require({
    message: `Are you sure you want to remove ${member.userName} from this project? They will no longer have access.`,
    header: 'Remove Member',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await store.removeMember(props.projectId, member.userId);
      } catch (error) {
        // Error handled by service
      }
    },
  });
}
</script>
