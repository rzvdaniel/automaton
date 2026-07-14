<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Checkbox from 'primevue/checkbox'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import Dialog from 'primevue/dialog'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { adminHostedRunnersService } from '../services/adminHostedRunnersService'
import { adminSubscriptionPlansService } from '../services/adminSubscriptionPlansService'
import type {
  CreateRunnerHostRequest,
  HostedRunner,
  RunnerEnrollment,
  RunnerHost,
  SubscriptionPlan,
  UserPlan,
  InfrastructureWorkflowExecution,
  InfrastructureWorkflowExecutionLog,
} from '../types/admin.types'

const toast = useToast()

const hosts = ref<RunnerHost[]>([])
const hostedRunners = ref<HostedRunner[]>([])
const loadingHosts = ref(false)
const loadingRunners = ref(false)
const loadingProvisioningOptions = ref(false)
const provisioningUserId = ref<string | null>(null)
const restartingRunnerId = ref<string | null>(null)
const reprovisioningRunnerId = ref<string | null>(null)
const deletingRunnerId = ref<string | null>(null)
const deleteConfirmationRunnerId = ref<string | null>(null)
const hostedRestartPollMaxAttempts = 8
const hostedRestartPollDelayMs = 1500
const hostDialogVisible = ref(false)
const savingHost = ref(false)
const editingHost = ref<RunnerHost | null>(null)
const selectedRunner = ref<HostedRunner | null>(null)
const enrollmentDialogVisible = ref(false)
const enrollmentHost = ref<RunnerHost | null>(null)
const enrollment = ref<RunnerEnrollment | null>(null)
const enrollingHostId = ref<string | null>(null)
const validatingHostId = ref<string | null>(null)
const validationDialogVisible = ref(false)
const validationExecution = ref<InfrastructureWorkflowExecution | null>(null)
const validationLogs = ref<InfrastructureWorkflowExecutionLog[]>([])
const provisionDialogVisible = ref(false)
const provisioningNewRunner = ref(false)
const users = ref<UserPlan[]>([])
const plans = ref<SubscriptionPlan[]>([])
const selectedProvisionUserId = ref<string | null>(null)
const selectedProvisionPlanId = ref<string | null>(null)
const runnerDetailsVisible = computed({
  get: () => selectedRunner.value !== null,
  set: (visible: boolean) => {
    if (!visible) selectedRunner.value = null
  },
})

const statusOptions = [
  { label: 'Active', value: 'active' },
  { label: 'Draining', value: 'draining' },
  { label: 'Disabled', value: 'disabled' },
] as const

const provisionablePlans = computed(() =>
  plans.value.filter((plan) => plan.isActive && plan.capabilities.hostedRunner.enabled),
)

const userOptions = computed(() =>
  users.value.map((user) => ({
    label: user.name ? `${user.name} (${user.email})` : user.email,
    value: user.userId,
  })),
)

const planOptions = computed(() =>
  provisionablePlans.value.map((plan) => ({
    label: `${plan.name} - ${plan.capabilities.hostedRunner.cpuCores} CPU, ${formatMb(plan.capabilities.hostedRunner.memoryMb)}`,
    value: plan.id,
  })),
)

function defaultHostForm(): CreateRunnerHostRequest {
  return {
    name: '',
    provider: 'hetzner',
    region: '',
    publicIp: '',
    isLocal: false,
    status: 'active',
    totalCpuCores: 2,
    totalMemoryMb: 4096,
    totalDiskMb: 40960,
  }
}

const hostForm = ref<CreateRunnerHostRequest>(defaultHostForm())

const totals = computed(() => {
  return hosts.value.reduce(
    (acc, host) => {
      acc.cpu += host.totalCpuCores
      acc.usedCpu += host.usedCpuCores
      acc.memory += host.totalMemoryMb
      acc.usedMemory += host.usedMemoryMb
      acc.disk += host.totalDiskMb
      acc.usedDisk += host.usedDiskMb
      return acc
    },
    { cpu: 0, usedCpu: 0, memory: 0, usedMemory: 0, disk: 0, usedDisk: 0 },
  )
})

async function loadHosts() {
  loadingHosts.value = true
  try {
    hosts.value = await adminHostedRunnersService.getHosts()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load runner hosts', life: 3000 })
  } finally {
    loadingHosts.value = false
  }
}

async function loadHostedRunners() {
  loadingRunners.value = true
  try {
    hostedRunners.value = await adminHostedRunnersService.getHostedRunners()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load hosted runners', life: 3000 })
  } finally {
    loadingRunners.value = false
  }
}

function delay(ms: number) {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

async function pollHostedRunnerUntilRunning(hostedRunnerId: string) {
  for (let attempt = 0; attempt < hostedRestartPollMaxAttempts; attempt += 1) {
    await delay(hostedRestartPollDelayMs)

    const latestHostedRunners = await adminHostedRunnersService.getHostedRunners()
    hostedRunners.value = latestHostedRunners

    if (latestHostedRunners.some((candidate) => candidate.id === hostedRunnerId && candidate.status === 'running')) {
      return true
    }
  }

  return false
}

async function loadProvisioningOptions() {
  loadingProvisioningOptions.value = true
  try {
    const [loadedUsers, loadedPlans] = await Promise.all([
      adminSubscriptionPlansService.getUsers(),
      adminSubscriptionPlansService.getPlans(),
    ])
    users.value = loadedUsers
    plans.value = loadedPlans
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load users or plans', life: 3000 })
  } finally {
    loadingProvisioningOptions.value = false
  }
}

async function refreshAll() {
  await Promise.all([loadHosts(), loadHostedRunners()])
}

function openCreateHost() {
  editingHost.value = null
  hostForm.value = defaultHostForm()
  hostDialogVisible.value = true
}

function openEditHost(host: RunnerHost) {
  editingHost.value = host
  hostForm.value = {
    name: host.name,
    provider: host.provider,
    region: host.region ?? '',
    publicIp: host.publicIp ?? '',
    isLocal: host.isLocal,
    status: host.status,
    totalCpuCores: host.totalCpuCores,
    totalMemoryMb: host.totalMemoryMb,
    totalDiskMb: host.totalDiskMb,
  }
  hostDialogVisible.value = true
}

async function saveHost() {
  savingHost.value = true
  try {
    if (editingHost.value) {
      await adminHostedRunnersService.updateHost(editingHost.value.id, hostForm.value)
      toast.add({ severity: 'success', summary: 'Host updated', life: 2500 })
    } else {
      await adminHostedRunnersService.createHost(hostForm.value)
      toast.add({ severity: 'success', summary: 'Host added', life: 2500 })
    }
    hostDialogVisible.value = false
    await loadHosts()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to save host', life: 3000 })
  } finally {
    savingHost.value = false
  }
}

async function createInfrastructureRunnerEnrollment(host: RunnerHost) {
  enrollingHostId.value = host.id
  try {
    enrollment.value = await adminHostedRunnersService.createInfrastructureRunnerEnrollment(host.id)
    enrollmentHost.value = host
    enrollmentDialogVisible.value = true
    await loadHosts()
  } catch {
    toast.add({ severity: 'error', summary: 'Enrollment failed', detail: 'Failed to create the Infrastructure Runner enrollment.', life: 3500 })
  } finally {
    enrollingHostId.value = null
  }
}

function enrollmentCommand(): string {
  if (!enrollment.value) return ''
  const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL || window.location.origin).replace(/\/+$/, '')
  const quote = (value: string) => `'${value.replace(/'/g, "'\\\"'\\\"'")}'`
  const installerUrl = `${apiBaseUrl}/api/infrastructure-runner/installer/latest.sh`
  const checksumUrl = `${apiBaseUrl}/api/infrastructure-runner/installer/latest.sha256`
  return `tmp=$(mktemp) && checksum=$(curl -fsSL ${quote(checksumUrl)} | awk '{print $1}') && curl -fsSL ${quote(installerUrl)} -o "$tmp" && echo "$checksum  $tmp" | sha256sum -c - && sudo bash "$tmp" --server-url ${quote(apiBaseUrl)} --enrollment-token ${quote(enrollment.value.enrollmentToken)}; status=$?; rm -f "$tmp"; exit $status`
}

async function validateHost(host: RunnerHost) {
  validatingHostId.value = host.id
  try {
    validationExecution.value = await adminHostedRunnersService.startHostValidation(host.id, host.infrastructureRunnerId)
    validationLogs.value = await adminHostedRunnersService.getInfrastructureWorkflowLogs(validationExecution.value.id)
    validationDialogVisible.value = true
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Validation failed', detail: error instanceof Error ? error.message : 'Host validation could not be started.', life: 4000 })
  } finally {
    validatingHostId.value = null
  }
}

async function retryProvisioning(runner: HostedRunner) {
  provisioningUserId.value = runner.userId
  try {
    await adminHostedRunnersService.provisionForUser(runner.userId, { planId: runner.subscriptionPlanId })
    toast.add({ severity: 'success', summary: 'Provisioning requested', detail: 'Hosted runner deployment was queued again.', life: 3000 })
    await refreshAll()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to request hosted runner provisioning', life: 3000 })
  } finally {
    provisioningUserId.value = null
  }
}

async function restartHostedRunner(runner: HostedRunner) {
  restartingRunnerId.value = runner.id
  try {
    await adminHostedRunnersService.restartHostedRunner(runner.id)
    toast.add({
      severity: 'info',
      summary: 'Restart requested',
      detail: `Restart requested for ${runner.name}. Waiting for it to reconnect...`,
      life: 3500,
    })

    const isRunning = await pollHostedRunnerUntilRunning(runner.id)
    await loadHosts()

    if (isRunning) {
      toast.add({
        severity: 'success',
        summary: 'Runner online',
        detail: `${runner.name} is running again.`,
        life: 3000,
      })
    } else {
      toast.add({
        severity: 'warn',
        summary: 'Still reconnecting',
        detail: `${runner.name} did not report running yet. It may still be restarting.`,
        life: 5000,
      })
    }
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to restart hosted runner', life: 3000 })
  } finally {
    restartingRunnerId.value = null
  }
}

async function reprovisionHostedRunner(runner: HostedRunner) {
  reprovisioningRunnerId.value = runner.id
  try {
    await adminHostedRunnersService.reprovisionHostedRunner(runner.id)
    toast.add({
      severity: 'success',
      summary: 'Reprovision requested',
      detail: `Reprovision started for ${runner.name}.`,
      life: 3000,
    })
    await refreshAll()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to reprovision hosted runner', life: 3000 })
  } finally {
    reprovisioningRunnerId.value = null
  }
}

function deleteHostedRunner(runner: HostedRunner) {
  deleteConfirmationRunnerId.value = runner.id
}

async function confirmDeleteHostedRunner(runner: HostedRunner) {
  deletingRunnerId.value = runner.id
  try {
    await adminHostedRunnersService.deleteHostedRunner(runner.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `${runner.name} was deleted.`, life: 3000 })
    await refreshAll()
    selectedRunner.value = hostedRunners.value.find((candidate) => candidate.id === runner.id) ?? null
  } catch {
    toast.add({ severity: 'error', summary: 'Delete failed', detail: 'Failed to delete the hosted runner.', life: 4000 })
  } finally {
    deletingRunnerId.value = null
    deleteConfirmationRunnerId.value = null
  }
}

async function openProvisionDialog() {
  selectedProvisionUserId.value = null
  selectedProvisionPlanId.value = null
  provisionDialogVisible.value = true
  await loadProvisioningOptions()
}

async function provisionHostedRunner() {
  if (!selectedProvisionUserId.value || !selectedProvisionPlanId.value) return

  provisioningNewRunner.value = true
  try {
    await adminHostedRunnersService.provisionForUser(selectedProvisionUserId.value, {
      planId: selectedProvisionPlanId.value,
    })
    toast.add({
      severity: 'success',
      summary: 'Provisioning requested',
      detail: 'Hosted runner provisioning has started for the selected user.',
      life: 3000,
    })
    provisionDialogVisible.value = false
    await refreshAll()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to provision hosted runner', life: 3000 })
  } finally {
    provisioningNewRunner.value = false
  }
}

function statusSeverity(status: string) {
  if (status === 'active' || status === 'running' || status === 'succeeded') return 'success'
  if (status === 'failed' || status === 'disabled' || status === 'deleteFailed') return 'danger'
  if (status === 'deleted') return 'secondary'
  if (status === 'draining' || status === 'provisioning' || status === 'pending') return 'warn'
  return 'info'
}

function isDeletedRunner(runner: HostedRunner) {
  return runner.status === 'deleted' || !!runner.deletedAt
}

function displayRunnerStatus(runner: HostedRunner) {
  return isDeletedRunner(runner) ? 'deleted' : runner.status
}

function formatMb(value: number) {
  if (value >= 1024) return `${(value / 1024).toFixed(value % 1024 === 0 ? 0 : 1)} GB`
  return `${value} MB`
}

function parseUtcDate(value?: string) {
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

function formatDateTime(value?: string) {
  const date = parseUtcDate(value)
  if (!date) {
    return 'Not tracked yet'
  }

  return date.toLocaleString()
}

function formatRelativeTime(value?: string) {
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

onMounted(refreshAll)
</script>

<template>
  <div class="hosted-runners-view">
    <div class="flex justify-content-between align-items-start gap-3 mb-4">
      <div>
        <h1 class="m-0">Hosted Runners</h1>
        <p class="text-color-secondary mt-2 mb-0">
          Manage Hetzner capacity and paid-user runner containers from the same subscription flow used by Stripe.
        </p>
      </div>
      <Button label="Refresh" icon="pi pi-refresh" outlined @click="refreshAll" />
    </div>

    <Card class="mb-4 infrastructure-note">
      <template #content>
        <div class="infrastructure-note__eyebrow">Experimental Infrastructure</div>
        <div class="infrastructure-note__copy">
          Hosted runners and deployment cutovers are still in preview. Short-lived disconnects,
          reconnect lag, or manual intervention may still be required while the platform evolves.
        </div>
      </template>
    </Card>

    <div class="grid mb-4">
      <div class="col-12 md:col-4">
        <Card>
          <template #content>
            <div class="text-color-secondary text-sm mb-2">CPU allocated</div>
            <div class="text-2xl font-semibold">{{ totals.usedCpu }} / {{ totals.cpu }} cores</div>
          </template>
        </Card>
      </div>
      <div class="col-12 md:col-4">
        <Card>
          <template #content>
            <div class="text-color-secondary text-sm mb-2">Memory allocated</div>
            <div class="text-2xl font-semibold">{{ formatMb(totals.usedMemory) }} / {{ formatMb(totals.memory) }}</div>
          </template>
        </Card>
      </div>
      <div class="col-12 md:col-4">
        <Card>
          <template #content>
            <div class="text-color-secondary text-sm mb-2">Disk allocated</div>
            <div class="text-2xl font-semibold">{{ formatMb(totals.usedDisk) }} / {{ formatMb(totals.disk) }}</div>
          </template>
        </Card>
      </div>
    </div>

    <Card class="mb-4">
      <template #title>
        <div class="flex justify-content-between align-items-center gap-3">
          <span>Runner Hosts</span>
          <Button label="Add Host" icon="pi pi-plus" size="small" @click="openCreateHost" />
        </div>
      </template>
      <template #content>
        <DataTable :value="hosts" :loading="loadingHosts" data-key="id" responsive-layout="scroll">
          <Column field="name" header="Host" />
          <Column field="provider" header="Provider" />
          <Column field="publicIp" header="IP" />
          <Column header="Infrastructure Runner">
            <template #body="{ data }">
              <div v-if="data.infrastructureRunnerName" class="flex align-items-center gap-2">
                <span>{{ data.infrastructureRunnerName }}</span>
                <Tag
                  :value="data.infrastructureRunnerOnboardingStatus || 'NotInstalled'"
                  :severity="statusSeverity(data.infrastructureRunnerOnboardingStatus?.toLowerCase() || 'none')"
                />
              </div>
              <Tag
                v-else
                :value="data.infrastructureRunnerOnboardingStatus || 'NotInstalled'"
                :severity="statusSeverity(data.infrastructureRunnerOnboardingStatus?.toLowerCase() || 'none')"
              />
            </template>
          </Column>
          <Column header="Status">
            <template #body="{ data }">
              <Tag :value="displayRunnerStatus(data)" :severity="statusSeverity(displayRunnerStatus(data))" />
            </template>
          </Column>
          <Column header="CPU">
            <template #body="{ data }">{{ data.usedCpuCores }} / {{ data.totalCpuCores }}</template>
          </Column>
          <Column header="Memory">
            <template #body="{ data }">{{ formatMb(data.usedMemoryMb) }} / {{ formatMb(data.totalMemoryMb) }}</template>
          </Column>
          <Column header="Disk">
            <template #body="{ data }">{{ formatMb(data.usedDiskMb) }} / {{ formatMb(data.totalDiskMb) }}</template>
          </Column>
          <Column field="hostedRunnerCount" header="Runners" />
          <Column header="Actions">
            <template #body="{ data }">
              <div class="flex gap-2">
                <Button
                  v-if="!data.infrastructureRunnerId"
                  label="Install Runner"
                  icon="pi pi-key"
                  size="small"
                  outlined
                  :loading="enrollingHostId === data.id"
                  @click="createInfrastructureRunnerEnrollment(data)"
                />
                <Button
                  v-else
                  label="Validate"
                  icon="pi pi-check-circle"
                  size="small"
                  outlined
                  :loading="validatingHostId === data.id"
                  @click="validateHost(data)"
                />
                <Button icon="pi pi-pencil" text rounded @click="openEditHost(data)" />
              </div>
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <Card>
      <template #title>
        <div class="flex justify-content-between align-items-center gap-3">
          <span>Hosted Runner Containers</span>
          <Button label="Provision Runner" icon="pi pi-play" size="small" @click="openProvisionDialog" />
        </div>
      </template>
      <template #content>
        <DataTable :value="hostedRunners" :loading="loadingRunners" data-key="id" responsive-layout="scroll" :row-class="(data: HostedRunner) => isDeletedRunner(data) ? 'opacity-60' : undefined">
          <Column field="userEmail" header="User" />
          <Column field="subscriptionPlanName" header="Plan" />
          <Column field="name" header="Runner" />
          <Column field="runnerHostName" header="Host" />
          <Column header="Status">
            <template #body="{ data }">
              <Tag :value="displayRunnerStatus(data)" :severity="statusSeverity(displayRunnerStatus(data))" />
            </template>
          </Column>
          <Column header="Resources">
            <template #body="{ data }">{{ data.cpuCores }} CPU, {{ formatMb(data.memoryMb) }}, {{ formatMb(data.diskMb) }}</template>
          </Column>
          <Column field="containerName" header="Container" />
          <Column header="Runtime port">
            <template #body="{ data }">
              <span v-if="data.runtimePort">{{ data.runtimePort }}</span>
              <span v-else class="text-color-secondary">Internal</span>
            </template>
          </Column>
          <Column header="Deployment">
            <template #body="{ data }">
              <Tag
                v-if="data.latestDeployment"
                :value="data.latestDeployment.status"
                :severity="statusSeverity(data.latestDeployment.status)"
              />
            </template>
          </Column>
          <Column header="Actions">
            <template #body="{ data }">
              <div class="flex gap-2">
                <Button label="Details" text size="small" @click="selectedRunner = data" />
                <Button
                  v-if="!isDeletedRunner(data)"
                  label="Restart"
                  icon="pi pi-refresh"
                  size="small"
                  outlined
                  :loading="restartingRunnerId === data.id"
                  @click="restartHostedRunner(data)"
                />
                <Button
                  v-if="!isDeletedRunner(data)"
                  label="Reprovision"
                  icon="pi pi-replay"
                  size="small"
                  severity="secondary"
                  outlined
                  :loading="reprovisioningRunnerId === data.id"
                  @click="reprovisionHostedRunner(data)"
                />
                <Button
                  v-if="!isDeletedRunner(data) && data.status !== 'running'"
                  label="Retry"
                  icon="pi pi-play"
                  size="small"
                  outlined
                  :loading="provisioningUserId === data.userId"
                  @click="retryProvisioning(data)"
                />
              </div>
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <Dialog v-model:visible="provisionDialogVisible" header="Provision Hosted Runner" modal :style="{ width: '36rem' }">
      <div class="flex flex-column gap-3">
        <div>
          <label class="block text-sm font-medium mb-2">User</label>
          <Select
            v-model="selectedProvisionUserId"
            :options="userOptions"
            option-label="label"
            option-value="value"
            class="w-full"
            placeholder="Select a user"
            :loading="loadingProvisioningOptions"
          />
        </div>
        <div>
          <label class="block text-sm font-medium mb-2">Hosted runner plan</label>
          <Select
            v-model="selectedProvisionPlanId"
            :options="planOptions"
            option-label="label"
            option-value="value"
            class="w-full"
            placeholder="Select a plan"
            :loading="loadingProvisioningOptions"
          />
        </div>
        <div class="text-color-secondary text-sm">
          This uses the same provisioning endpoint as plan assignment and Stripe-driven subscriptions, but lets admins run the first smoke test directly.
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="provisionDialogVisible = false" />
        <Button
          label="Provision"
          icon="pi pi-play"
          :disabled="!selectedProvisionUserId || !selectedProvisionPlanId"
          :loading="provisioningNewRunner"
          @click="provisionHostedRunner"
        />
      </template>
    </Dialog>

    <Dialog v-model:visible="enrollmentDialogVisible" header="Install Infrastructure Runner" modal :style="{ width: '48rem' }">
      <div v-if="enrollment" class="flex flex-column gap-3">
        <Message severity="warn" :closable="false">
          This token is shown once. It expires {{ formatDateTime(enrollment.expiresAtUtc) }} and cannot be reused after enrollment.
        </Message>
        <div>
          <div class="text-color-secondary text-sm mb-2">Runner Host</div>
          <div class="font-medium">{{ enrollmentHost?.name || enrollment.runnerHostId }}</div>
        </div>
        <div>
          <div class="text-color-secondary text-sm mb-2">Installer command</div>
          <Textarea :model-value="enrollmentCommand()" readonly auto-resize class="w-full" />
        </div>
        <div>
          <div class="text-color-secondary text-sm mb-2">Authorized capabilities</div>
          <div class="flex flex-wrap gap-2">
            <Tag v-for="capability in enrollment.expectedCapabilities" :key="capability" :value="capability" />
          </div>
        </div>
        <div class="text-color-secondary text-sm">
          Copy the command to the new Hetzner server and run it as root. The Runner will persist its durable credential in its mounted config volume.
        </div>
      </div>
      <template #footer>
        <Button label="Close" text @click="enrollmentDialogVisible = false" />
      </template>
    </Dialog>

    <Dialog v-model:visible="validationDialogVisible" header="Infrastructure Host Validation" modal :style="{ width: '56rem' }">
      <div v-if="validationExecution" class="flex flex-column gap-3">
        <div class="flex align-items-center gap-2">
          <span class="text-color-secondary">Execution</span>
          <code>{{ validationExecution.id }}</code>
          <Tag :value="validationExecution.status" :severity="statusSeverity(validationExecution.status)" />
        </div>
        <Message v-if="validationExecution.error" severity="error" :closable="false">{{ validationExecution.error }}</Message>
        <div v-if="validationExecution.resultJson">
          <div class="text-color-secondary text-sm mb-2">Structured result</div>
          <pre class="command-preview">{{ validationExecution.resultJson }}</pre>
        </div>
        <div>
          <div class="text-color-secondary text-sm mb-2">Durable logs</div>
          <Textarea :model-value="validationLogs.map((log) => `[${log.stream}] ${log.message}`).join('\n')" readonly auto-resize class="w-full" />
        </div>
      </div>
      <template #footer>
        <Button label="Close" text @click="validationDialogVisible = false" />
      </template>
    </Dialog>

    <Dialog v-model:visible="hostDialogVisible" :header="editingHost ? 'Edit Runner Host' : 'Add Runner Host'" modal :style="{ width: '36rem' }">
      <div class="flex flex-column gap-3">
        <div>
          <label class="block text-sm font-medium mb-2">Name</label>
          <InputText v-model="hostForm.name" class="w-full" placeholder="Hetzner CX23 - Nuremberg" />
        </div>
        <div class="grid">
          <div class="col-12 md:col-6">
            <label class="block text-sm font-medium mb-2">Provider</label>
            <InputText v-model="hostForm.provider" class="w-full" />
          </div>
          <div class="col-12 md:col-6">
            <label class="block text-sm font-medium mb-2">Status</label>
            <Select v-model="hostForm.status" :options="[...statusOptions]" option-label="label" option-value="value" class="w-full" />
          </div>
        </div>
        <div class="grid">
          <div class="col-12 md:col-6">
            <label class="block text-sm font-medium mb-2">Region</label>
            <InputText v-model="hostForm.region" class="w-full" placeholder="nbg1" />
          </div>
          <div class="col-12 md:col-6">
            <label class="block text-sm font-medium mb-2">Public IP</label>
            <InputText v-model="hostForm.publicIp" class="w-full" />
          </div>
        </div>
        <div class="grid">
          <div class="col-12 md:col-4">
            <label class="block text-sm font-medium mb-2">CPU cores</label>
            <InputNumber v-model="hostForm.totalCpuCores" class="w-full" :min="0.25" :step="0.25" :min-fraction-digits="0" :max-fraction-digits="2" />
          </div>
          <div class="col-12 md:col-4">
            <label class="block text-sm font-medium mb-2">Memory MB</label>
            <InputNumber v-model="hostForm.totalMemoryMb" class="w-full" :min="128" />
          </div>
          <div class="col-12 md:col-4">
            <label class="block text-sm font-medium mb-2">Disk MB</label>
            <InputNumber v-model="hostForm.totalDiskMb" class="w-full" :min="512" />
          </div>
        </div>
        <div class="flex align-items-center gap-2">
          <Checkbox v-model="hostForm.isLocal" binary input-id="isLocalHost" />
          <label for="isLocalHost">This is the current Automaton server</label>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text @click="hostDialogVisible = false" />
        <Button label="Save" icon="pi pi-check" :loading="savingHost" @click="saveHost" />
      </template>
    </Dialog>

    <Dialog v-model:visible="runnerDetailsVisible" header="Hosted Runner Details" modal :style="{ width: '48rem' }">
      <div v-if="selectedRunner" class="flex flex-column gap-3">
        <div v-if="deletingRunnerId === selectedRunner.id" class="flex align-items-center gap-2 border-bottom-1 surface-border pb-3 text-color-secondary">
          <i class="pi pi-spin pi-spinner" />
          <span>Deleting hosted runner. Removing managed container and clearing assignments...</span>
        </div>
        <div v-else-if="deleteConfirmationRunnerId === selectedRunner.id" class="border-bottom-1 surface-border pb-3">
          <div class="flex align-items-center gap-2 text-color-danger font-semibold mb-2">
            <i class="pi pi-exclamation-triangle" />
            <span>Delete Hosted Runner</span>
          </div>
          <p class="mt-0 mb-3 text-color-secondary">This will remove the managed container, unassign all applications, release the hosted runner, and cannot be undone.</p>
          <div class="flex justify-content-end gap-2">
            <Button label="Cancel" text @click="deleteConfirmationRunnerId = null" />
            <Button label="Delete hosted runner" icon="pi pi-trash" severity="danger" @click="confirmDeleteHostedRunner(selectedRunner)" />
          </div>
        </div>
        <template v-else>
        <div class="grid">
          <div class="col-12 md:col-6">
            <div class="text-color-secondary text-sm">User</div>
            <div class="font-medium">{{ selectedRunner.userEmail }}</div>
          </div>
          <div class="col-12 md:col-6">
            <div class="text-color-secondary text-sm">Container</div>
            <div class="font-medium">{{ selectedRunner.containerName }}</div>
          </div>
        </div>
        <div class="grid">
          <div class="col-12 md:col-6">
            <div class="text-color-secondary text-sm">Runner status</div>
            <Tag :value="selectedRunner.runnerConnectivityStatus" :severity="statusSeverity(selectedRunner.runnerConnectivityStatus)" />
          </div>
          <div class="col-12 md:col-6">
            <div class="text-color-secondary text-sm">Runner version</div>
            <div class="font-medium">{{ selectedRunner.runnerVersion || 'Unknown' }}</div>
          </div>
          <div class="col-12 md:col-6">
            <div class="text-color-secondary text-sm">Last heartbeat</div>
            <div class="font-medium">{{ formatRelativeTime(selectedRunner.runnerLastHeartbeatAtUtc) }}</div>
          </div>
          <div class="col-12 md:col-6">
            <div class="text-color-secondary text-sm">Last connected</div>
            <div class="font-medium">{{ formatDateTime(selectedRunner.runnerLastConnectedAtUtc) }}</div>
          </div>
          <div class="col-12 md:col-6">
            <div class="text-color-secondary text-sm">Machine name</div>
            <div class="font-medium">{{ selectedRunner.runnerMachineName || 'Unknown' }}</div>
          </div>
          <div v-if="selectedRunner.deletedAt" class="col-12 md:col-6">
            <div class="text-color-secondary text-sm">Deleted</div>
            <div class="font-medium">{{ formatDateTime(selectedRunner.deletedAt) }}</div>
          </div>
        </div>
        <div v-if="selectedRunner.lastError">
          <div class="text-color-secondary text-sm mb-2">Last error</div>
          <Textarea :model-value="selectedRunner.lastError" readonly auto-resize class="w-full" />
        </div>
        <div v-if="selectedRunner.latestDeployment?.logs">
          <div class="text-color-secondary text-sm mb-2">Deployment log</div>
          <Textarea :model-value="selectedRunner.latestDeployment.logs" readonly auto-resize class="w-full" />
        </div>
        <div v-if="selectedRunner.latestDeployment?.commandPreview">
          <div class="text-color-secondary text-sm mb-2">Docker command preview</div>
          <pre class="command-preview">{{ selectedRunner.latestDeployment.commandPreview }}</pre>
        </div>
        <div v-if="!isDeletedRunner(selectedRunner)" class="flex justify-content-end border-top-1 surface-border pt-3">
          <Button
            :label="deletingRunnerId === selectedRunner.id ? 'Deleting...' : 'Delete hosted runner'"
            icon="pi pi-trash"
            severity="danger"
            outlined
            :loading="deletingRunnerId === selectedRunner.id"
            :disabled="deletingRunnerId === selectedRunner.id"
            @click="deleteHostedRunner(selectedRunner)"
          />
        </div>
        </template>
      </div>
    </Dialog>
  </div>
</template>

<style scoped>
.hosted-runners-view :deep(.p-card-content) {
  padding-top: 0;
}

.infrastructure-note {
  border: 1px solid color-mix(in srgb, var(--p-orange-500) 28%, var(--p-surface-200));
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--p-orange-500) 10%, transparent), transparent 70%),
    var(--p-surface-0);
}

.infrastructure-note__eyebrow {
  margin-bottom: 0.5rem;
  color: var(--p-orange-600);
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.infrastructure-note__copy {
  color: var(--p-text-muted-color);
  line-height: 1.6;
}

:global(.app-dark) .infrastructure-note {
  border-color: color-mix(in srgb, var(--p-orange-400) 24%, var(--p-surface-700));
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--p-orange-400) 12%, transparent), transparent 70%),
    var(--p-surface-900);
}

:global(.app-dark) .infrastructure-note__eyebrow {
  color: var(--p-orange-300);
}

.command-preview {
  margin: 0;
  padding: 1rem;
  border-radius: 0.75rem;
  background: var(--surface-100);
  color: var(--text-color);
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 0.875rem;
}
</style>
