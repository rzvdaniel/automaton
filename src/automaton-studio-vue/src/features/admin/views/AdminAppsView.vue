<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import Button from 'primevue/button'
import Card from 'primevue/card'
import DataTable, { type DataTablePageEvent } from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import InputIcon from 'primevue/inputicon'
import IconField from 'primevue/iconfield'
import ToggleSwitch from 'primevue/toggleswitch'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { adminAppsService, type AdminAppListItem } from '../services/adminAppsService'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
// Only system administrators approve permissions; others can look but are not invited to review.
const isSystemAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
// Access status is stored lowercase ("active", "suspended", ...); only flag anything that is not active.
const isRestricted = (status?: string | null) => !!status && status.toLowerCase() !== 'active'
const toast = useToast()

const pageSize = 25
const isLoading = ref(false)
const apps = ref<AdminAppListItem[]>([])
const total = ref(0)
const waitingTotal = ref(0)
const first = ref(0)
const search = ref(String(route.query.search || ''))
const waitingOnly = ref(route.query.waiting === 'true')
let searchTimer: ReturnType<typeof setTimeout> | undefined

async function loadApps() {
  isLoading.value = true
  try {
    const result = await adminAppsService.listApps({
      search: search.value.trim(),
      waiting: waitingOnly.value,
      page: Math.floor(first.value / pageSize) + 1,
      pageSize,
    })
    apps.value = result.items
    total.value = result.total
    waitingTotal.value = result.waitingForApprovalTotal
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load apps', life: 3000 })
  } finally {
    isLoading.value = false
  }
}

function reload() {
  first.value = 0
  void router.replace({
    query: { search: search.value.trim() || undefined, waiting: waitingOnly.value ? 'true' : undefined },
  })
  void loadApps()
}

function onPage(event: DataTablePageEvent) {
  first.value = event.first
  void loadApps()
}

function openApp(app: AdminAppListItem) {
  void router.push({
    name: 'admin-app-detail',
    params: { appId: app.id },
    query: {
      from: 'apps',
      ownerUserId: app.ownerUserId || undefined,
      ownerUserName: app.ownerName || undefined,
      tab: isSystemAdministrator.value && app.waitingPermissions.length > 0 ? 'permissions' : undefined,
    },
  })
}

function openOwner(app: AdminAppListItem) {
  if (!app.ownerUserId) return
  void router.push({ name: 'admin-user-detail', params: { userId: app.ownerUserId } })
}

function formatDate(value?: string) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString()
}

watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(reload, 300)
})
watch(waitingOnly, reload)
// Back/forward or a link changes the query without touching the inputs; follow it.
watch(() => route.query, (query) => {
  const nextSearch = String(query.search || '')
  const nextWaiting = query.waiting === 'true'
  if (nextSearch !== search.value.trim()) search.value = nextSearch
  if (nextWaiting !== waitingOnly.value) waitingOnly.value = nextWaiting
})

onMounted(() => {
  void loadApps()
})
</script>

<template>
  <div class="admin-apps-view">
    <div class="page-header">
      <div>
        <h2 class="page-title">Apps</h2>
        <p class="page-subtitle">Every app on this server, with its owner. Open an app to inspect it or decide its permission requests.</p>
      </div>
    </div>

    <Card>
      <template #content>
        <div class="toolbar">
          <IconField class="search-field">
            <InputIcon class="pi pi-search" />
            <InputText v-model="search" placeholder="Search by app, URL or owner..." aria-label="Search apps" />
          </IconField>
          <label class="waiting-toggle">
            <ToggleSwitch v-model="waitingOnly" input-id="waiting-only" />
            <span>Waiting for approval</span>
            <Tag v-if="waitingTotal > 0" :value="String(waitingTotal)" severity="warn" rounded />
          </label>
          <Tag :value="`${total} apps`" severity="secondary" />
        </div>

        <DataTable
          :value="apps"
          data-key="id"
          lazy
          paginator
          :rows="pageSize"
          :first="first"
          :total-records="total"
          :loading="isLoading"
          responsive-layout="scroll"
          striped-rows
          @page="onPage"
        >
          <template #empty>
            <div class="empty-state">
              {{ waitingOnly ? 'No permission requests are waiting for approval.' : 'No apps found.' }}
            </div>
          </template>

          <Column header="App" style="min-width: 220px">
            <template #body="{ data }">
              <button type="button" class="link-button" @click="openApp(data)">{{ data.name }}</button>
              <div v-if="data.publicUrl" class="secondary-text">{{ data.publicUrl }}</div>
            </template>
          </Column>

          <Column header="Owner" style="min-width: 200px">
            <template #body="{ data }">
              <button v-if="data.ownerUserId" type="button" class="link-button" @click="openOwner(data)">
                {{ data.ownerName || data.ownerEmail }}
              </button>
              <span v-else>—</span>
              <div v-if="data.ownerEmail && data.ownerName" class="secondary-text">{{ data.ownerEmail }}</div>
            </template>
          </Column>

          <Column header="Status" style="width: 220px">
            <template #body="{ data }">
              <div class="tags">
                <Tag :severity="data.isPublished ? 'success' : 'secondary'" :value="data.isPublished ? 'Published' : 'Draft'" />
                <Tag v-if="isRestricted(data.accessStatus)" severity="danger" :value="data.accessStatus" />
                <Tag
                  v-for="permission in data.waitingPermissions"
                  :key="permission"
                  severity="warn"
                  :value="`${permission}: waiting`"
                />
              </div>
            </template>
          </Column>

          <Column header="Updated" style="min-width: 170px">
            <template #body="{ data }">{{ formatDate(data.updated) }}</template>
          </Column>

          <Column header="" style="width: 120px">
            <template #body="{ data }">
              <Button
                :label="isSystemAdministrator && data.waitingPermissions.length > 0 ? 'Review' : 'Open'"
                icon="pi pi-arrow-right"
                icon-pos="right"
                link
                @click="openApp(data)"
              />
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.admin-apps-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-title {
  margin: 0;
  font-size: 1.75rem;
  font-weight: 700;
}

.page-subtitle,
.secondary-text {
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

.page-subtitle {
  margin: 0.35rem 0 0;
}

.secondary-text {
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}

.search-field {
  flex: 1 1 18rem;
}

.search-field :deep(input) {
  width: 100%;
}

.waiting-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}

.link-button {
  padding: 0;
  border: none;
  background: none;
  color: var(--p-primary-color);
  font: inherit;
  font-weight: 600;
  text-align: left;
  cursor: pointer;
}

.link-button:hover {
  text-decoration: underline;
}

.empty-state {
  padding: 1.5rem;
  text-align: center;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}
</style>
