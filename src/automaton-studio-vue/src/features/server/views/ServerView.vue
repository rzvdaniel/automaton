<script setup lang="ts">
import { useRoute } from 'vue-router'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import { isAxiosError } from 'axios'
import SelfHostedRunnerInstallDialog from '@/features/runners/components/SelfHostedRunnerInstallDialog.vue'
import { runnersService } from '@/features/runners/services/runners.service'
import { isVisibleApplicationRunner } from '@/features/runners/utils/runner-classification'
import type { Runner } from '@/features/runners/types/runners.types'
import PageHead from '@/shared/components/PageHead.vue'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import ServerHealthCard from '../components/ServerHealthCard.vue'
import RunnersList from '../components/RunnersList.vue'
import ServerVersionCard from '../components/ServerVersionCard.vue'
import { serverHealthService } from '../services/server-health.service'
import { serverConsoleService } from '../services/server-console.service'
import type { ServerHealth } from '../types/server.types'

const refreshMilliseconds = 15000


const route = useRoute()
const onRunners = computed(() => route.params.tab === 'runners')
const authStore = useAuthStore()
const isSystemAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
const loading = ref(true)
const runners = ref<Runner[]>([])
const runnersFailed = ref(false)
const health = ref<ServerHealth | null>(null)
// Server health and updates are for system administrators; a plain administrator still gets the Runners.
const healthDenied = ref(false)
const healthFailed = ref(false)
const valuesFrom = ref<Date | null>(null)
// While the update replaces the API, loads fail on purpose: keep the last data and retry quietly.
const updating = ref(false)
const showInstall = ref(false)
const serverConsole = ref<string | null>(null)
const installingConsole = ref(false)
const installConsoleError = ref<string | null>(null)
let timer: ReturnType<typeof setInterval> | undefined

const visibleRunners = computed(() => runners.value.filter(isVisibleApplicationRunner))
const updateAvailable = computed(() => health.value?.server.updateAvailable === true && !!health.value.server.latestVersion)

async function loadRunners() {
  try {
    runners.value = await runnersService.list()
    runnersFailed.value = false
    valuesFrom.value = new Date()
  } catch {
    runnersFailed.value = true
  }
}

async function loadHealth() {
  try {
    health.value = await serverHealthService.get()
    healthFailed.value = false
  } catch (error) {
    if (isAxiosError(error) && error.response?.status === 403) healthDenied.value = true
    else healthFailed.value = true
  }
}

async function loadServerConsole() {
  try {
    serverConsole.value = (await serverConsoleService.get()).appId
  } catch {
    serverConsole.value = null
  }
}

async function installServerConsole() {
  installingConsole.value = true
  installConsoleError.value = null
  try {
    await serverConsoleService.install()
    await loadServerConsole()
  } catch (error) {
    const message = isAxiosError(error) ? (error.response?.data as { error?: string } | undefined)?.error : undefined
    installConsoleError.value = message ?? 'Server Console could not be installed.'
  } finally {
    installingConsole.value = false
  }
}

function onUpdating(value: boolean) {
  updating.value = value
}

// The API did not answer (and no update explains it): one line, with the time of the values still shown.
const unreachable = computed(() => !updating.value && (healthFailed.value || runnersFailed.value))
const shownAt = computed(() => valuesFrom.value ? valuesFrom.value.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : null)
const healthPaused = computed(() => updating.value ? 'updating' as const : healthFailed.value ? 'unreachable' as const : null)

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
    <PageHead title="Server" :line="'Automaton version, health and Runners.'" />

    <p v-if="unreachable" class="server-notice" data-testid="server-notice">
      The server did not answer<template v-if="shownAt"> &middot; showing {{ shownAt }} values</template>.
      <Button label="Retry" text class="btn-small" @click="refresh" />
    </p>

    <nav class="server-tabs" role="tablist" aria-label="Server">
      <RouterLink to="/server" role="tab" :aria-selected="!onRunners" class="server-tab" data-testid="tab-automaton">Automaton</RouterLink>
      <RouterLink to="/server/runners" role="tab" :aria-selected="onRunners" class="server-tab" data-testid="tab-runners">
        Runners<span v-if="!loading" class="server-tab__count" data-testid="runners-count">{{ visibleRunners.length }}</span>
      </RouterLink>
    </nav>

    <template v-if="!onRunners">
    <ServerVersionCard
      v-if="health" :current="health.server.version" :latest="health.server.latestVersion" :update-available="updateAvailable"
      :checked-at="health.generatedAtUtc" @updated="refresh" @updating="onUpdating" @check="refresh"
    />
    <p v-if="healthDenied" class="studio-muted" data-testid="health-denied">Updates are shown to system administrators.</p>

    <ServerHealthCard
      v-if="isSystemAdministrator && health" :health="health" :console-id="serverConsole" :paused="healthPaused" :shown-at="shownAt"
      :show-console="!loading" :installing="installingConsole" :install-error="installConsoleError"
      @retry="refresh" @install="installServerConsole"
    />

    </template>

    <section v-else>
      <div class="flex align-items-center justify-content-between flex-wrap gap-2">
        <h2 class="studio-section">Runners<span v-if="updating" class="studio-muted" data-testid="runners-updating"> &middot; paused while updating</span>
          <span v-else-if="runnersFailed && shownAt" class="studio-muted"> &middot; could not refresh &middot; showing {{ shownAt }} values</span></h2>
        <Button label="Add a Runner" outlined class="btn-small" data-testid="add-runner" @click="showInstall = true" />
      </div>
      <div v-if="loading" class="studio-muted">Loading Runners...</div>
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
.server-notice { margin: 0 0 16px; padding: 10px 14px; border-radius: 12px; background: color-mix(in srgb, var(--signal) 18%, transparent); color: var(--signal-ink); }
.server-tabs { display: flex; gap: 4px; margin: 0 0 16px; border-bottom: 1px solid var(--line); }
a.server-tab { padding: 8px 12px; margin-bottom: -1px; border-bottom: 2px solid transparent; color: var(--ink-soft); text-decoration: none; font-weight: 500; }
a.server-tab[aria-selected='true'] { color: var(--ink); border-bottom-color: var(--signal); }
.server-tab__count { margin-left: 6px; font-size: 12px; color: var(--ink-soft); }
.server-view { max-width: 1400px; margin: 0 auto; }
</style>
