<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { isAxiosError } from 'axios'
import SelfHostedRunnerInstallDialog from '@/features/runners/components/SelfHostedRunnerInstallDialog.vue'
import { runnersService } from '@/features/runners/services/runners.service'
import { isVisibleApplicationRunner } from '@/features/runners/utils/runner-classification'
import type { Runner } from '@/features/runners/types/runners.types'
import HealthTiles from '../components/HealthTiles.vue'
import RunnersList from '../components/RunnersList.vue'
import ServerUpdateBanner from '../components/ServerUpdateBanner.vue'
import { serverHealthService } from '../services/server-health.service'
import type { ServerHealth } from '../types/server.types'

const refreshMilliseconds = 15000

const loading = ref(true)
const runners = ref<Runner[]>([])
const runnersError = ref<string | null>(null)
const health = ref<ServerHealth | null>(null)
// Server health and updates are for system administrators; a plain administrator still gets the Runners.
const healthDenied = ref(false)
const healthError = ref<string | null>(null)
const showInstall = ref(false)
let timer: ReturnType<typeof setInterval> | undefined

const visibleRunners = computed(() => runners.value.filter(isVisibleApplicationRunner))
const updateAvailable = computed(() => health.value?.server.updateAvailable === true && !!health.value.server.latestVersion)

async function loadRunners() {
  try {
    runners.value = await runnersService.list()
    runnersError.value = null
  } catch {
    runnersError.value = 'Runners could not be loaded.'
  }
}

async function loadHealth() {
  try {
    health.value = await serverHealthService.get()
    healthError.value = null
  } catch (error) {
    if (isAxiosError(error) && error.response?.status === 403) healthDenied.value = true
    else healthError.value = 'Server health could not be loaded.'
  }
}

async function refresh() {
  await Promise.all([loadRunners(), healthDenied.value ? Promise.resolve() : loadHealth()])
}

onMounted(async () => {
  await refresh()
  loading.value = false
  timer = setInterval(() => void refresh(), refreshMilliseconds)
})
onBeforeUnmount(() => clearInterval(timer))
</script>

<template>
  <div class="server-view">
    <div class="flex align-items-center justify-content-between flex-wrap gap-3 mb-4">
      <div>
        <h1 class="text-3xl font-bold m-0">Server</h1>
        <p class="text-color-secondary mt-1 mb-0">
          <template v-if="health?.server.version">Automaton {{ health.server.version }}</template>
          <template v-else>Health, updates and Runners</template>
        </p>
      </div>
    </div>

    <ServerUpdateBanner
      v-if="updateAvailable" class="mb-4" :current="health!.server.version" :latest="health!.server.latestVersion"
      @updated="refresh"
    />

    <section v-if="health" class="mb-5">
      <HealthTiles :health="health" />
      <p v-if="health.host" class="text-color-secondary text-sm mt-2 mb-0" data-testid="host-os">
        {{ [health.host.osName, health.host.osVersion].filter(Boolean).join(' ') }}
      </p>
    </section>
    <Message v-else-if="healthDenied" severity="info" :closable="false" class="mb-4" data-testid="health-denied">
      Health and updates are shown to system administrators.
    </Message>
    <Message v-else-if="healthError" severity="warn" :closable="false" class="mb-4">{{ healthError }}</Message>

    <section>
      <div class="flex align-items-center justify-content-between flex-wrap gap-2 mb-3">
        <h2 class="text-xl font-semibold m-0">Runners</h2>
        <Button label="Add a Runner" icon="pi pi-plus" size="small" data-testid="add-runner" @click="showInstall = true" />
      </div>
      <Message v-if="runnersError" severity="error" :closable="false">{{ runnersError }}</Message>
      <div v-else-if="loading" class="text-color-secondary">Loading Runners...</div>
      <div
        v-else-if="visibleRunners.length === 0" class="empty border-round p-4 text-center text-color-secondary"
        data-testid="runners-empty"
      >
        No Runners yet. A Runner is the machine that runs your apps; add one to deploy.
      </div>
      <RunnersList v-else :runners="visibleRunners" @changed="loadRunners" />
    </section>

    <SelfHostedRunnerInstallDialog v-model:visible="showInstall" @created="loadRunners" />
  </div>
</template>

<style scoped>
.server-view {
  max-width: 1100px;
  margin: 0 auto;
}

.empty {
  border: 1px dashed var(--p-content-border-color);
}
</style>
