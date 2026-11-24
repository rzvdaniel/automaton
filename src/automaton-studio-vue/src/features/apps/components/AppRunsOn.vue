<script setup lang="ts">
import { computed, ref } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Select from 'primevue/select'
import { useToast } from 'primevue/usetoast'
import { runnersService } from '@/features/runners/services/runners.service'
import { actionSchedulesService } from '../services/action-schedules.service'
import { appsService, type AppActionVersion } from '../services/apps.service'
import type { App, AppDeploymentTarget } from '../types/apps.types'
import { getDeploymentTargetRunnerOptions, getDeploymentTargetRunnerProjectId } from '../utils/deployment-target-runner'
import { getLiveTarget } from '../utils/deployment-target-actions'
import { formatWhen } from '../utils/app-overview'
import type { AppViewKey } from '../app-navigation'

const props = defineProps<{ app: App; targets: ReadonlyArray<AppDeploymentTarget>; origin?: string }>()
const emit = defineEmits<{ changed: []; navigate: [view: AppViewKey] }>()
const toast = useToast()

const canEdit = computed(() => props.app.canUpdate !== false)
const liveTarget = computed(() => getLiveTarget([...props.targets]))
const runnerText = computed(() => {
  const runner = liveTarget.value?.runner
  return runner ? `${runner.name} · ${runner.isOnline ? 'online' : 'offline'}` : 'none assigned'
})
const hasActions = computed(() => !props.app.application?.isStaticSite)

function fail(summary: string, error: unknown) {
  toast.add({ severity: 'error', summary, detail: error instanceof Error ? error.message : undefined, life: 5000 })
}

const runnerDialog = ref(false)
const runnerOptions = ref<ReturnType<typeof getDeploymentTargetRunnerOptions>>([])
const runnerId = ref<string | null>(null)
const savingRunner = ref(false)

async function openRunner() {
  runnerId.value = liveTarget.value?.runner?.id ?? null
  runnerDialog.value = true
  try {
    runnerOptions.value = getDeploymentTargetRunnerOptions(await runnersService.list(getDeploymentTargetRunnerProjectId(props.app.projectId)))
  } catch (error) {
    fail('Runners could not be loaded', error)
  }
}

async function saveRunner() {
  const target = liveTarget.value
  if (!target) return
  savingRunner.value = true
  try {
    await appsService.assignDeploymentTargetRunner(props.app.id, target.id, runnerId.value, false)
    runnerDialog.value = false
    emit('changed')
  } catch (error) {
    fail('The Runner could not be changed', error)
  } finally {
    savingRunner.value = false
  }
}

const isDeploying = computed(() => liveTarget.value?.status === 'Deploying')
const cancelling = ref(false)

async function cancelDeploy() {
  cancelling.value = true
  try {
    await appsService.cancelDeployment(props.app.id, liveTarget.value?.slug)
    emit('changed')
  } catch (error) {
    fail('The deployment could not be cancelled', error)
  } finally {
    cancelling.value = false
  }
}

// Earlier version (Runner-local Action versions)
interface ActionVersions { key: string; versions: AppActionVersion[] }
const versionsDialog = ref(false)
const actionVersions = ref<ActionVersions[]>([])
const versionsError = ref('')

async function openVersions() {
  versionsDialog.value = true
  versionsError.value = ''
  try {
    const actions = await actionSchedulesService.listActions(props.app.id)
    actionVersions.value = await Promise.all(actions.map(async (action) => ({
      key: action.key,
      versions: await appsService.listActionVersions(props.app.id, action.key),
    })))
  } catch {
    actionVersions.value = []
    versionsError.value = 'Earlier versions are kept on the Runner. It is not reachable right now.'
  }
}

async function restore(actionKey: string, version: string) {
  try {
    await appsService.rollbackAction(props.app.id, actionKey, version)
    toast.add({ severity: 'success', summary: `${actionKey} is back on ${version}`, life: 3000 })
    await openVersions()
  } catch (error) {
    fail('That version could not be restored', error)
  }
}
</script>

<template>
  <section class="card" aria-labelledby="runs-on-title" data-testid="runs-on">
    <h2 id="runs-on-title">Running</h2>
    <div class="row">
      <span>Runner</span>
      <span class="value"><span class="dot" :class="{ off: !liveTarget?.runner?.isOnline }" aria-hidden="true"></span>{{ runnerText }}</span>
      <Button v-if="canEdit && liveTarget" label="Change…" size="small" severity="secondary" outlined @click="openRunner" />
    </div>
    <div v-if="canEdit && isDeploying" class="row" data-testid="cancel-deploy">
      <span>Deploying</span>
      <span class="value hint">Stuck? Cancel it to redeploy or delete.</span>
      <Button label="Cancel deploy" size="small" severity="danger" text :loading="cancelling" @click="cancelDeploy" />
    </div>
    <div v-if="liveTarget?.lastSuccessfulDeploymentAt || (canEdit && hasActions)" class="row">
      <span>Version</span>
      <span class="value"><span v-if="liveTarget?.lastSuccessfulDeploymentAt" class="hint">deployed {{ formatWhen(liveTarget.lastSuccessfulDeploymentAt) }}</span></span>
      <Button v-if="canEdit && hasActions" label="Earlier versions…" size="small" severity="secondary" outlined @click="openVersions" />
    </div>
    <div class="row">
      <span>Definition</span>
      <span class="value mono">automaton.json</span>
      <Button label="View in Source" size="small" text @click="emit('navigate', 'source')" />
    </div>
    <div v-if="origin" class="row" data-testid="installed-from">
      <span>Installed from</span>
      <span class="value mono">{{ origin }}</span>
    </div>
  </section>

  <Dialog v-model:visible="runnerDialog" modal header="Change Runner" :style="{ width: 'min(28rem, 92vw)' }">
    <Select v-model="runnerId" :options="runnerOptions" option-label="label" option-value="value" option-disabled="disabled" placeholder="Choose a Runner" class="w-full" aria-label="Runner" />
    <p v-if="props.app.projectId" class="hint" data-testid="project-runner-hint">
      Only Runners shared with this project are listed. Your personal Runner is not shared automatically, because sharing lets every project member deploy to it.
      The Runner's owner can share it with <code>automaton project runners add &lt;runner&gt; --project {{ props.app.projectId }}</code>.
    </p>
    <template #footer>
      <Button label="Cancel" text @click="runnerDialog = false" />
      <Button label="Save" :loading="savingRunner" :disabled="!runnerId" @click="saveRunner" />
    </template>
  </Dialog>

  <Dialog v-model:visible="versionsDialog" modal header="Earlier versions" :style="{ width: 'min(36rem, 94vw)' }">
    <p v-if="versionsError" class="hint">{{ versionsError }}</p>
    <p v-else-if="actionVersions.length === 0" class="hint">Loading…</p>
    <div v-for="action in actionVersions" :key="action.key" class="versions">
      <strong>{{ action.key }}</strong>
      <p v-if="action.versions.length === 0" class="hint">No versions kept.</p>
      <div v-for="version in action.versions" :key="version.version" class="row">
        <span>{{ formatWhen(version.createdAtUtc) }} <span class="hint">· {{ Math.ceil(version.sizeBytes / 1024) }} KB</span></span>
        <span v-if="version.isCurrent" class="hint">current</span>
        <Button v-else label="Restore" size="small" text @click="restore(action.key, version.version)" />
      </div>
    </div>
  </Dialog>
</template>

<style scoped>
.card { display: flex; flex-direction: column; gap: 4px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
h2 { margin: 0 0 6px; font-size: 16px; font-weight: 600; }
.row { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; padding: 4px 0; border-bottom: 1px solid var(--row-line); }
.row:last-child { border-bottom: 0; }
.row > :first-child { width: 90px; color: var(--ink-soft); font-size: 13px; }
.dot { display: inline-block; width: 8px; height: 8px; margin-right: 6px; border-radius: 50%; background: var(--ink-soft); }
.dot.off { background: var(--line); }
.value { flex: 1 1 160px; min-width: 0; overflow-wrap: anywhere; }
.hint { color: var(--ink-soft); font-size: 13px; }
.mono, code { font-family: var(--mono); font-size: 12px; }
.versions { margin-bottom: 12px; }
</style>
