<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Message from 'primevue/message'
import SelectButton from 'primevue/selectbutton'
import ToggleSwitch from 'primevue/toggleswitch'
import SelfHostedRunnerInstallDialog from '../components/SelfHostedRunnerInstallDialog.vue'
import RunnerRemovalDialog from '../components/RunnerRemovalDialog.vue'
import { isAxiosError } from 'axios'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { runnersService } from '../services/runners.service'
import { useProjectsStore } from '@/features/projects/stores/projectsStore'
import {
  RunnerStatus,
  getStatusDisplayName,
  getStatusSeverity,
  type Runner,
  type RunnerRemovalInstructions,
  isRunnerLinkedToProject,
} from '../types/runners.types'
import { classifyRunner, isVisibleApplicationRunner } from '../utils/runner-classification'

const toast = useToast()
const confirm = useConfirm()
const projectsStore = useProjectsStore()
const authStore = useAuthStore()

// State
const loading = ref(true)
const runners = ref<Runner[]>([])
const showSelfHostedInstallDialog = ref(false)
const removalInstructions = ref<RunnerRemovalInstructions | null>(null)
const removalRunnerName = ref('')
const showRemovalDialog = ref(false)
const viewMode = ref<'grid' | 'list'>('grid')
const showDebugDetails = ref(false)
const autoRefreshIntervalMs = 15000
let autoRefreshTimer: number | null = null

// View toggle options
const viewOptions = [
  { icon: 'pi pi-th-large', value: 'grid' },
  { icon: 'pi pi-list', value: 'list' },
]

// Computed stats
const visibleRunners = computed(() => runners.value.filter(isVisibleApplicationRunner))
const onlineCount = computed(
  () => visibleRunners.value.filter((r) => r.status === RunnerStatus.Online).length
)
const offlineCount = computed(
  () => visibleRunners.value.filter((r) => r.status === RunnerStatus.Offline).length
)
const totalCount = computed(() => visibleRunners.value.length)
const isAdminViewer = computed(() => {
  const roles = authStore.user?.roles ?? []
  return roles.includes('Admin') || roles.includes('SysAdmin')
})

function parseUtcDate(value?: string | null) {
  if (!value) {
    return null
  }

  const normalizedValue = /z$|[+-]\d{2}:\d{2}$/i.test(value) ? value : `${value}Z`
  const date = new Date(normalizedValue)

  if (Number.isNaN(date.getTime())) {
    return null
  }

  return date
}

function formatDateTime(value?: string | null) {
  const date = parseUtcDate(value)
  if (!date) {
    return 'Not tracked yet'
  }

  return date.toLocaleString()
}

function formatRelativeTime(value?: string | null) {
  const date = parseUtcDate(value)
  if (!date) {
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

  return formatDateTime(value)
}

function formatStorage(bytes?: number | null) {
  if (bytes === undefined || bytes === null) return 'Not measured'
  const gibibytes = bytes / (1024 ** 3)
  return `${gibibytes.toFixed(gibibytes >= 10 ? 1 : 2)} GB`
}

// Get status color for grid view indicator
function getStatusColor(status: RunnerStatus): string {
  return status === RunnerStatus.Online ? 'var(--p-green-500)' : 'var(--p-red-500)'
}

function getRunnerSourceLabel(runner: Runner): string {
  switch (classifyRunner(runner)) {
    case 'machine-management':
      return 'Self-hosted · Machine Management'
    default:
      return 'Unsupported Runner profile'
  }
}

function getRunnerSourceSeverity(runner: Runner): 'contrast' | 'warn' | 'info' {
  switch (classifyRunner(runner)) {
    case 'machine-management':
      return 'warn'
    default:
      return 'info'
  }
}

async function handleSelfHostedInstallationCreated() {
  if (projectsStore.activeProjectId) {
    projectsStore.setActiveProject(null)
    toast.add({
      severity: 'info',
      summary: 'Switched to Personal Runners',
      detail: 'Your new self-hosted Runner is personal and will appear here after it connects.',
      life: 5000,
    })
    return
  }

  await refreshRunners()
}

// Load runners with optional project filter
async function loadRunners(projectId?: string | null) {
  loading.value = true
  try {
    runners.value = await runnersService.list(projectId ?? undefined)
  } catch (err: unknown) {
    console.error('Failed to load runners:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load runners',
      life: 5000,
    })
  } finally {
    loading.value = false
  }
}

async function refreshRunners(projectId?: string | null) {
  try {
    runners.value = await runnersService.list(projectId ?? undefined)
  } catch (err: unknown) {
    console.error('Failed to refresh runners:', err)
  }
}

function startAutoRefresh() {
  stopAutoRefresh()
  autoRefreshTimer = window.setInterval(() => {
    void refreshRunners(projectsStore.activeProjectId)
  }, autoRefreshIntervalMs)
}

function stopAutoRefresh() {
  if (autoRefreshTimer !== null) {
    window.clearInterval(autoRefreshTimer)
    autoRefreshTimer = null
  }
}

// Delete runner
async function deleteRunner(runner: Runner) {
  try {
    removalInstructions.value = await runnersService.prepareRemoval(runner.id)
    removalRunnerName.value = runner.name
    showRemovalDialog.value = true
    await loadRunners()
  } catch (err: unknown) {
    console.error('Failed to delete runner:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: isAxiosError(err) ? err.response?.data?.message ?? 'Failed to prepare Runner removal' : 'Failed to prepare Runner removal',
      life: 10000,
    })
  }
}

function confirmDelete(runner: Runner) {
  confirm.require({
    message: `Prepare removal of "${runner.name}"? All app environments must be moved or deleted first. New assignments will be blocked, and you will receive a command to uninstall it on its server.`,
    header: 'Delete Runner',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Prepare removal',
    acceptClass: 'p-button-danger',
    accept: () => {
      deleteRunner(runner)
    },
  })
}

// React to active project changes (including initial load)
watch(
  () => projectsStore.activeProjectId,
  async (projectId) => {
    await loadRunners(projectId)
  },
  { immediate: true },
)

startAutoRefresh()

onUnmounted(() => {
  stopAutoRefresh()
})
</script>

<template>
  <div class="runners-view">
    <!-- Page Header -->
    <div class="runners-page-header mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Runners</h1>
        <p class="text-color-secondary mt-1 mb-0">Remote execution agents for your workflows</p>
      </div>
      <div class="runners-page-header__actions">
        <Button
          label="Install Machine Management Runner"
          icon="pi pi-plus"
          @click="showSelfHostedInstallDialog = true"
        />
      </div>
    </div>

    <!-- Stats Cards -->
    <div class="grid mb-5">
      <div class="col-12 md:col-4">
        <Card class="stat-card">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div class="stat-icon stat-icon-success">
                <i class="pi pi-check-circle text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Online</div>
                <div class="text-2xl font-bold">{{ onlineCount }}</div>
              </div>
            </div>
          </template>
        </Card>
      </div>
      <div class="col-12 md:col-4">
        <Card class="stat-card">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div class="stat-icon stat-icon-danger">
                <i class="pi pi-times-circle text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Offline</div>
                <div class="text-2xl font-bold">{{ offlineCount }}</div>
              </div>
            </div>
          </template>
        </Card>
      </div>
      <div class="col-12 md:col-4">
        <Card class="stat-card">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div class="stat-icon stat-icon-info">
                <i class="pi pi-server text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Total Runners</div>
                <div class="text-2xl font-bold">{{ totalCount }}</div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <!-- Runners Section Header with View Toggle -->
    <div class="flex align-items-center justify-content-between mb-4">
      <h2 class="text-xl font-semibold m-0">Registered Runners</h2>
      <div class="flex align-items-center gap-3">
        <div v-if="isAdminViewer" class="flex align-items-center gap-2">
          <label for="runner-debug-toggle" class="text-sm text-color-secondary">Debug details</label>
          <ToggleSwitch input-id="runner-debug-toggle" v-model="showDebugDetails" />
        </div>
        <SelectButton
          v-model="viewMode"
          :options="viewOptions"
          option-value="value"
          :allow-empty="false"
          class="view-toggle"
        >
          <template #option="{ option }">
            <i :class="option.icon"></i>
          </template>
        </SelectButton>
      </div>
    </div>

    <!-- Empty State -->
    <Card v-if="!loading && visibleRunners.length === 0">
      <template #content>
        <div class="runners-empty-state">
          <i class="pi pi-server runners-empty-icon"></i>
          <h3 class="mt-0 mb-1">No runners connected</h3>
          <p class="text-color-secondary mt-0 mb-0">
            A runner is a lightweight process that executes your app's backend Python code.
            Without one, <strong>Deploy Backend</strong> will fail in the App Editor.
          </p>

          <p class="text-color-secondary text-sm mt-3 mb-0">
            <strong>Recommended:</strong> create a one-time Docker command for a self-hosted Runner
            on a machine you own. The generated command enrolls this server securely.
          </p>

          <div class="flex gap-2 mt-4 justify-content-center">
            <Button
              label="Install Machine Management Runner"
              icon="pi pi-plus"
              @click="showSelfHostedInstallDialog = true"
            />
              </div>
        </div>
      </template>
    </Card>

    <!-- Loading State -->
    <div v-else-if="loading" class="text-center py-6">
      <i class="pi pi-spin pi-spinner text-4xl text-color-secondary"></i>
      <p class="text-color-secondary mt-3">Loading runners...</p>
    </div>

    <!-- Grid View -->
    <div v-else-if="viewMode === 'grid'" class="runners-grid">
      <Card v-for="runner in visibleRunners" :key="runner.id" class="runner-card">
        <template #content>
          <div class="flex align-items-start justify-content-between">
            <div class="flex align-items-center gap-3">
              <!-- Status Indicator -->
              <div
                class="status-indicator"
                :style="{ backgroundColor: getStatusColor(runner.status) }"
              ></div>

              <!-- Runner Info -->
              <div>
                <div class="flex align-items-center gap-2 mb-1">
                  <span class="font-semibold text-lg">{{ runner.name }}</span>
                  <Tag
                    :value="getRunnerSourceLabel(runner)"
                    :severity="getRunnerSourceSeverity(runner)"
                  />
                  <Tag
                    v-if="isRunnerLinkedToProject(runner)"
                    v-tooltip.top="'Linked to a project. Manage under Project settings.'"
                    :value="`Project: ${runner.projectName}`"
                    severity="info"
                  />
                </div>
                <Tag
                  :value="getStatusDisplayName(runner.status)"
                  :severity="getStatusSeverity(runner.status)"
                  class="mt-1"
                />
                <div v-if="runner.capabilities?.length" class="text-color-secondary text-sm mt-2">
                  {{ runner.capabilities.join(' · ') }}
                </div>
              </div>
            </div>

            <!-- Actions Menu -->
            <div class="flex gap-1">
              <Button
                v-if="!runner.isHostedRunner && !isRunnerLinkedToProject(runner)"
                v-tooltip.top="'Delete runner'"
                icon="pi pi-trash"
                text
                rounded
                size="small"
                severity="danger"
                @click.stop="confirmDelete(runner)"
              />
            </div>
          </div>

          <!-- Connection ID -->
          <div v-if="showDebugDetails && isAdminViewer" class="runner-telemetry mt-3 pt-3 border-top-1 surface-border">
            <div class="runner-telemetry__row">
              <span class="text-color-secondary text-sm">Connection ID</span>
              <code v-if="runner.connectionId" class="connection-id">{{ runner.connectionId }}</code>
              <span v-else class="text-color-secondary">-</span>
            </div>
            <div class="runner-telemetry__row">
              <span class="text-color-secondary text-sm">API color</span>
              <code v-if="runner.apiColor" class="connection-id">{{ runner.apiColor }}</code>
              <span v-else class="text-color-secondary">-</span>
            </div>
            <div class="runner-telemetry__row">
              <span class="text-color-secondary text-sm">API instance</span>
              <code v-if="runner.apiInstanceId" class="connection-id">{{ runner.apiInstanceId }}</code>
              <span v-else class="text-color-secondary">-</span>
            </div>
            <div class="runner-telemetry__row">
              <span class="text-color-secondary text-sm">Session ID</span>
              <code v-if="runner.currentSessionId" class="connection-id">{{ runner.currentSessionId }}</code>
              <span v-else class="text-color-secondary">-</span>
            </div>
            <div class="runner-telemetry__row">
              <span class="text-color-secondary text-sm">Last disconnect reason</span>
              <code v-if="runner.lastDisconnectReason" class="connection-id">{{ runner.lastDisconnectReason }}</code>
              <span v-else class="text-color-secondary">-</span>
            </div>
            <div class="runner-telemetry__row">
              <span class="text-color-secondary text-sm">Current session started</span>
              <span>{{ formatDateTime(runner.currentSessionConnectedAtUtc) }}</span>
            </div>
          </div>

          <Message
            v-if="showDebugDetails && isAdminViewer && runner.apiColor && runner.status === RunnerStatus.Online && runner.apiColor !== 'unknown'"
            severity="warn"
            :closable="false"
            class="mt-3"
          >
            Verify this runner's API color matches the current live API color after deploy.
          </Message>

          <div class="runner-telemetry mt-3 pt-3 border-top-1 surface-border">
            <div class="runner-telemetry__row">
              <span class="text-color-secondary text-sm">Last heartbeat</span>
              <span>{{ formatRelativeTime(runner.lastHeartbeatAtUtc) }}</span>
            </div>
            <div class="runner-telemetry__row">
              <span class="text-color-secondary text-sm">Last connected</span>
              <span>{{ formatDateTime(runner.lastConnectedAtUtc) }}</span>
            </div>
            <div class="runner-telemetry__row">
              <span class="text-color-secondary text-sm">Version</span>
              <span>{{ runner.version || 'Unknown' }}</span>
            </div>
            <template v-if="runner.isHostedRunner">
              <div class="runner-telemetry__row">
                <span class="text-color-secondary text-sm">Storage</span>
                <span>{{ formatStorage(runner.storageTotalBytes) }} / {{ formatStorage(runner.storageAllowanceBytes) }}</span>
              </div>
              <div class="runner-telemetry__row">
                <span class="text-color-secondary text-sm">Storage state</span>
                <Tag :value="runner.storageState || 'Unknown'" :severity="runner.storageState === 'Healthy' ? 'success' : runner.storageState === 'Warning' ? 'warn' : 'danger'" />
              </div>
              <div class="runner-telemetry__row">
                <span class="text-color-secondary text-sm">Measured</span>
                <span>{{ formatRelativeTime(runner.storageMeasuredAtUtc) }} · {{ runner.storageMeasurementSource || 'Unknown source' }}</span>
              </div>
            </template>
            <template v-else-if="runner.applicationProfile === 'MachineManagement'">
              <div class="runner-telemetry__row">
                <span class="text-color-secondary text-sm">Workspace storage</span>
                <span>{{ formatStorage(runner.machineStorageAvailableBytes) }} free / {{ formatStorage(runner.machineStorageTotalBytes) }}</span>
              </div>
              <div class="runner-telemetry__row">
                <span class="text-color-secondary text-sm">Storage measurement</span>
                <Tag :value="runner.machineStorageMeasurementStatus || 'Not measured'" :severity="runner.machineStorageMeasurementStatus === 'Succeeded' ? 'success' : runner.machineStorageMeasurementStatus === 'Failed' ? 'danger' : 'secondary'" />
              </div>
              <div class="runner-telemetry__row">
                <span class="text-color-secondary text-sm">Measured</span>
                <span>{{ formatRelativeTime(runner.machineStorageMeasuredAtUtc) }}</span>
              </div>
            </template>
          </div>
          <Message v-if="runner.isHostedRunner" severity="info" :closable="false" class="mt-3">
            Hosted Runner storage uses a hard XFS project quota on supported managed Runner Hosts. It includes the Hosted Runner system and user volumes, including application data and build workspaces; Docker images, shared cache, Docker logs, and container writable layers are outside this quota.
          </Message>
          <Message v-if="runner.isHostedRunner && runner.storageMeasurementError" severity="warn" :closable="false" class="mt-2">
            {{ runner.storageMeasurementError }}
          </Message>
          <Message v-if="runner.applicationProfile === 'MachineManagement'" severity="info" :closable="false" class="mt-3">
            Storage reports free and total space on this installation's persistent workspace filesystem. It is a periodic capacity reading, not a disk quota or a measurement of every Docker volume.
          </Message>
          <Message v-if="runner.applicationProfile === 'MachineManagement' && runner.machineStorageMeasurementError" severity="warn" :closable="false" class="mt-2">
            {{ runner.machineStorageMeasurementError }}
          </Message>
        </template>
      </Card>
    </div>

    <!-- List View -->
    <Card v-else>
      <template #content>
        <DataTable
          :value="visibleRunners"
          :loading="loading"
          class="p-datatable-sm"
          :paginator="visibleRunners.length > 10"
          :rows="10"
          striped-rows
        >
          <Column field="name" header="Name" :sortable="true">
            <template #body="{ data }">
              <div class="flex align-items-center gap-2">
                <i class="pi pi-server text-color-secondary"></i>
                <span class="font-medium">{{ data.name }}</span>
                <Tag
                  :value="getRunnerSourceLabel(data)"
                  :severity="getRunnerSourceSeverity(data)"
                />
                <Tag
                  v-if="isRunnerLinkedToProject(data)"
                  v-tooltip.top="'Linked to a project. Manage under Project settings.'"
                  :value="`Project: ${data.projectName}`"
                  severity="info"
                />
              </div>
            </template>
          </Column>

          <Column field="status" header="Status" :sortable="true" style="width: 150px">
            <template #body="{ data }">
              <Tag
                :value="getStatusDisplayName(data.status)"
                :severity="getStatusSeverity(data.status)"
              />
            </template>
          </Column>

          <Column header="Capabilities" style="min-width: 280px">
            <template #body="{ data }">
              <span class="text-color-secondary text-sm">
                {{ data.capabilities?.join(' · ') || 'Default capabilities' }}
              </span>
            </template>
          </Column>

          <Column header="Last heartbeat" style="width: 180px">
            <template #body="{ data }">
              <span>{{ formatRelativeTime(data.lastHeartbeatAtUtc) }}</span>
            </template>
          </Column>

          <Column header="Last connected" style="width: 220px">
            <template #body="{ data }">
              <span>{{ formatDateTime(data.lastConnectedAtUtc) }}</span>
            </template>
          </Column>

          <Column header="Version" style="width: 140px">
            <template #body="{ data }">
              <span>{{ data.version || 'Unknown' }}</span>
            </template>
          </Column>

          <Column v-if="isAdminViewer && showDebugDetails" field="connectionId" header="Connection ID" style="width: 300px">
            <template #body="{ data }">
              <code v-if="data.connectionId" class="connection-id">
                {{ data.connectionId }}
              </code>
              <span v-else class="text-color-secondary">-</span>
            </template>
          </Column>

          <Column v-if="isAdminViewer && showDebugDetails" header="API Color" style="width: 140px">
            <template #body="{ data }">
              <code v-if="data.apiColor">{{ data.apiColor }}</code>
              <span v-else class="text-color-secondary">-</span>
            </template>
          </Column>

          <Column v-if="isAdminViewer && showDebugDetails" header="API Instance" style="width: 240px">
            <template #body="{ data }">
              <code v-if="data.apiInstanceId" class="connection-id">
                {{ data.apiInstanceId }}
              </code>
              <span v-else class="text-color-secondary">-</span>
            </template>
          </Column>

          <Column v-if="isAdminViewer && showDebugDetails" header="Session ID" style="width: 240px">
            <template #body="{ data }">
              <code v-if="data.currentSessionId" class="connection-id">
                {{ data.currentSessionId }}
              </code>
              <span v-else class="text-color-secondary">-</span>
            </template>
          </Column>

          <Column v-if="isAdminViewer && showDebugDetails" header="Disconnect Reason" style="width: 180px">
            <template #body="{ data }">
              <code v-if="data.lastDisconnectReason" class="connection-id">
                {{ data.lastDisconnectReason }}
              </code>
              <span v-else class="text-color-secondary">-</span>
            </template>
          </Column>

          <Column header="Actions" style="width: 120px">
            <template #body="{ data }">
              <div class="flex gap-1">
                <Button
                  v-if="!data.isHostedRunner && !isRunnerLinkedToProject(data)"
                  v-tooltip.top="'Delete runner'"
                  icon="pi pi-trash"
                  text
                  rounded
                  size="small"
                  severity="danger"
                  @click.stop="confirmDelete(data)"
                />
              </div>
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <SelfHostedRunnerInstallDialog
      v-model:visible="showSelfHostedInstallDialog"
      @created="handleSelfHostedInstallationCreated"
    />

    <RunnerRemovalDialog v-if="removalInstructions" :key="removalInstructions.runnerId"
      v-model:visible="showRemovalDialog" :instructions="removalInstructions"
      :runner-name="removalRunnerName" @removed="loadRunners" />


  </div>
</template>

<style scoped>
.runners-view {
  max-width: 1400px;
  margin: 0 auto;
}

.runners-page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.runners-page-header__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 0.5rem;
}

.stat-card {
  height: 100%;
}

.stat-icon {
  width: 48px;
  height: 48px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.stat-icon-success {
  background-color: var(--p-green-100);
  color: var(--p-green-600);
}

.stat-icon-danger {
  background-color: var(--p-red-100);
  color: var(--p-red-600);
}

.stat-icon-info {
  background-color: var(--p-blue-100);
  color: var(--p-blue-600);
}

:global(.app-dark) .stat-icon-success {
  background-color: color-mix(in srgb, var(--p-green-500) 20%, transparent);
}

:global(.app-dark) .stat-icon-danger {
  background-color: color-mix(in srgb, var(--p-red-500) 20%, transparent);
}

:global(.app-dark) .stat-icon-info {
  background-color: color-mix(in srgb, var(--p-blue-500) 20%, transparent);
}

/* View Toggle */
.view-toggle {
  flex-shrink: 0;
}

/* Grid View */
.runners-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 1rem;
}

.runner-card {
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.runner-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

:global(.app-dark) .runner-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
}

.status-indicator {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  flex-shrink: 0;
}

.connection-id {
  font-size: 0.8rem;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--p-surface-ground);
}

.runner-telemetry {
  display: grid;
  gap: 0.5rem;
}

.runner-telemetry__row {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  font-size: 0.95rem;
}

.code-block {
  position: relative;
  background: #1e1e1e;
  border-radius: 8px;
  padding: 16px;
  padding-right: 60px;
  margin-bottom: 16px;
}

.compact-code-block {
  margin-bottom: 0;
}

.code-content {
  color: #d4d4d4;
  margin: 0;
  white-space: pre-wrap;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  line-height: 1.5;
}

.code-content .cmd {
  color: #569cd6;
}

.code-content .env {
  color: #9cdcfe;
}

.code-content .val {
  color: #ce9178;
}

.copy-btn {
  position: absolute;
  top: 8px;
  right: 8px;
}

.config-table {
  border: 1px solid var(--p-surface-200);
  border-radius: 6px;
  overflow: hidden;
}

:global(.app-dark) .config-table {
  border-color: var(--p-surface-700);
}

.config-row {
  display: flex;
  padding: 10px 12px;
  gap: 16px;
  align-items: center;
}

.config-row:not(:last-child) {
  border-bottom: 1px solid var(--p-surface-200);
}

:global(.app-dark) .config-row:not(:last-child) {
  border-color: var(--p-surface-700);
}

.config-key {
  flex-shrink: 0;
  font-size: 0.875rem;
  background: var(--p-surface-ground);
  padding: 2px 8px;
  border-radius: 4px;
}

.config-desc {
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}

.deploy-steps {
  padding-left: 20px;
  margin: 0;
}

.deploy-steps li {
  margin-bottom: 16px;
}

.deploy-steps li strong {
  display: block;
  margin-bottom: 4px;
}

.deploy-steps li span {
  color: var(--p-text-muted-color);
  font-size: 0.9rem;
}

.env-list {
  margin-top: 8px;
  padding-left: 20px;
}

.env-list li {
  margin-bottom: 4px;
}

.env-list code {
  font-size: 0.85rem;
  background: var(--p-surface-ground);
  padding: 2px 6px;
  border-radius: 4px;
}

/* Empty state */
.runners-empty-state {
  text-align: center;
  padding: 2rem 1rem;
}

.runners-empty-icon {
  font-size: 3.5rem;
  color: var(--p-text-muted-color);
  display: block;
  margin-bottom: 1rem;
}

@media (max-width: 640px) {
  .runners-page-header {
    align-items: stretch;
    flex-direction: column;
  }

  .runners-page-header__actions {
    justify-content: stretch;
  }

  .runners-page-header__actions :deep(.p-button) {
    flex: 1 1 auto;
  }
}
</style>
