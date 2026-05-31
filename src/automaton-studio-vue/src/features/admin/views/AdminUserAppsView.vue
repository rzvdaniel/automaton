<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import InputIcon from 'primevue/inputicon'
import IconField from 'primevue/iconfield'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { adminUsersService } from '../services/adminUsersService'
import type { App } from '@/features/apps/types/apps.types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const isLoading = ref(false)
const apps = ref<App[]>([])
const search = ref('')

const userId = computed(() => String(route.params.userId || ''))
const userName = computed(() => String(route.query.userName || 'User'))

const filteredApps = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (!query) {
    return apps.value
  }

  return apps.value.filter((app) =>
    app.name.toLowerCase().includes(query) ||
    (app.description || '').toLowerCase().includes(query) ||
    (app.summary || '').toLowerCase().includes(query) ||
    (app.publicUrl || '').toLowerCase().includes(query),
  )
})

async function loadApps() {
  if (!userId.value) {
    return
  }

  isLoading.value = true
  try {
    apps.value = await adminUsersService.getUserApps(userId.value)
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load user apps',
      life: 3000,
    })
  } finally {
    isLoading.value = false
  }
}

function goBack() {
  void router.push({ name: 'admin-user-detail', params: { userId: userId.value } })
}

function openApp(appId: string) {
  void router.push({
    name: 'admin-app-detail',
    params: { appId },
    query: {
      ownerUserId: userId.value,
      ownerUserName: userName.value,
    },
  })
}

function formatDate(value?: string) {
  if (!value) {
    return '—'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }

  return date.toLocaleString()
}

onMounted(() => {
  void loadApps()
})
</script>

<template>
  <div class="admin-user-apps-view">
    <div class="page-header">
      <div>
        <div class="breadcrumb-row">
          <Button label="Users" link class="breadcrumb-link" @click="router.push({ name: 'admin-users' })" />
          <span class="breadcrumb-separator">/</span>
          <Button label="User Details" link class="breadcrumb-link" @click="goBack" />
          <span class="breadcrumb-separator">/</span>
          <span class="breadcrumb-current">Apps</span>
        </div>
        <h2 class="page-title">{{ userName }}'s Apps</h2>
        <p class="page-subtitle">Read-only admin view of owned applications.</p>
      </div>

      <Button icon="pi pi-arrow-left" label="Back to User" outlined @click="goBack" />
    </div>

    <Card>
      <template #content>
        <div class="toolbar">
          <IconField class="search-field">
            <InputIcon class="pi pi-search" />
            <InputText v-model="search" placeholder="Search apps..." />
          </IconField>
          <Tag :value="`${filteredApps.length} apps`" severity="secondary" />
        </div>

        <div v-if="isLoading" class="loading-state">
          <ProgressSpinner style="width: 40px; height: 40px" />
        </div>

        <DataTable
          v-else
          :value="filteredApps"
          data-key="id"
          responsive-layout="scroll"
          striped-rows
          :paginator="filteredApps.length > 10"
          :rows="10"
        >
          <template #empty>
            <div class="empty-state">No apps found for this user.</div>
          </template>

          <Column field="name" header="App" sortable style="min-width: 220px">
            <template #body="{ data }">
              <button type="button" class="link-button" @click="openApp(data.id)">
                {{ data.name }}
              </button>
            </template>
          </Column>

          <Column header="Status" style="width: 140px">
            <template #body="{ data }">
              <Tag :severity="data.isPublished ? 'success' : 'secondary'" :value="data.isPublished ? 'Published' : 'Draft'" />
            </template>
          </Column>

          <Column field="publicUrl" header="Public URL" style="min-width: 180px">
            <template #body="{ data }">
              {{ data.publicUrl || '—' }}
            </template>
          </Column>

          <Column field="updated" header="Updated" sortable style="min-width: 180px">
            <template #body="{ data }">
              {{ formatDate(data.updated) }}
            </template>
          </Column>

          <Column header="Actions" style="width: 140px">
            <template #body="{ data }">
              <Button label="Open" icon="pi pi-arrow-right" link @click="openApp(data.id)" />
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.admin-user-apps-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
  flex-wrap: wrap;
}

.breadcrumb-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  margin-bottom: 0.5rem;
}

.breadcrumb-link {
  padding: 0;
}

.breadcrumb-separator,
.breadcrumb-current,
.page-subtitle {
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

.page-title {
  margin: 0;
  font-size: 1.75rem;
  font-weight: 700;
}

.page-subtitle {
  margin: 0.35rem 0 0;
}

.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}

.search-field {
  flex: 1 1 18rem;
}

.search-field :deep(.p-inputtext) {
  width: 100%;
}

.loading-state,
.empty-state {
  display: flex;
  justify-content: center;
  padding: 2rem 0;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

.link-button {
  border: 0;
  background: transparent;
  color: var(--primary-color);
  padding: 0;
  cursor: pointer;
  font: inherit;
  font-weight: 600;
}

.link-button:hover {
  text-decoration: underline;
}
</style>
