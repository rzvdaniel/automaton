<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import Tag from 'primevue/tag'
import HostedRunnersTabs from '../components/HostedRunnersTabs.vue'
import { adminHostedRunnersService } from '../services/adminHostedRunnersService'
import type { RunnerHost } from '../types/admin.types'
import { useDocumentationDrawer } from '@/shared/documentation/services/documentationDrawer'

const hosts = ref<RunnerHost[]>([])
const loading = ref(false)
const runners = computed(() => hosts.value.filter((host) => host.infrastructureRunnerId))
const { openDocumentation } = useDocumentationDrawer()

async function load() {
  loading.value = true
  try { hosts.value = await adminHostedRunnersService.getHosts() }
  finally { loading.value = false }
}

function severity(status: string) {
  return status.toLowerCase() === 'online' ? 'success' : status.toLowerCase() === 'offline' ? 'danger' : 'warn'
}

onMounted(load)
</script>

<template>
  <div>
    <div class="flex justify-content-between align-items-start gap-3 mb-4">
      <div>
        <h1 class="m-0">Runners</h1>
        <p class="mt-2 mb-0 text-color-secondary">Manage server control agents separately from tenant execution containers.</p>
      </div>
      <div class="flex gap-2">
        <Button label="Documentation" icon="pi pi-book" text @click="openDocumentation('admin.hosted-runners.infrastructure-runners', 'Hosted Runners Administration Guide')" />
        <Button label="Deployment targets" icon="pi pi-sitemap" text @click="openDocumentation('admin.hosted-runners.deployment-targets', 'Deployment targets and Runner relationships')" />
        <Button label="Refresh" icon="pi pi-refresh" outlined :loading="loading" @click="load" />
      </div>
    </div>
    <HostedRunnersTabs />
    <DataTable :value="runners" :loading="loading" data-key="infrastructureRunnerId" responsive-layout="scroll">
      <template #empty>No Infrastructure Runners are enrolled. Install one from the Runner Hosts tab.</template>
      <Column field="infrastructureRunnerName" header="Name" />
      <Column field="name" header="Associated host" />
      <Column header="Connection status"><template #body="{ data }"><Tag :value="data.infrastructureRunnerStatus" :severity="severity(data.infrastructureRunnerStatus)" /></template></Column>
      <Column field="infrastructureRunnerVersion" header="Installed version"><template #body="{ data }">{{ data.infrastructureRunnerVersion || 'Unknown' }}</template></Column>
      <Column field="infrastructureRunnerImageDigest" header="Image digest"><template #body="{ data }">{{ data.infrastructureRunnerImageDigest || 'Unknown' }}</template></Column>
      <Column field="infrastructureRunnerSpecificationVersion" header="Specification"><template #body="{ data }">{{ data.infrastructureRunnerSpecificationVersion || 'Unknown' }}</template></Column>
      <Column header="Compatibility"><template #body="{ data }">{{ data.infrastructureRunnerApiCompatibilityVersion ? `API v${data.infrastructureRunnerApiCompatibilityVersion}` : 'Unknown' }}</template></Column>
      <Column header="Installation health"><template #body="{ data }"><Tag :value="data.infrastructureRunnerOnboardingStatus" :severity="severity(data.infrastructureRunnerOnboardingStatus)" /></template></Column>
      <Column field="infrastructureRunnerLastHeartbeatAtUtc" header="Last heartbeat"><template #body="{ data }">{{ data.infrastructureRunnerLastHeartbeatAtUtc ? new Date(data.infrastructureRunnerLastHeartbeatAtUtc).toLocaleString() : 'Never' }}</template></Column>
      <Column header="Capabilities"><template #body="{ data }"><span>{{ data.infrastructureRunnerCapabilities?.join(', ') || 'None reported' }}</span></template></Column>
    </DataTable>
  </div>
</template>
