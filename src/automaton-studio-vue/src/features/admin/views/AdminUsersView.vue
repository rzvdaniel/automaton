<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { FilterMatchMode } from '@primevue/core/api'
import Button from 'primevue/button'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import IconField from 'primevue/iconfield'
import InputText from 'primevue/inputtext'
import InputIcon from 'primevue/inputicon'
import Menu from 'primevue/menu'
import Paginator from 'primevue/paginator'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import type { MenuItem } from 'primevue/menuitem'
import type { DataTableRowClickEvent } from 'primevue/datatable'
import { adminUsersService } from '../services/adminUsersService'
import type { AdminUser, AdminUserSummary } from '../types/admin.types'

const toast = useToast()
const router = useRouter()

const users = ref<AdminUser[]>([])
const totalCount = ref(0)
const isLoading = ref(false)
const pageIndex = ref(0)
const pageSize = ref(100)
const sortField = ref('lastLoginAtUtc')
const sortOrder = ref(-1)
const globalSearch = ref('')
const rowActionMenu = ref()
const selectedUser = ref<AdminUser | null>(null)
const summary = ref<AdminUserSummary>({
  totalCount: 0,
  verifiedCount: 0,
  activeCount: 0,
  joinedTodayCount: 0,
  joinedThisWeekCount: 0,
  joinedThisMonthCount: 0,
})
const tableFilters = ref({
  global: { value: null as string | null, matchMode: FilterMatchMode.CONTAINS },
  emailConfirmed: { value: null as boolean | null, matchMode: FilterMatchMode.EQUALS },
  isLocked: { value: null as boolean | null, matchMode: FilterMatchMode.EQUALS },
})
let searchDebounceHandle: ReturnType<typeof setTimeout> | undefined

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

const headerStats = computed(() => {
  return [
    `Users: ${summary.value.totalCount.toLocaleString()}`,
    `Active: ${summary.value.activeCount.toLocaleString()}`,
    `Verified: ${summary.value.verifiedCount.toLocaleString()}`,
    `Joined Today: ${summary.value.joinedTodayCount.toLocaleString()}`,
    `Joined This Week: ${summary.value.joinedThisWeekCount.toLocaleString()}`,
    `Joined This Month: ${summary.value.joinedThisMonthCount.toLocaleString()}`,
  ]
})

const rowActionItems = computed<MenuItem[]>(() => {
  const user = selectedUser.value
  if (!user) {
    return []
  }

  return [
    {
      label: 'View profile',
      icon: 'pi pi-user',
      command: () => openUserDetails(user),
    },
    {
      label: 'View apps',
      icon: 'pi pi-th-large',
      command: () => router.push('/apps'),
    },
    {
      label: 'View runners',
      icon: 'pi pi-server',
      command: () => router.push('/runners'),
    },
    {
      separator: true,
    },
    {
      label: 'Resend confirmation',
      icon: 'pi pi-envelope',
      disabled: user.emailConfirmed,
      command: () => showNotImplemented('Resend confirmation'),
    },
    {
      label: user.isLocked ? 'Enable account' : 'Disable account',
      icon: user.isLocked ? 'pi pi-lock-open' : 'pi pi-ban',
      command: () => showNotImplemented(user.isLocked ? 'Enable account' : 'Disable account'),
    },
  ]
})

async function loadUsers() {
  isLoading.value = true
  try {
    const result = await adminUsersService.getUsers({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      search: globalSearch.value.trim() || undefined,
      emailConfirmed: tableFilters.value.emailConfirmed.value ?? undefined,
      isLocked: tableFilters.value.isLocked.value ?? undefined,
      sortField: sortField.value,
      sortOrder: sortOrder.value,
    })
    totalCount.value = result.totalCount
    summary.value = result.summary
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
  pageIndex.value = 0

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

function onSort(event: { sortField?: string | ((item: unknown) => string); sortOrder?: number | null }) {
  sortField.value = typeof event.sortField === 'string' ? event.sortField : 'lastLoginAtUtc'
  sortOrder.value = event.sortOrder ?? -1
  pageIndex.value = 0
  void loadUsers()
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

  const date = new Date(value)
  if (Number.isNaN(date.getTime()) || date.getUTCFullYear() <= 1) {
    return '—'
  }

  return date.toLocaleString()
}

function formatUsage(appCount: number, runnerCount: number) {
  const appLabel = `${appCount} app${appCount === 1 ? '' : 's'}`
  const runnerLabel = `${runnerCount} runner${runnerCount === 1 ? '' : 's'}`
  return `${appLabel} • ${runnerLabel}`
}

function openUserDetails(user: AdminUser) {
  void router.push({ name: 'admin-user-detail', params: { userId: user.id } })
}

function onRowClick(event: DataTableRowClickEvent) {
  openUserDetails(event.data as AdminUser)
}

function toggleRowActions(event: Event, user: AdminUser) {
  selectedUser.value = user
  rowActionMenu.value.toggle(event)
}

function showNotImplemented(actionLabel: string) {
  const userName = selectedUser.value?.name || 'this user'
  toast.add({
    severity: 'info',
    summary: 'Not implemented yet',
    detail: `${actionLabel} is not wired yet for ${userName}.`,
    life: 3000,
  })
}

function clearFilters() {
  globalSearch.value = ''
  pageIndex.value = 0
  tableFilters.value = {
    global: { value: null, matchMode: FilterMatchMode.CONTAINS },
    emailConfirmed: { value: null, matchMode: FilterMatchMode.EQUALS },
    isLocked: { value: null, matchMode: FilterMatchMode.EQUALS },
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

      <div class="users-stats-strip">
        <span
          v-for="stat in headerStats"
          :key="stat"
          class="users-stat-chip"
        >
          {{ stat }}
        </span>
      </div>
    </div>

    <DataTable
      :value="users"
      v-model:filters="tableFilters"
      :loading="isLoading"
      data-key="id"
      filter-display="menu"
      :global-filter-fields="['name', 'email', 'publicUsername']"
      :sort-field="sortField"
      :sort-order="sortOrder"
      lazy
      row-hover
      responsive-layout="scroll"
      striped-rows
      @filter="onColumnFilter"
      @row-click="onRowClick"
      @sort="onSort"
    >
      <template #header>
        <div class="users-table-toolbar">
          <IconField class="users-search">
            <InputIcon class="pi pi-search" />
            <InputText
              v-model="globalSearch"
              placeholder="Search users..."
              @input="onGlobalSearchInput"
            />
          </IconField>

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

      <Column field="name" header="Name" style="min-width: 180px" sortable>
        <template #body="slotProps">
          <span class="users-name-cell">{{ slotProps.data.name }}</span>
        </template>
      </Column>
      <Column field="email" header="Email" style="min-width: 240px" />
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
      <Column field="lastLoginAtUtc" header="Last Login" style="min-width: 180px" sortable>
        <template #body="slotProps">
          {{ formatDate(slotProps.data.lastLoginAtUtc) }}
        </template>
      </Column>
      <Column header="Usage" style="min-width: 180px">
        <template #body="slotProps">
          <span class="users-usage-cell">
            {{ formatUsage(slotProps.data.appCount, slotProps.data.runnerCount) }}
          </span>
        </template>
      </Column>
      <Column header="" style="width: 64px">
        <template #body="slotProps">
          <Button
            icon="pi pi-ellipsis-v"
            text
            rounded
            severity="secondary"
            size="small"
            @click.stop="toggleRowActions($event, slotProps.data)"
          />
        </template>
      </Column>
    </DataTable>

    <Menu ref="rowActionMenu" :model="rowActionItems" popup />

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

.users-stats-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: flex-end;
  align-items: flex-start;
  flex: 1 1 28rem;
}

.users-stat-chip {
  padding: 0.4rem 0.7rem;
  border-radius: 999px;
  background: var(--p-surface-100);
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
  font-size: 0.875rem;
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

.users-name-cell {
  font-weight: 600;
}

.users-usage-cell {
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
  white-space: nowrap;
}

.admin-users-view :deep(.p-datatable-tbody > tr) {
  cursor: pointer;
}

.admin-users-view :deep(.p-datatable-column-filter-button),
.admin-users-view :deep(.p-datatable-sort-icon) {
  color: var(--p-surface-500);
  opacity: 0.65;
}

.admin-users-view :deep(th:hover .p-datatable-column-filter-button),
.admin-users-view :deep(th:hover .p-datatable-sort-icon),
.admin-users-view :deep(.p-datatable-column-filter-button:focus),
.admin-users-view :deep(.p-datatable-column-filter-button.p-overlay-badge),
.admin-users-view :deep(.p-datatable-column-filter-button.p-highlight) {
  opacity: 1;
}

.admin-users-view :deep(.p-datatable-column-filter-button) {
  transform: scale(0.9);
}

.users-filter-select {
  min-width: 12rem;
}

.users-empty {
  padding: 1rem;
  text-align: center;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

@media (max-width: 768px) {
  .users-stats-strip,
  .users-table-toolbar,
  .users-toolbar-actions {
    flex-direction: column;
    align-items: stretch;
  }

  .users-stats-strip {
    justify-content: flex-start;
  }

  .users-filter-select {
    width: 100%;
  }
}
</style>