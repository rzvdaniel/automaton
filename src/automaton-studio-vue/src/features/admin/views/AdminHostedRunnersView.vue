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
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { adminHostedRunnersService } from '../services/adminHostedRunnersService'
import type { CreateRunnerHostRequest, HostedRunner, RunnerHost } from '../types/admin.types'

const toast = useToast()

const hosts = ref<RunnerHost[]>([])
const hostedRunners = ref<HostedRunner[]>([])
const loadingHosts = ref(false)
const loadingRunners = ref(false)
const provisioningUserId = ref<string | null>(null)
const hostDialogVisible = ref(false)
const savingHost = ref(false)
const editingHost = ref<RunnerHost | null>(null)
const selectedRunner = ref<HostedRunner | null>(null)
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

function statusSeverity(status: string) {
  if (status === 'active' || status === 'running' || status === 'succeeded') return 'success'
  if (status === 'failed' || status === 'disabled') return 'danger'
  if (status === 'draining' || status === 'provisioning' || status === 'pending') return 'warn'
  return 'info'
}

function formatMb(value: number) {
  if (value >= 1024) return `${(value / 1024).toFixed(value % 1024 === 0 ? 0 : 1)} GB`
  return `${value} MB`
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
          <Column header="Status">
            <template #body="{ data }">
              <Tag :value="data.status" :severity="statusSeverity(data.status)" />
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
              <Button icon="pi pi-pencil" text rounded @click="openEditHost(data)" />
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <Card>
      <template #title>Hosted Runner Containers</template>
      <template #content>
        <DataTable :value="hostedRunners" :loading="loadingRunners" data-key="id" responsive-layout="scroll">
          <Column field="userEmail" header="User" />
          <Column field="subscriptionPlanName" header="Plan" />
          <Column field="name" header="Runner" />
          <Column field="runnerHostName" header="Host" />
          <Column header="Status">
            <template #body="{ data }">
              <Tag :value="data.status" :severity="statusSeverity(data.status)" />
            </template>
          </Column>
          <Column header="Resources">
            <template #body="{ data }">{{ data.cpuCores }} CPU, {{ formatMb(data.memoryMb) }}, {{ formatMb(data.diskMb) }}</template>
          </Column>
          <Column field="containerName" header="Container" />
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
                  v-if="data.status !== 'running'"
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
            <Select v-model="hostForm.status" :options="statusOptions" option-label="label" option-value="value" class="w-full" />
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
      </div>
    </Dialog>
  </div>
</template>

<style scoped>
.hosted-runners-view :deep(.p-card-content) {
  padding-top: 0;
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
