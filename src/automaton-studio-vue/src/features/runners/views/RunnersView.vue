<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
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
import ToggleSwitch from 'primevue/toggleswitch'
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
const showDebugDetails = ref(false)
const restartingRunnerId = ref<string | null>(null)
const hostedRestartPollMaxAttempts = 8
const hostedRestartPollDelayMs = 1500
const autoRefreshIntervalMs = 15000
let autoRefreshTimer: number | null = null

// View toggle options
const viewOptions = [
  { icon: 'pi pi-th-large', value: 'grid' },
  { icon: 'pi pi-list', value: 'list' },
]

// Computed stats
const onlineCount = computed(() => runners.value.filter((r) => r.status === RunnerStatus.Online).length)
const offlineCount = computed(() => runners.value.filter((r) => r.status === RunnerStatus.Offline).length)
const totalCount = computed(() => runners.value.length)

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

function delay(ms: number) {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

async function pollRunnerUntilOnline(runnerId: string, projectId?: string | null) {
  for (let attempt = 0; attempt < hostedRestartPollMaxAttempts; attempt += 1) {
    await delay(hostedRestartPollDelayMs)

    const latestRunners = await runnersService.list(projectId ?? undefined)
    runners.value = latestRunners

    if (latestRunners.some((candidate) => candidate.id === runnerId && candidate.status === RunnerStatus.Online)) {
      return true
    }
  }

  return false
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

async function restartHostedRunner(runner: Runner) {
  restartingRunnerId.value = runner.id
  try {
    await runnersService.restartHostedRunner(runner.id)
    toast.add({
      severity: 'info',
      summary: 'Restart requested',
      detail: `Restart requested for ${runner.name}. Waiting for it to reconnect...`,
      life: 3500,
    })

    const isOnline = await pollRunnerUntilOnline(runner.id, projectsStore.activeProjectId)

    if (isOnline) {
      toast.add({
        severity: 'success',
        summary: 'Runner online',
        detail: `${runner.name} is back online.`,
        life: 3000,
      })
    } else {
      toast.add({
        severity: 'warn',
        summary: 'Still reconnecting',
        detail: `${runner.name} did not come back online yet. It may still be restarting.`,
        life: 5000,
      })
    }
  } catch (err: unknown) {
    console.error('Failed to restart hosted runner:', err)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to restart hosted runner',
      life: 5000,
    })
  } finally {
    restartingRunnerId.value = null
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

const cliCommand = `dotnet tool install --global Automaton.Cli
automaton login
automaton runner install`

const dockerCommand = `docker run -d \\
  --name automaton-runner \\
  --restart unless-stopped \\
  -e AUTOMATON_API_KEY=ak_your_key_here \\
  -e AUTOMATON_RUNNER_NAME=My-Runner \\
  rzvdaniel/automaton-runner:latest`

const composeCommand = `services:
  automaton-runner:
    image: rzvdaniel/automaton-runner:latest
    container_name: automaton-runner
    restart: unless-stopped
    environment:
      AUTOMATON_API_KEY: ak_your_key_here
      AUTOMATON_RUNNER_NAME: My-Runner`

async function copyText(text: string, detail: string) {
  try {
    await navigator.clipboard.writeText(text)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail,
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

function copyCliCommand() {
  void copyText(cliCommand, 'CLI commands copied to clipboard')
}

function copyDockerCommand() {
  void copyText(dockerCommand.replace(/\\\n/g, ''), 'Docker command copied to clipboard')
}

function copyComposeCommand() {
  void copyText(composeCommand, 'Docker Compose file copied to clipboard')
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
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Runners</h1>
        <p class="text-color-secondary mt-1 mb-0">Remote execution agents for your workflows</p>
      </div>
      <Button label="Install Runner" icon="pi pi-plus" @click="showDeployDialog = true" />
    </div>

    <Message severity="warn" :closable="false" class="mb-4">
      <strong>Experimental Infrastructure:</strong> hosted runners and backend deployments are
      still evolving. Unexpected disconnects or reconnect delays may occur during redeployments.
    </Message>

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
        <div class="flex align-items-center gap-2">
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
    <Card v-if="!loading && runners.length === 0">
      <template #content>
        <div class="runners-empty-state">
          <i class="pi pi-server runners-empty-icon"></i>
          <h3 class="mt-0 mb-1">No runners connected</h3>
          <p class="text-color-secondary mt-0 mb-0">
            A runner is a lightweight process that executes your app's backend Python code.
            Without one, <strong>Deploy Backend</strong> will fail in the App Editor.
          </p>

          <!-- CLI quick-start -->
          <div class="runner-quickstart mt-4">
            <div class="runner-quickstart-label">Recommended setup — Automaton CLI:</div>
            <div class="runner-quickstart-code">
              <span class="code-line">dotnet tool install --global Automaton.Cli</span>
              <span class="code-line">automaton login</span>
              <span class="code-line">automaton runner install</span>
            </div>
            <p class="text-color-secondary text-sm mt-2 mb-0">
              Requires Docker to be installed and running. Get a runner API key from
              <router-link to="/apikeys" class="runner-link">API Keys</router-link>.
              Docker and Docker Compose options are also available from <strong>Install Runner</strong>.
            </p>
          </div>

          <div class="flex gap-2 mt-4 justify-content-center">
            <Button label="Install Runner" icon="pi pi-plus" @click="showDeployDialog = true" />
            <Button
              label="API Keys"
              icon="pi pi-key"
              severity="secondary"
              outlined
              @click="$router.push('/apikeys')"
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
                  <Tag v-if="runner.isHostedRunner" value="Hosted" severity="contrast" />
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
              </div>
            </div>

            <!-- Actions Menu -->
            <div class="flex gap-1">
              <Button
                v-if="runner.isHostedRunner"
                v-tooltip.top="'Restart hosted runner'"
                icon="pi pi-refresh"
                text
                rounded
                size="small"
                :loading="restartingRunnerId === runner.id"
                @click.stop="restartHostedRunner(runner)"
              />
              <Button
                v-else-if="!isRunnerLinkedToProject(runner)"
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
          <div v-if="runner.connectionId" class="mt-3 pt-3 border-top-1 surface-border">
            <div class="text-color-secondary text-sm mb-1">Connection ID</div>
            <code class="connection-id">{{ runner.connectionId.substring(0, 24) }}...</code>
          </div>

          <div v-if="showDebugDetails" class="runner-telemetry mt-3 pt-3 border-top-1 surface-border">
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
          </div>

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
                <Tag v-if="data.isHostedRunner" value="Hosted" severity="contrast" />
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

          <Column field="connectionId" header="Connection ID" style="width: 300px">
            <template #body="{ data }">
              <code v-if="data.connectionId" class="connection-id">
                {{ data.connectionId.substring(0, 20) }}...
              </code>
              <span v-else class="text-color-secondary">-</span>
            </template>
          </Column>

          <Column v-if="showDebugDetails" header="API Color" style="width: 140px">
            <template #body="{ data }">
              <code v-if="data.apiColor">{{ data.apiColor }}</code>
              <span v-else class="text-color-secondary">-</span>
            </template>
          </Column>

          <Column v-if="showDebugDetails" header="API Instance" style="width: 240px">
            <template #body="{ data }">
              <code v-if="data.apiInstanceId" class="connection-id">
                {{ data.apiInstanceId.substring(0, 16) }}...
              </code>
              <span v-else class="text-color-secondary">-</span>
            </template>
          </Column>

          <Column header="Actions" style="width: 120px">
            <template #body="{ data }">
              <div class="flex gap-1">
                <Button
                  v-if="data.isHostedRunner"
                  v-tooltip.top="'Restart hosted runner'"
                  icon="pi pi-refresh"
                  text
                  rounded
                  size="small"
                  :loading="restartingRunnerId === data.id"
                  @click.stop="restartHostedRunner(data)"
                />
                <Button
                  v-else-if="!isRunnerLinkedToProject(data)"
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
      header="Deploy a runner"
      :modal="true"
      :style="{ width: '760px' }"
    >
      <p class="text-color-secondary mt-0 mb-4">
        A runner connects your machine or server to Automaton and executes backend code for your apps.
        Use the CLI for the easiest setup, or Docker if you prefer to manage the container yourself.
      </p>

      <Message severity="info" :closable="false" class="mb-4">
        You'll need an <strong>API Key</strong> with Runner scope.
        <router-link to="/apikeys" class="ml-1">Go to API Keys</router-link> to create one.
      </Message>

      <TabView>
        <TabPanel header="CLI install" value="cli-install">
          <div class="py-3">
            <Message severity="success" :closable="false" class="mb-3">
              <strong>Recommended.</strong> The Automaton CLI installs and configures the Docker runner container for you.
            </Message>

            <p class="mt-0 mb-3"><strong>Run these commands in your terminal:</strong></p>

            <div class="code-block">
              <pre class="code-content"><span class="cmd">dotnet tool install</span> --global Automaton.Cli
<span class="cmd">automaton login</span>
<span class="cmd">automaton runner install</span></pre>
              <Button
                icon="pi pi-copy"
                size="small"
                class="copy-btn"
                @click="copyCliCommand"
              />
            </div>

            <h4 class="mb-2">Requirements</h4>
            <div class="config-table">
              <div class="config-row">
                <code class="config-key">.NET SDK</code>
                <span class="config-desc">Required to install the Automaton CLI global tool</span>
              </div>
              <div class="config-row">
                <code class="config-key">Docker</code>
                <span class="config-desc">Required and must be running before installing the runner</span>
              </div>
              <div class="config-row">
                <code class="config-key">API Key</code>
                <span class="config-desc">Create a Runner-scoped key from API Keys</span>
              </div>
            </div>
          </div>
        </TabPanel>

        <TabPanel header="Docker install" value="docker-install">
          <div class="py-3">
            <p class="mt-0 mb-3"><strong>Run the runner container directly:</strong></p>

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
                <span class="config-desc">Required. Get this from API Keys</span>
              </div>
              <div class="config-row">
                <code class="config-key">AUTOMATON_RUNNER_NAME</code>
                <span class="config-desc">Optional display name shown in Studio</span>
              </div>
            </div>

            <Message severity="info" :closable="false" class="mt-4">
              By default, the runner connects to <code>https://api.automaton.monster</code>.
              Set <code>AUTOMATON_API_URL</code> only if you use a custom or self-hosted Automaton API.
            </Message>
          </div>
        </TabPanel>

        <TabPanel header="Docker Compose" value="docker-compose">
          <div class="py-3">
            <p class="mt-0 mb-3"><strong>Create a docker-compose.yml file:</strong></p>

            <div class="code-block">
              <pre class="code-content"><span class="env">services</span>:
  automaton-runner:
    image: rzvdaniel/automaton-runner:latest
    container_name: automaton-runner
    restart: unless-stopped
    environment:
      <span class="env">AUTOMATON_API_KEY</span>: <span class="val">ak_your_key_here</span>
      <span class="env">AUTOMATON_RUNNER_NAME</span>: <span class="val">My-Runner</span></pre>
              <Button
                icon="pi pi-copy"
                size="small"
                class="copy-btn"
                @click="copyComposeCommand"
              />
            </div>

            <p class="mt-0 mb-3"><strong>Start the runner:</strong></p>
            <div class="code-block compact-code-block">
              <pre class="code-content"><span class="cmd">docker compose up</span> -d</pre>
            </div>

            <Message severity="info" :closable="false" class="mt-4">
              Docker Compose is a good option for VPS or server installations because the runner restarts automatically after reboot.
            </Message>
          </div>
        </TabPanel>

        <TabPanel header="Troubleshooting" value="troubleshooting">
          <div class="py-3">
            <p class="mt-0 mb-3"><strong>Useful commands:</strong></p>

            <div class="code-block compact-code-block">
              <pre class="code-content"><span class="cmd">docker ps</span>
<span class="cmd">docker logs</span> -f automaton-runner
<span class="cmd">automaton --version</span></pre>
            </div>

            <h4 class="mb-2">Runner does not appear online?</h4>
            <ul class="env-list">
              <li>Make sure Docker is installed and running.</li>
              <li>Make sure the API key is valid and has Runner scope.</li>
              <li>Make sure the machine can reach <code>https://api.automaton.monster</code>.</li>
              <li>Check the runner logs for connection or authentication errors.</li>
            </ul>
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

.runner-quickstart {
  text-align: left;
  max-width: 540px;
  margin: 0 auto;
}

.runner-quickstart-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  margin-bottom: 0.5rem;
}

.runner-quickstart-code {
  display: flex;
  flex-direction: column;
  background: var(--p-surface-900);
  color: var(--p-surface-100);
  border-radius: 8px;
  padding: 0.875rem 1rem;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 0.82rem;
  line-height: 1.7;
}

:global(.app-dark) .runner-quickstart-code {
  background: var(--p-surface-800);
}

.runner-quickstart-code em {
  color: var(--p-primary-300);
  font-style: normal;
}

.code-line {
  white-space: nowrap;
}

.runner-link {
  color: var(--p-primary-500);
  text-decoration: none;
}

.runner-link:hover {
  text-decoration: underline;
}
</style>
