<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Textarea from 'primevue/textarea'
import { useToast } from 'primevue/usetoast'
import { appsService, type AppActionExecution, type AppActionVersion } from '../services/apps.service'

const props = defineProps<{ appId: string; actionKey: string }>()
const toast = useToast()
const visible = ref(false)
const input = ref('{}')
const running = ref(false)
const execution = ref<AppActionExecution | null>(null)
const logs = ref<string | null>(null)
const result = ref<string | null>(null)
const recent = ref<AppActionExecution[]>([])
const versions = ref<AppActionVersion[]>([])
const terminalStatuses = new Set(['Succeeded', 'Failed', 'TimedOut', 'Cancelled', 'RunnerUnavailable', 'Lost'])
const isActive = computed(() => execution.value != null && !terminalStatuses.has(execution.value.status))

async function refreshContext() {
  try {
    const [allExecutions, actionVersions] = await Promise.all([
      appsService.listActionExecutions(props.appId),
      appsService.listActionVersions(props.appId, props.actionKey),
    ])
    recent.value = allExecutions.filter(item => item.moduleKey === props.actionKey).slice(0, 5)
    versions.value = actionVersions
  } catch {
    recent.value = []
    versions.value = []
  }
}

async function pollExecution(executionId: string) {
  for (let attempt = 0; attempt < 360; attempt++) {
    const current = await appsService.getActionExecution(props.appId, executionId)
    execution.value = current
    if (terminalStatuses.has(current.status)) return
    await new Promise(resolve => window.setTimeout(resolve, 1000))
  }
}

async function run() {
  try {
    const payload = JSON.parse(input.value)
    running.value = true
    logs.value = null
    result.value = null
    execution.value = await appsService.runAction(props.appId, props.actionKey, payload)
    await pollExecution(execution.value.id)
    if (execution.value) {
      const [logArtifact, resultArtifact] = await Promise.all([
        appsService.getActionArtifact(props.appId, execution.value.id, 'logs'),
        appsService.getActionArtifact(props.appId, execution.value.id, 'result'),
      ])
      logs.value = logArtifact.content ?? logArtifact.message ?? null
      result.value = resultArtifact.content ?? resultArtifact.message ?? null
    }
    await refreshContext()
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Action failed', detail: error instanceof Error ? error.message : String(error), life: 5000 })
  } finally {
    running.value = false
  }
}

async function cancel() {
  if (!execution.value) return
  const cancelled = await appsService.cancelAction(props.appId, execution.value.id)
  toast.add({ severity: cancelled ? 'info' : 'warn', summary: cancelled ? 'Cancellation requested' : 'Action is not active', life: 3000 })
}

async function rollback(version: string) {
  await appsService.rollbackAction(props.appId, props.actionKey, version)
  toast.add({ severity: 'success', summary: 'Action version restored', detail: version, life: 3000 })
  await refreshContext()
}

async function removeVersion(version: string) {
  await appsService.deleteActionVersion(props.appId, props.actionKey, version)
  await refreshContext()
}

onMounted(refreshContext)
watch(() => props.actionKey, refreshContext)
</script>

<template>
  <div class="action-context-bar">
    <span><i class="pi pi-bolt" /> Action: <strong>{{ actionKey }}</strong></span>
    <Button label="Run" icon="pi pi-play" size="small" @click="visible = true" />
  </div>
  <Dialog v-model:visible="visible" modal :header="`Run ${actionKey}`" :style="{ width: 'min(42rem, 92vw)' }">
    <label class="input-label" for="action-input">JSON input</label>
    <Textarea id="action-input" v-model="input" rows="7" class="input-editor" />
    <div v-if="execution" class="execution-summary">Status: <strong>{{ execution.status }}</strong><span v-if="execution.errorCode"> · {{ execution.errorCode }}</span></div>
    <pre v-if="logs" class="artifact"><strong>Logs</strong>\n{{ logs }}</pre>
    <pre v-if="result" class="artifact"><strong>Result</strong>\n{{ result }}</pre>
    <template #footer>
      <Button v-if="isActive" label="Cancel" severity="danger" outlined @click="cancel" />
      <Button label="Run" icon="pi pi-play" :loading="running" @click="run" />
    </template>
    <div v-if="recent.length" class="context-section">
      <strong>Recent executions</strong>
      <div v-for="item in recent" :key="item.id" class="context-row"><code>{{ item.id.slice(0, 8) }}</code><span>{{ item.status }}</span><span>{{ new Date(item.createdAtUtc).toLocaleString() }}</span></div>
    </div>
    <div v-if="versions.length" class="context-section">
      <strong>Runner-local versions</strong>
      <div v-for="item in versions" :key="item.version" class="context-row">
        <code>{{ item.version }}</code><span>{{ Math.ceil(item.sizeBytes / 1024) }} KB</span>
        <span v-if="item.isCurrent">Current</span>
        <span v-else class="version-actions"><Button label="Restore" size="small" text @click="rollback(item.version)" /><Button icon="pi pi-trash" size="small" text severity="danger" aria-label="Delete version" @click="removeVersion(item.version)" /></span>
      </div>
    </div>
  </Dialog>
</template>

<style scoped>
.action-context-bar { display: flex; align-items: center; justify-content: space-between; gap: .75rem; padding: .5rem .75rem; border-bottom: 1px solid var(--surface-border); background: var(--surface-50); }
.input-label { display: block; margin-bottom: .4rem; font-weight: 600; }
.input-editor { width: 100%; font-family: var(--font-family-monospace, monospace); }
.execution-summary { margin-top: 1rem; }
.artifact { max-height: 14rem; overflow: auto; padding: .75rem; border-radius: 6px; background: var(--surface-100); white-space: pre-wrap; }
.context-section { margin-top: 1rem; }
.context-row { display: grid; grid-template-columns: minmax(7rem, 1fr) auto auto; align-items: center; gap: .75rem; padding: .35rem 0; border-bottom: 1px solid var(--surface-border); }
.version-actions { display: flex; }
</style>
