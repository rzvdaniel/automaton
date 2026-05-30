<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { FilterMatchMode } from '@primevue/core/api'
import Button from 'primevue/button'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import DatePicker from 'primevue/datepicker'
import InputText from 'primevue/inputtext'
import Paginator from 'primevue/paginator'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { adminUsersService } from '../services/adminUsersService'
import type { AdminUser } from '../types/admin.types'

const toast = useToast()

const users = ref<AdminUser[]>([])
const totalCount = ref(0)
const isLoading = ref(false)
const pageIndex = ref(0)
const pageSize = ref(100)
const globalSearch = ref('')
const tableFilters = ref({
  global: { value: null as string | null, matchMode: FilterMatchMode.CONTAINS },
  registrationProvider: { value: null as string | null, matchMode: FilterMatchMode.EQUALS },
  emailConfirmed: { value: null as boolean | null, matchMode: FilterMatchMode.EQUALS },
  isLocked: { value: null as boolean | null, matchMode: FilterMatchMode.EQUALS },
  registeredAtUtc: {
    value: null as [Date | null, Date | null] | null,
    matchMode: FilterMatchMode.BETWEEN,
  },
})
let searchDebounceHandle: ReturnType<typeof setTimeout> | undefined

const providerOptions = [
  { label: 'All providers', value: null },
  { label: 'Password', value: 'password' },
  { label: 'GitHub', value: 'github' },
  { label: 'Invitation', value: 'invitation' },
  { label: 'Admin', value: 'adminCreated' },
]

const emailStatusOptions = [
  { label: 'All email states', value: null },
  { label: 'Confirmed', value: true },
  { label: 'Pending', value: false },
]

const lockStatusOptions = [
  { label: 'All statuses', value: null },
  { label: 'Active', value: false },
  { label: 'Locked', value: true },
]

async function loadUsers() {
  isLoading.value = true
  try {
    const registeredRange = tableFilters.value.registeredAtUtc.value
    const registeredFrom = registeredRange?.[0] ?? null
    const registeredTo = registeredRange?.[1] ?? null

    const result = await adminUsersService.getUsers({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      search: globalSearch.value.trim() || undefined,
      registrationProvider: tableFilters.value.registrationProvider.value || undefined,
      emailConfirmed: tableFilters.value.emailConfirmed.value ?? undefined,
      isLocked: tableFilters.value.isLocked.value ?? undefined,
      registeredFromUtc: registeredFrom ? toStartOfDayIso(registeredFrom) : undefined,
      registeredToUtc: registeredTo ? toEndOfDayIso(registeredTo) : undefined,
    })
    totalCount.value = result.totalCount
    users.value = result.items
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

function onGlobalSearchInput() {
  tableFilters.value.global.value = globalSearch.value || null

  if (searchDebounceHandle) {
    clearTimeout(searchDebounceHandle)
  }

  searchDebounceHandle = setTimeout(() => {
    void loadUsers()
  }, 250)
}

function onColumnFilter() {
  pageIndex.value = 0
  void loadUsers()
}

function onPageChange(event: { page: number; rows: number }) {
  pageIndex.value = event.page
  pageSize.value = event.rows
  void loadUsers()
}

function toStartOfDayIso(date: Date) {
  const normalized = new Date(date)
  normalized.setHours(0, 0, 0, 0)
  return normalized.toISOString()
}

function toEndOfDayIso(date: Date) {
  const normalized = new Date(date)
  normalized.setHours(23, 59, 59, 999)
  return normalized.toISOString()
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

function clearFilters() {
  globalSearch.value = ''
  pageIndex.value = 0
  tableFilters.value = {
    global: { value: null, matchMode: FilterMatchMode.CONTAINS },
    registrationProvider: { value: null, matchMode: FilterMatchMode.EQUALS },
    emailConfirmed: { value: null, matchMode: FilterMatchMode.EQUALS },
    isLocked: { value: null, matchMode: FilterMatchMode.EQUALS },
    registeredAtUtc: { value: null, matchMode: FilterMatchMode.BETWEEN },
  }

  void loadUsers()
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

    <DataTable
      :value="users"
      v-model:filters="tableFilters"
      :loading="isLoading"
      data-key="id"
      filter-display="menu"
      :global-filter-fields="['name', 'email', 'publicUsername']"
      lazy
      responsive-layout="scroll"
      striped-rows
      @filter="onColumnFilter"
    >
      <template #header>
        <div class="users-table-toolbar">
          <span class="p-input-icon-left users-search">
            <i class="pi pi-search" />
            <InputText
              v-model="globalSearch"
              placeholder="Search users..."
              @input="onGlobalSearchInput"
            />
          </span>

          <div class="users-toolbar-actions">
            <span class="users-count">{{ users.length.toLocaleString() }} of {{ totalCount.toLocaleString() }} users</span>
            <Button
              label="Clear"
              icon="pi pi-times"
              severity="secondary"
              outlined
              :disabled="isLoading"
              @click="clearFilters"
            />
            <Button
              icon="pi pi-refresh"
              label="Reload"
              severity="secondary"
              outlined
              :loading="isLoading"
              @click="loadUsers"
            />
          </div>
        </div>
      </template>

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
      <Column
        field="registrationProvider"
        header="Provider"
        style="width: 120px"
        :show-filter-match-modes="false"
      >
        <template #filter="slotProps">
          <Select
            v-model="slotProps.filterModel.value"
            :options="providerOptions"
            option-label="label"
            option-value="value"
            placeholder="Any"
            class="users-filter-select"
            @change="slotProps.filterCallback()"
          />
        </template>
        <template #body="slotProps">
          <Tag
            :severity="providerSeverity(slotProps.data.registrationProvider)"
            :value="formatProvider(slotProps.data.registrationProvider)"
          />
        </template>
      </Column>
      <Column
        field="emailConfirmed"
        header="Verified"
        style="width: 120px"
        :show-filter-match-modes="false"
      >
        <template #filter="slotProps">
          <Select
            v-model="slotProps.filterModel.value"
            :options="emailStatusOptions"
            option-label="label"
            option-value="value"
            placeholder="Any"
            class="users-filter-select"
            @change="slotProps.filterCallback()"
          />
        </template>
        <template #body="slotProps">
          <Tag
            :severity="slotProps.data.emailConfirmed ? 'success' : 'warn'"
            :value="slotProps.data.emailConfirmed ? 'Confirmed' : 'Pending'"
          />
        </template>
      </Column>
      <Column
        field="isLocked"
        header="Status"
        style="width: 110px"
        :show-filter-match-modes="false"
      >
        <template #filter="slotProps">
          <Select
            v-model="slotProps.filterModel.value"
            :options="lockStatusOptions"
            option-label="label"
            option-value="value"
            placeholder="Any"
            class="users-filter-select"
            @change="slotProps.filterCallback()"
          />
        </template>
        <template #body="slotProps">
          <Tag
            :severity="slotProps.data.isLocked ? 'danger' : 'success'"
            :value="slotProps.data.isLocked ? 'Locked' : 'Active'"
          />
        </template>
      </Column>
      <Column
        field="registeredAtUtc"
        header="Registered"
        style="min-width: 180px"
        :show-filter-match-modes="false"
      >
        <template #filter="slotProps">
          <DatePicker
            v-model="slotProps.filterModel.value"
            selection-mode="range"
            :manual-input="false"
            placeholder="Pick range"
            show-icon
            class="users-filter-date"
            @date-select="slotProps.filterCallback()"
            @clear-click="slotProps.filterCallback()"
          />
        </template>
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

    <Paginator
      :first="pageIndex * pageSize"
      :rows="pageSize"
      :total-records="totalCount"
      :rows-per-page-options="[25, 50, 100, 250]"
      template="FirstPageLink PrevPageLink PageLinks NextPageLink LastPageLink RowsPerPageDropdown CurrentPageReport"
      current-page-report-template="Showing {first} to {last} of {totalRecords} users"
      @page="onPageChange"
    />
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

.users-table-toolbar {
  display: flex;
  justify-content: space-between;
  gap: 0.75rem;
  align-items: center;
  flex-wrap: wrap;
}

.users-search {
  flex: 1 1 20rem;
}

.users-search :deep(.p-inputtext) {
  width: 100%;
}

.users-toolbar-actions {
  display: flex;
  gap: 0.75rem;
  align-items: center;
  justify-content: flex-end;
}

.users-count {
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
  font-size: 0.875rem;
  white-space: nowrap;
}

.users-filter-select,
.users-filter-date {
  min-width: 12rem;
}

.users-empty {
  padding: 1rem;
  text-align: center;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

@media (max-width: 768px) {
  .users-table-toolbar,
  .users-toolbar-actions {
    flex-direction: column;
    align-items: stretch;
  }

  .users-filter-select,
  .users-filter-date {
    width: 100%;
  }
}
</style>