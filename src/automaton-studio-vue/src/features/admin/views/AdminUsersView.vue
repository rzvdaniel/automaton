<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import { adminUsersService } from '../services/adminUsersService'
import type { AdminUser } from '../types/admin.types'

const toast = useToast()

const users = ref<AdminUser[]>([])
const isLoading = ref(false)
const filters = ref({
  name: '',
  email: '',
})

async function loadUsers() {
  isLoading.value = true
  try {
    users.value = await adminUsersService.getUsers({
      name: filters.value.name.trim() || undefined,
      email: filters.value.email.trim() || undefined,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load users',
      life: 3000,
    })
  } finally {
    isLoading.value = false
  }
}

function formatRoles(roles: string[]) {
  if (roles.length === 0) {
    return 'No roles'
  }

  return roles.join(', ')
}

function formatDate(value?: string) {
  if (!value) {
    return '—'
  }

  return new Date(value).toLocaleString()
}

function formatProvider(value?: string) {
  switch (value) {
    case 'password':
      return 'Password'
    case 'github':
      return 'GitHub'
    case 'invitation':
      return 'Invitation'
    case 'adminCreated':
      return 'Admin'
    default:
      return value || 'Unknown'
  }
}

function providerSeverity(value?: string) {
  switch (value) {
    case 'github':
      return 'info'
    case 'invitation':
      return 'contrast'
    case 'adminCreated':
      return 'danger'
    default:
      return 'secondary'
  }
}

onMounted(async () => {
  await loadUsers()
})
</script>

<template>
  <div class="admin-users-view">
    <div class="users-header">
      <div>
        <h2 class="users-title">Users</h2>
        <p class="users-subtitle">
          Platform-wide user directory for administrators.
        </p>
      </div>

      <Button
        icon="pi pi-refresh"
        label="Reload"
        severity="secondary"
        outlined
        :loading="isLoading"
        @click="loadUsers"
      />
    </div>

    <div class="users-filters">
      <span class="p-input-icon-left">
        <i class="pi pi-user" />
        <InputText
          v-model="filters.name"
          placeholder="Filter by name"
          @keyup.enter="loadUsers"
        />
      </span>

      <span class="p-input-icon-left">
        <i class="pi pi-envelope" />
        <InputText
          v-model="filters.email"
          placeholder="Filter by email"
          @keyup.enter="loadUsers"
        />
      </span>

      <Button
        label="Apply"
        icon="pi pi-search"
        :loading="isLoading"
        @click="loadUsers"
      />
    </div>

    <DataTable
      :value="users"
      :loading="isLoading"
      data-key="id"
      responsive-layout="scroll"
      striped-rows
    >
      <template #empty>
        <div class="users-empty">No users found for the current filter.</div>
      </template>

      <Column field="name" header="Name" style="min-width: 180px" />
      <Column field="email" header="Email" style="min-width: 240px" />
      <Column field="publicUsername" header="Public Username" style="min-width: 180px">
        <template #body="slotProps">
          {{ slotProps.data.publicUsername || '—' }}
        </template>
      </Column>
      <Column header="Provider" style="width: 120px">
        <template #body="slotProps">
          <Tag
            :severity="providerSeverity(slotProps.data.registrationProvider)"
            :value="formatProvider(slotProps.data.registrationProvider)"
          />
        </template>
      </Column>
      <Column field="emailConfirmed" header="Email" style="width: 120px">
        <template #body="slotProps">
          <Tag
            :severity="slotProps.data.emailConfirmed ? 'success' : 'warn'"
            :value="slotProps.data.emailConfirmed ? 'Confirmed' : 'Pending'"
          />
        </template>
      </Column>
      <Column header="Status" style="width: 110px">
        <template #body="slotProps">
          <Tag
            :severity="slotProps.data.isLocked ? 'danger' : 'success'"
            :value="slotProps.data.isLocked ? 'Locked' : 'Active'"
          />
        </template>
      </Column>
      <Column header="Registered" style="min-width: 180px">
        <template #body="slotProps">
          {{ formatDate(slotProps.data.registeredAtUtc) }}
        </template>
      </Column>
      <Column header="Last Login" style="min-width: 180px">
        <template #body="slotProps">
          {{ formatDate(slotProps.data.lastLoginAtUtc) }}
        </template>
      </Column>
      <Column field="appCount" header="Apps" style="width: 90px" />
      <Column field="runnerCount" header="Runners" style="width: 100px" />
      <Column header="Roles" style="min-width: 220px">
        <template #body="slotProps">
          {{ formatRoles(slotProps.data.roles) }}
        </template>
      </Column>
    </DataTable>
  </div>
</template>

<style scoped>
.admin-users-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.users-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.users-title {
  margin: 0;
  font-size: 1.75rem;
  font-weight: 700;
}

.users-subtitle {
  margin: 0.35rem 0 0;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

.users-filters {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
  align-items: center;
}

.users-filters :deep(.p-inputtext) {
  width: 18rem;
}

.users-empty {
  padding: 1rem;
  text-align: center;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

@media (max-width: 768px) {
  .users-filters {
    flex-direction: column;
    align-items: stretch;
  }

  .users-filters :deep(.p-inputtext) {
    width: 100%;
  }
}
</style>