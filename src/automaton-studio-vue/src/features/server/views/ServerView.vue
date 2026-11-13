<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { isAxiosError } from 'axios'
import SelfHostedRunnerInstallDialog from '@/features/runners/components/SelfHostedRunnerInstallDialog.vue'
import { appsService } from '@/features/apps/services/apps.service'
import { runnersService } from '@/features/runners/services/runners.service'
import { isVisibleApplicationRunner } from '@/features/runners/utils/runner-classification'
import type { Runner } from '@/features/runners/types/runners.types'
import PageHead from '@/shared/components/PageHead.vue'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import CleanupSection from '../components/CleanupSection.vue'
import RunnersList from '../components/RunnersList.vue'
import ServerUpdateBanner from '../components/ServerUpdateBanner.vue'
import { serverHealthService } from '../services/server-health.service'
import { serverConsoleApp } from '../utils/server-console'
import type { App } from '@/features/apps/types/apps.types'
import type { ServerHealth } from '../types/server.types'

const refreshMilliseconds = 15000


const authStore = useAuthStore()
const isSystemAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
const loading = ref(true)
const runners = ref<Runner[]>([])
const runnersError = ref<string | null>(null)
const health = ref<ServerHealth | null>(null)
// Server health and updates are for system administrators; a plain administrator still gets the Runners.
const healthDenied = ref(false)
const healthError = ref<string | null>(null)
const showInstall = ref(false)
const serverConsole = ref<App | null>(null)
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

async function loadServerConsole() {
  try {
    serverConsole.value = serverConsoleApp(await appsService.list())
  } catch {
    serverConsole.value = null
  }
}

async function refresh() {
  await Promise.all([loadRunners(), healthDenied.value ? Promise.resolve() : loadHealth()])
}

onMounted(async () => {
  await Promise.all([refresh(), isSystemAdministrator.value ? loadServerConsole() : Promise.resolve()])
  loading.value = false
  timer = setInterval(() => void refresh(), refreshMilliseconds)
})
onBeforeUnmount(() => clearInterval(timer))
</script>

<template>
  <div class="server-view studio-page">
    <PageHead title="Server" :line="health?.server.version ? `Automaton ${health.server.version}` : 'Runners, updates and cleanup.'" />

    <ServerUpdateBanner
      v-if="updateAvailable" class="mb-4" :current="health!.server.version" :latest="health!.server.latestVersion"
      @updated="refresh"
    />

    <Message v-if="healthDenied" severity="info" :closable="false" class="mb-4" data-testid="health-denied">
      Updates are shown to system administrators.
    </Message>
    <Message v-else-if="healthError" severity="warn" :closable="false" class="mb-4">{{ healthError }}</Message>

    <p v-if="isSystemAdministrator" class="mb-4" data-testid="server-console-link">
      <template v-if="serverConsole">
        Server health, resources and OS updates:
        <RouterLink :to="`/apps/${serverConsole.id}`" data-testid="server-console-open">open Server Console</RouterLink>.
      </template>
      <template v-else>
        Server health, resources and OS updates are shown by the Server Console app.
        <RouterLink to="/apps/catalog" data-testid="server-console-install">Install Server Console</RouterLink> from the catalog.
      </template>
    </p>

    <CleanupSection v-if="isSystemAdministrator" />

    <section>
      <div class="flex align-items-center justify-content-between flex-wrap gap-2">
        <h2 class="studio-section">Runners</h2>
        <Button label="Add a Runner" outlined class="btn-small" data-testid="add-runner" @click="showInstall = true" />
      </div>
      <Message v-if="runnersError" severity="error" :closable="false">{{ runnersError }}</Message>
      <div v-else-if="loading" class="studio-muted">Loading Runners...</div>
      <div
        v-else-if="visibleRunners.length === 0" class="studio-card text-center studio-muted"
        data-testid="runners-empty"
      >
        No Runners yet. Add one to deploy apps.
      </div>
      <RunnersList v-else :runners="visibleRunners" @changed="loadRunners" />
    </section>


    <SelfHostedRunnerInstallDialog v-model:visible="showInstall" @created="loadRunners" />
  </div>
</template>

<style scoped>
.server-view { max-width: 1400px; margin: 0 auto; }
</style>
