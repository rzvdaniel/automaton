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
import type { Runner } from '@/features/runners/types/runners.types'
import { getStatusSeverity } from '@/features/runners/types/runners.types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const isLoading = ref(false)
const runners = ref<Runner[]>([])
const search = ref('')

const userId = computed(() => String(route.params.userId || ''))
const userName = computed(() => String(route.query.userName || 'User'))

const filteredRunners = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (!query) {
    return runners.value
  }

  return runners.value.filter((runner) =>
    runner.name.toLowerCase().includes(query) ||
    (runner.version || '').toLowerCase().includes(query) ||
    (runner.projectName || '').toLowerCase().includes(query),
  )
})

async function loadRunners() {
  if (!userId.value) {
    return
  }

  isLoading.value = true
  try {
    runners.value = await adminUsersService.getUserRunners(userId.value)
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load user runners',
      life: 3000,
    })
  } finally {
    isLoading.value = false
  }
}

function goBack() {
  void router.push({ name: 'admin-user-detail', params: { userId: userId.value } })
}

function formatDate(value?: string | null) {
  if (!value) {
    return '—'
  }

  const date = new Date(/z$|[+-]\d{2}:\d{2}$/i.test(value) ? value : `${value}Z`)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }

  return date.toLocaleString()
}

function formatLastHeartbeat(value?: string | null) {
  if (!value) {
    return 'Not tracked yet'
  }

  const date = new Date(/z$|[+-]\d{2}:\d{2}$/i.test(value) ? value : `${value}Z`)
  if (Number.isNaN(date.getTime())) {
    return 'Not tracked yet'
  }

  const diffSeconds = Math.max(0, Math.floor((Date.now() - date.getTime()) / 1000))
  if (diffSeconds < 60) {
    return `${diffSeconds} second${diffSeconds === 1 ? '' : 's'} ago`
  }

  const diffMinutes = Math.floor(diffSeconds / 60)
  if (diffMinutes < 60) {
    return `${diffMinutes} minute${diffMinutes === 1 ? '' : 's'} ago`
  }

  const diffHours = Math.floor(diffMinutes / 60)
  if (diffHours < 24) {
    return `${diffHours} hour${diffHours === 1 ? '' : 's'} ago`
  }

  return formatDate(value)
}

onMounted(() => {
  void loadRunners()
})
</script>

<template>
  <div class="admin-user-runners-view">
    <div class="page-header">
      <div>
        <div class="breadcrumb-row">
          <Button label="Users" link class="breadcrumb-link" @click="router.push({ name: 'admin-users' })" />
          <span class="breadcrumb-separator">/</span>
          <Button label="User Details" link class="breadcrumb-link" @click="goBack" />
          <span class="breadcrumb-separator">/</span>
          <span class="breadcrumb-current">Runners</span>
        </div>
        <h2 class="page-title">{{ userName }}'s Runners</h2>
        <p class="page-subtitle">Read-only admin view of runner status and telemetry.</p>
      </div>

      <Button icon="pi pi-arrow-left" label="Back to User" outlined @click="goBack" />
    </div>

    <Card>
      <template #content>
        <div class="toolbar">
          <IconField class="search-field">
            <InputIcon class="pi pi-search" />
            <InputText v-model="search" placeholder="Search runners..." />
          </IconField>
          <Tag :value="`${filteredRunners.length} runners`" severity="secondary" />
        </div>

        <div v-if="isLoading" class="loading-state">
          <ProgressSpinner style="width: 40px; height: 40px" />
        </div>

        <DataTable
          v-else
          :value="filteredRunners"
          data-key="id"
          responsive-layout="scroll"
          striped-rows
          :paginator="filteredRunners.length > 10"
          :rows="10"
        >
          <template #empty>
            <div class="empty-state">No runners found for this user.</div>
          </template>

          <Column field="name" header="Runner" sortable style="min-width: 220px" />

          <Column header="Status" style="width: 140px">
            <template #body="{ data }">
              <Tag :severity="getStatusSeverity(data.status)" :value="data.status" />
            </template>
          </Column>

          <Column header="Last Heartbeat" style="min-width: 170px">
            <template #body="{ data }">
              {{ formatLastHeartbeat(data.lastHeartbeatAtUtc) }}
            </template>
          </Column>

          <Column header="Last Connected" style="min-width: 180px">
            <template #body="{ data }">
              {{ formatDate(data.lastConnectedAtUtc) }}
            </template>
          </Column>

          <Column field="version" header="Version" style="width: 140px">
            <template #body="{ data }">
              {{ data.version || 'Unknown' }}
            </template>
          </Column>

          <Column field="projectName" header="Project" style="min-width: 180px">
            <template #body="{ data }">
              {{ data.projectName || 'Personal' }}
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.admin-user-runners-view {
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
</style>
