<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Select from 'primevue/select'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { runnersService } from '@/features/runners/services/runners.service'
import { actionSchedulesService } from '../services/action-schedules.service'
import { appsService, type AppActionVersion } from '../services/apps.service'
import { useAppsStore } from '../stores/apps.store'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { App, AppDeploymentTarget } from '../types/apps.types'
import { getDeploymentTargetRunnerOptions, getDeploymentTargetRunnerProjectId } from '../utils/deployment-target-runner'
import { getLiveTarget } from '../utils/deployment-target-actions'
import { formatWhen } from '../utils/app-overview'

const props = defineProps<{ app: App; targets: ReadonlyArray<AppDeploymentTarget>; open?: boolean }>()
const emit = defineEmits<{ changed: [] }>()

const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const appsStore = useAppsStore()
const editorStore = useAppEditorStore()

const liveTarget = computed(() => getLiveTarget([...props.targets]))
const runnerText = computed(() => {
  const runner = liveTarget.value?.runner
  return runner ? `${runner.name} · ${runner.isOnline ? 'online' : 'offline'}` : 'none assigned'
})
const hasActions = computed(() => !props.app.application?.isStaticSite)

function fail(summary: string, error: unknown) {
  toast.add({ severity: 'error', summary, detail: error instanceof Error ? error.message : undefined, life: 5000 })
}

// Runner
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

// App definition
const definitionDialog = ref(false)
const definitionJson = computed(() => JSON.stringify(props.app.application, null, 2))

// Icon
const fileInput = ref<HTMLInputElement | null>(null)

async function uploadIcon(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!file.type.startsWith('image/') || file.size > 2 * 1024 * 1024) {
    toast.add({ severity: 'error', summary: 'Choose an image under 2 MB', life: 4000 })
    return
  }
  try {
    editorStore.currentApp!.thumbnailUrl = await appsService.uploadThumbnail(props.app.id, file)
  } catch (error) {
    fail('The icon could not be uploaded', error)
  }
}

async function removeIcon() {
  try {
    await appsService.removeThumbnail(props.app.id)
    editorStore.currentApp!.thumbnailUrl = undefined
  } catch (error) {
    fail('The icon could not be removed', error)
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

// Delete
const deleting = ref(false)

function askDelete() {
  confirm.require({
    header: 'Delete this app?',
    message: `Delete "${props.app.name}" and all of its data? This cannot be undone.`,
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    acceptLabel: 'Delete',
    accept: async () => {
      deleting.value = true
      try {
        await appsStore.deleteApp(props.app.id)
        toast.add({ severity: 'success', summary: `"${props.app.name}" was deleted`, life: 3000 })
        await router.push('/apps')
      } catch (error) {
        fail('The app could not be deleted', error)
      } finally {
        deleting.value = false
      }
    },
  })
}
</script>

<template>
  <details class="card" :open="open" data-testid="advanced">
    <summary>Advanced</summary>
    <div class="row">
      <span>Runner</span>
      <span class="hint"><span class="mono">{{ runnerText }}</span><template v-if="liveTarget"> · <button type="button" class="link" @click="openRunner">Change…</button></template></span>
    </div>
    <div class="row">
      <span>App definition (automaton.json)</span>
      <button type="button" class="link" @click="definitionDialog = true">View</button>
    </div>
    <div class="row">
      <span>Icon</span>
      <span class="hint">
        <button type="button" class="link" @click="fileInput?.click()">Change</button>
        <template v-if="app.thumbnailUrl"> · <button type="button" class="link" @click="removeIcon">Remove</button></template>
        <input ref="fileInput" type="file" accept="image/*" hidden @change="uploadIcon" />
      </span>
    </div>
    <div v-if="hasActions" class="row">
      <span>Go back to an earlier version</span>
      <button type="button" class="link" @click="openVersions">Choose…</button>
    </div>
    <div class="row">
      <span class="danger">Delete this app and its data</span>
      <Button label="Delete…" severity="danger" outlined :loading="deleting" @click="askDelete" />
    </div>
  </details>

  <Dialog v-model:visible="runnerDialog" modal header="Change Runner" :style="{ width: 'min(28rem, 92vw)' }">
    <Select v-model="runnerId" :options="runnerOptions" option-label="label" option-value="value" option-disabled="disabled" placeholder="Choose a Runner" class="w-full" aria-label="Runner" />
    <p v-if="props.app.projectId" class="text-color-secondary" data-testid="project-runner-hint">
      Only Runners shared with this project are listed. Your personal Runner is not shared automatically, because sharing lets every project member deploy to it.
      The Runner's owner can share it with <code>automaton project runners add &lt;runner&gt; --project {{ props.app.projectId }}</code>.
    </p>
    <template #footer>
      <Button label="Cancel" text @click="runnerDialog = false" />
      <Button label="Save" :loading="savingRunner" :disabled="!runnerId" @click="saveRunner" />
    </template>
  </Dialog>

  <Dialog v-model:visible="definitionDialog" modal header="App definition" :style="{ width: 'min(40rem, 94vw)' }">
    <pre class="code" data-testid="raw-contract">{{ definitionJson }}</pre>
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
.card { display: flex; flex-direction: column; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
summary { cursor: pointer; font-weight: 600; }
.row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 0; border-bottom: 1px solid var(--row-line); }
.row:last-child { border-bottom: 0; }
.hint { color: var(--ink-soft); font-size: 13px; }
.mono, code { font-family: var(--mono); font-size: 12px; }
.link { padding: 0; border: 0; background: none; color: var(--signal-ink); font: inherit; text-decoration: underline; cursor: pointer; }
.danger { color: var(--signal-ink); }
.code { max-height: 60vh; overflow: auto; margin: 0; font-family: var(--mono); font-size: 12px; }
.versions { margin-bottom: 12px; }
</style>
