<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import SelectButton from 'primevue/selectbutton'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { runnersService } from '../services/runners.service'
import { useProjectsStore } from '@/features/projects/stores/projectsStore'
import {
  RunnerStatus,
  getStatusDisplayName,
  getStatusSeverity,
  type Runner,
  isRunnerLinkedToProject,
} from '../types/runners.types'

const toast = useToast()
const confirm = useConfirm()
const projectsStore = useProjectsStore()

// State
const loading = ref(true)
const runners = ref<Runner[]>([])
const showDeployDialog = ref(false)
const viewMode = ref<'grid' | 'list'>('grid')

// View toggle options
const viewOptions = [
  { icon: 'pi pi-th-large', value: 'grid' },
  { icon: 'pi pi-list', value: 'list' },
]

// Computed stats
const onlineCount = computed(() => runners.value.filter((r) => r.status === RunnerStatus.Online).length)
const offlineCount = computed(() => runners.value.filter((r) => r.status === RunnerStatus.Offline).length)
const totalCount = computed(() => runners.value.length)

// Get status color for grid view indicator
function getStatusColor(status: RunnerStatus): string {
  return status === RunnerStatus.Online ? 'var(--p-green-500)' : 'var(--p-red-500)'
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

// Delete runner
async function deleteRunner(runner: Runner) {
  try {
    await runnersService.delete(runner.id)
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Runner deleted successfully',
      life: 3000,
    })
    await loadRunners()
  } catch (err: unknown) {
    console.error('Failed to delete runner:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to delete runner',
      life: 5000,
    })
  }
}

function confirmDelete(runner: Runner) {
  confirm.require({
    message: `Are you sure you want to delete the runner "${runner.name}"?`,
    header: 'Delete Runner',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptClass: 'p-button-danger',
    accept: () => {
      deleteRunner(runner)
    },
  })
}

// Copy Docker command
const dockerCommand = `docker run -d \\
  --name automaton-runner \\
  --restart unless-stopped \\
  -e AUTOMATON_API_KEY=ak_your_key_here \\
  -e AUTOMATON_RUNNER_NAME=My-Runner \\
  rzvdaniel/automaton-runner:latest`

async function copyDockerCommand() {
  try {
    await navigator.clipboard.writeText(dockerCommand.replace(/\\\n/g, ''))
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'Docker command copied to clipboard',
      life: 2000,
    })
  } catch (err) {
    console.error('Failed to copy:', err)
    toast.add({
      severity: 'warn',
      summary: 'Copy Failed',
      detail: 'Could not copy to clipboard',
      life: 3000,
    })
  }
}

// React to active project changes (including initial load)
watch(
  () => projectsStore.activeProjectId,
  async (projectId) => {
    await loadRunners(projectId)
  },
  { immediate: true },
)
</script>

<template>
  <div class="runners-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Runners</h1>
        <p class="text-color-secondary mt-1 mb-0">Remote execution agents for your workflows</p>
      </div>
      <Button label="Deploy Runner" icon="pi pi-plus" @click="showDeployDialog = true" />
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

    <!-- Empty State -->
    <Card v-if="!loading && runners.length === 0">
      <template #content>
        <div class="text-center py-6">
          <i class="pi pi-server text-5xl text-color-secondary mb-3" style="display: block"></i>
          <h3 class="mt-0 mb-2">No runners registered</h3>
          <p class="text-color-secondary mt-0 mb-4">
            Deploy a runner to start executing your workflows remotely.
          </p>
          <Button label="Deploy Runner" icon="pi pi-plus" @click="showDeployDialog = true" />
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
      <Card v-for="runner in runners" :key="runner.id" class="runner-card">
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
                    v-if="isRunnerLinkedToProject(runner)"
                    :value="`Project: ${runner.projectName}`"
                    severity="info"
                    v-tooltip.top="'Linked to a project. Manage under Project settings.'"
                  />
                </div>
                <Tag
                  :value="getStatusDisplayName(runner.status)"
                  :severity="getStatusSeverity(runner.status)"
                  class="mt-1"
                />
              </div>
            </div>

            <!-- Actions Menu -->
            <Button
              v-if="!isRunnerLinkedToProject(runner)"
              v-tooltip.top="'Delete runner'"
              icon="pi pi-trash"
              text
              rounded
              size="small"
              severity="danger"
              @click.stop="confirmDelete(runner)"
            />
          </div>

          <!-- Connection ID -->
          <div v-if="runner.connectionId" class="mt-3 pt-3 border-top-1 surface-border">
            <div class="text-color-secondary text-sm mb-1">Connection ID</div>
            <code class="connection-id">{{ runner.connectionId.substring(0, 24) }}...</code>
          </div>
        </template>
      </Card>
    </div>

    <!-- List View -->
    <Card v-else>
      <template #content>
        <DataTable
          :value="runners"
          :loading="loading"
          class="p-datatable-sm"
          :paginator="runners.length > 10"
          :rows="10"
          striped-rows
        >
          <Column field="name" header="Name" :sortable="true">
            <template #body="{ data }">
              <div class="flex align-items-center gap-2">
                <i class="pi pi-server text-color-secondary"></i>
                <span class="font-medium">{{ data.name }}</span>
                <Tag
                  v-if="isRunnerLinkedToProject(data)"
                  :value="`Project: ${data.projectName}`"
                  severity="info"
                  v-tooltip.top="'Linked to a project. Manage under Project settings.'"
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

          <Column field="connectionId" header="Connection ID" style="width: 300px">
            <template #body="{ data }">
              <code v-if="data.connectionId" class="connection-id">
                {{ data.connectionId.substring(0, 20) }}...
              </code>
              <span v-else class="text-color-secondary">-</span>
            </template>
          </Column>

          <Column header="Actions" style="width: 120px">
            <template #body="{ data }">
              <div class="flex gap-1">
                <Button
                  v-if="!isRunnerLinkedToProject(data)"
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

    <!-- Deploy Runner Dialog -->
    <Dialog
      v-model:visible="showDeployDialog"
      header="Deploy a Runner"
      :modal="true"
      :style="{ width: '700px' }"
    >
      <Message severity="info" :closable="false" class="mb-4">
        You'll need an <strong>API Key</strong> with Runner scope to deploy a runner.
        <router-link to="/settings" class="ml-1">Go to Settings</router-link> to create one.
      </Message>

      <TabView>
        <TabPanel header="Docker CLI" value="docker-cli">
          <div class="py-3">
            <p class="mt-0 mb-3"><strong>Run this command in your terminal:</strong></p>

            <div class="code-block">
              <pre class="code-content"><span class="cmd">docker run</span> -d \
  --name automaton-runner \
  --restart unless-stopped \
  -e <span class="env">AUTOMATON_API_KEY</span>=<span class="val">ak_your_key_here</span> \
  -e <span class="env">AUTOMATON_RUNNER_NAME</span>=<span class="val">My-Runner</span> \
  rzvdaniel/automaton-runner:latest</pre>
              <Button
                icon="pi pi-copy"
                size="small"
                class="copy-btn"
                @click="copyDockerCommand"
              />
            </div>

            <h4 class="mb-2">Configuration</h4>
            <div class="config-table">
              <div class="config-row">
                <code class="config-key">AUTOMATON_API_KEY</code>
                <span class="config-desc">Required. Get this from Settings &gt; API Keys</span>
              </div>
              <div class="config-row">
                <code class="config-key">AUTOMATON_RUNNER_NAME</code>
                <span class="config-desc">Display name shown in Studio (optional)</span>
              </div>
            </div>

            <Message severity="success" :closable="false" class="mt-4">
              Once the container starts, your runner will appear in the list above.
            </Message>
          </div>
        </TabPanel>

        <TabPanel header="Docker Desktop" value="docker-desktop">
          <div class="py-3">
            <p class="mt-0 mb-3 text-color-secondary">
              If you prefer Docker Desktop's graphical interface:
            </p>

            <ol class="deploy-steps">
              <li>
                <strong>Open Docker Desktop</strong>
                <span>Make sure Docker Desktop is running</span>
              </li>
              <li>
                <strong>Search for the image</strong>
                <span>Go to Images and search for <code>rzvdaniel/automaton-runner</code></span>
              </li>
              <li>
                <strong>Pull the image</strong>
                <span>Click "Pull" to download the latest version</span>
              </li>
              <li>
                <strong>Run the container</strong>
                <span>Click "Run" and add these environment variables:</span>
                <ul class="env-list">
                  <li><code>AUTOMATON_API_KEY=YOUR_API_KEY</code></li>
                  <li><code>AUTOMATON_RUNNER_NAME=My-Runner</code> (optional)</li>
                </ul>
              </li>
            </ol>

            <Message severity="info" :closable="false" class="mt-3">
              <strong>Tip:</strong> Set restart policy to "Unless stopped" so the runner auto-starts with Docker Desktop.
            </Message>
          </div>
        </TabPanel>

        <TabPanel header="Manual Download" value="manual-download">
          <div class="py-3">
            <p class="mt-0 mb-3 text-color-secondary">
              For advanced users who need to run the runner as a Windows service, on systems without Docker,
              or with custom configurations.
            </p>
            <p class="text-color-secondary">
              Download the standalone executable from the Downloads page.
            </p>
          </div>
        </TabPanel>
      </TabView>

      <template #footer>
        <Button label="Close" text @click="showDeployDialog = false" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.runners-view {
  max-width: 1400px;
  margin: 0 auto;
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

.code-block {
  position: relative;
  background: #1e1e1e;
  border-radius: 8px;
  padding: 16px;
  padding-right: 60px;
  margin-bottom: 16px;
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
</style>
