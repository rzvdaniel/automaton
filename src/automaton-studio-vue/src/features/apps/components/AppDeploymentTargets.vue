<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { runnersService } from '@/features/runners/services/runners.service'
import { type Runner } from '@/features/runners/types/runners.types'
import { appsService } from '../services/apps.service'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import {
  getDeploymentTargetRunnerOptions,
  getDeploymentTargetRunnerProjectId,
  isPrivilegedSelfHostedRunner,
  requiresPrivilegedHostAcknowledgement,
  resolveAutomatonHostedRunnerId,
  shouldResetPrivilegedHostAcknowledgement,
} from '../utils/deployment-target-runner'
import { getDeploymentTargetUrl } from '../utils/public-app-url'

const props = defineProps<{ appId: string; projectId?: string | null; refreshRevision?: number }>()
const emit = defineEmits<{
  deploy: [target: AppDeploymentTarget]
  viewLogs: [target: AppDeploymentTarget]
  targetsChanged: [targets: AppDeploymentTarget[]]
}>()

const confirm = useConfirm()
const toast = useToast()
const targets = ref<AppDeploymentTarget[]>([])
const runners = ref<Runner[]>([])
const isLoading = ref(true)
const busyTargetId = ref<string | null>(null)
const showEditor = ref(false)
const editingTarget = ref<AppDeploymentTarget | null>(null)
const name = ref('')
const slug = ref('')
const runnerId = ref<string | null>(null)
const privilegedHostAccessAcknowledged = ref(false)
const purpose = ref<'Staging' | 'Canary' | 'Custom'>('Custom')
const advancedTargetIds = ref<string[]>([])

const selectedRunner = computed(() => runners.value.find((runner) => runner.id === runnerId.value))
const selectedRunnerIsPrivileged = computed(() => isPrivilegedSelfHostedRunner(selectedRunner.value))
const requiresPrivilegedAcknowledgement = computed(() => requiresPrivilegedHostAcknowledgement(
  selectedRunner.value,
  runnerId.value,
  editingTarget.value?.runner?.id ?? null,
))
const runnerOptions = computed(() => getDeploymentTargetRunnerOptions(runners.value))
const liveTarget = computed(() => targets.value.find((target) => target.isDefault) ?? null)
const additionalTargets = computed(() => targets.value.filter((target) => !target.isDefault))
const hostedRunners = computed(() => runners.value.filter((runner) => runner.isHostedRunner))
const selfHostedRunnerOptions = computed(() => runnerOptions.value.filter((option) => {
  const runner = runners.value.find((candidate) => candidate.id === option.value)
  return !runner?.isHostedRunner
}))
const hostedDestinationOption = computed(() => {
  const existingRunnerId = editingTarget.value?.runner?.isHostedRunner ? editingTarget.value.runner.id : null
  const runner = runners.value.find((candidate) => candidate.id === resolveAutomatonHostedRunnerId(runners.value, existingRunnerId)) ?? null
  if (runner) return { label: 'Automaton Hosted', value: runner.id, disabled: runner.status !== 'Online' }
  return {
    label: hostedRunners.value.length > 1
      ? 'Automaton Hosted (multiple managed Runners; unavailable here)'
      : 'Automaton Hosted (unavailable)',
    value: '__automaton-hosted-unavailable__',
    disabled: true,
  }
})
const destinationRunnerOptions = computed(() => [
  hostedDestinationOption.value,
  ...selfHostedRunnerOptions.value,
])

const normalizedSlug = computed(() => slug.value
  .trim()
  .toLowerCase()
  .replace(/[^a-z0-9]+/g, '-')
  .replace(/^-+|-+$/g, ''))

async function load() {
  isLoading.value = true
  try {
    const [targetData, runnerData] = await Promise.all([
      appsService.listDeploymentTargets(props.appId),
      runnersService.list(getDeploymentTargetRunnerProjectId(props.projectId)),
    ])
    targets.value = targetData
    runners.value = runnerData
    emit('targetsChanged', targetData)
  } catch (error) {
    showError(error, 'Failed to load deployment targets')
  } finally {
    isLoading.value = false
  }
}

function openCreate() {
  editingTarget.value = null
  name.value = ''
  slug.value = ''
  runnerId.value = null
  purpose.value = 'Custom'
  privilegedHostAccessAcknowledged.value = false
  showEditor.value = true
}

function applyPurposeSuggestion() {
  if (purpose.value === 'Custom') return
  const suggestion = purpose.value === 'Staging'
    ? { name: 'Staging', slug: 'staging' }
    : { name: 'Canary', slug: 'canary' }
  if (!name.value.trim()) name.value = suggestion.name
  if (!slug.value.trim()) slug.value = suggestion.slug
}

function openEdit(target: AppDeploymentTarget) {
  editingTarget.value = target
  name.value = target.name
  slug.value = target.slug
  runnerId.value = target.runner?.id ?? null
  purpose.value = 'Custom'
  privilegedHostAccessAcknowledged.value = false
  showEditor.value = true
}

async function saveTarget() {
  if (!name.value.trim() || !normalizedSlug.value) return
  const targetId = editingTarget.value?.id ?? 'new'
  busyTargetId.value = targetId
  try {
    if (editingTarget.value) {
      const runnerChanged = runnerId.value !== (editingTarget.value.runner?.id ?? null)
      const hasRuntime = editingTarget.value.frontendDeployed || editingTarget.value.backendDeployed
      if (runnerChanged && hasRuntime) {
        confirm.require({
          header: 'Move deployed target?',
          message: 'Changing the Runner will stop and remove this target runtime from its current Runner. Continue?',
          icon: 'pi pi-exclamation-triangle',
          acceptClass: 'p-button-danger',
          accept: () => void updateExistingTarget(true),
          reject: () => { busyTargetId.value = null },
        })
        return
      }
      await updateExistingTarget(false)
    } else {
      await appsService.createDeploymentTarget(props.appId, {
        name: name.value.trim(),
        slug: normalizedSlug.value,
        runnerId: runnerId.value,
        privilegedHostAccessAcknowledged: privilegedHostAccessAcknowledged.value,
      })
      showEditor.value = false
      toast.add({ severity: 'success', summary: 'Target created', detail: `${name.value.trim()} is ready to configure.`, life: 2500 })
      await load()
    }
  } catch (error) {
    busyTargetId.value = null
    showError(error, 'Failed to save deployment target')
  }
}

async function updateExistingTarget(cleanupDeployedRuntime: boolean) {
  const target = editingTarget.value
  if (!target) return
  try {
    await appsService.updateDeploymentTarget(props.appId, target.id, {
      name: name.value.trim(),
      slug: normalizedSlug.value,
    })
    if (runnerId.value !== (target.runner?.id ?? null)) {
      await appsService.assignDeploymentTargetRunner(
        props.appId,
        target.id,
        runnerId.value,
        cleanupDeployedRuntime,
        privilegedHostAccessAcknowledged.value,
      )
    }
    showEditor.value = false
    toast.add({ severity: 'success', summary: 'Target updated', detail: `${name.value.trim()} was updated.`, life: 2500 })
    await load()
  } catch (error) {
    showError(error, 'Failed to update deployment target')
  } finally {
    busyTargetId.value = null
  }
}

watch(runnerId, (nextRunnerId, previousRunnerId) => {
  if (shouldResetPrivilegedHostAcknowledgement(nextRunnerId, previousRunnerId)) {
    privilegedHostAccessAcknowledged.value = false
  }
})

function makeDefault(target: AppDeploymentTarget) {
  confirm.require({
    header: 'Promote to Live?',
    message: 'This changes which deployed target is served through the App’s normal private and published URLs. It does not redeploy either target.',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Promote to Live',
    accept: () => void promoteToLive(target),
  })
}

async function promoteToLive(target: AppDeploymentTarget) {
  busyTargetId.value = target.id
  try {
    await appsService.makeDefaultDeploymentTarget(props.appId, target.id)
    toast.add({ severity: 'success', summary: 'Live target changed', detail: `${target.name} now serves this app’s normal routes.`, life: 2500 })
    await load()
  } catch (error) {
    showError(error, 'Failed to promote the Live target')
  } finally {
    busyTargetId.value = null
  }
}

function remove(target: AppDeploymentTarget) {
  confirm.require({
    header: 'Delete deployment target?',
    message: `${target.name} and its deployed runtime will be removed. Historical execution records are preserved.`,
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      busyTargetId.value = target.id
      try {
        await appsService.deleteDeploymentTarget(props.appId, target.id)
        toast.add({ severity: 'success', summary: 'Target deleted', detail: `${target.name} was deleted.`, life: 2500 })
        await load()
      } catch (error) {
        showError(error, 'Failed to delete deployment target')
      } finally {
        busyTargetId.value = null
      }
    },
  })
}

function statusSeverity(status: AppDeploymentTarget['status']) {
  if (status === DeploymentTargetStatus.Running) return 'success'
  if (status === DeploymentTargetStatus.Failed) return 'danger'
  if (status === DeploymentTargetStatus.RunnerOffline) return 'warn'
  if (status === DeploymentTargetStatus.Deploying) return 'info'
  return 'secondary'
}

function targetRoute(target: AppDeploymentTarget) {
  return target.routePath
}

function targetPreviewUrl(target: AppDeploymentTarget) {
  return getDeploymentTargetUrl(props.appId, target.routePath)
}

function openRoute(target: AppDeploymentTarget) {
  const route = targetPreviewUrl(target)
  if (route) window.open(route, '_blank', 'noopener,noreferrer')
}

function destinationLabel(target: AppDeploymentTarget): string {
  if (!target.runner) return 'No destination assigned'
  if (target.runner.isHostedRunner) return 'Automaton Hosted'
  return target.runner.applicationProfile === 'PrivilegedHost'
    ? 'Self-hosted · Privileged Host'
    : 'Self-hosted · Standard'
}

function runtimeTransport(target: AppDeploymentTarget): string {
  return target.runner?.isHostedRunner ? 'Direct edge routing' : 'API ↔ Runner tunnel'
}

function canOpenPreview(target: AppDeploymentTarget): boolean {
  return !!target.runner && target.frontendDeployed && target.runner.isOnline && target.status !== DeploymentTargetStatus.RunnerOffline
}

function deploymentState(target: AppDeploymentTarget): string {
  if (!target.frontendDeployed && !target.backendDeployed) return 'Not deployed'
  if (target.frontendDeployed && target.backendDeployed) return 'Frontend and backend deployed'
  return target.frontendDeployed ? 'Frontend deployed' : 'Backend deployed'
}

function formatDeploymentTime(target: AppDeploymentTarget): string {
  return target.lastSuccessfulDeploymentAt
    ? new Date(target.lastSuccessfulDeploymentAt).toLocaleString()
    : 'Never deployed'
}

function toggleAdvanced(target: AppDeploymentTarget) {
  advancedTargetIds.value = advancedTargetIds.value.includes(target.id)
    ? advancedTargetIds.value.filter((id) => id !== target.id)
    : [...advancedTargetIds.value, target.id]
}

function isAdvancedVisible(target: AppDeploymentTarget): boolean {
  return advancedTargetIds.value.includes(target.id)
}

function showError(error: unknown, fallback: string) {
  const detail = typeof error === 'object' && error && 'response' in error
    ? (error as { response?: { data?: { message?: string; detail?: string } } }).response?.data?.message
      ?? (error as { response?: { data?: { detail?: string } } }).response?.data?.detail
      ?? fallback
    : error instanceof Error ? error.message : fallback
  toast.add({ severity: 'error', summary: 'Deployment target error', detail, life: 5000 })
}

onMounted(load)
watch(() => props.refreshRevision, load)
</script>

<template>
  <section class="targets-section">
    <div class="targets-header">
      <div>
        <h2>Deployment Targets</h2>
        <p>Manage the Live deployment and optional named destinations separately.</p>
      </div>
      <Button label="Add target" icon="pi pi-plus" :disabled="isLoading" @click="openCreate" />
    </div>

    <div v-if="isLoading" class="targets-loading"><i class="pi pi-spin pi-spinner"></i> Loading targets…</div>
    <template v-else>
      <section class="target-group">
        <div class="target-group__heading">
          <h3>Live deployment</h3>
          <p>The version served by the App’s normal private and published URLs.</p>
        </div>
        <article v-if="liveTarget" class="target-card target-card--live">
          <div class="target-main">
            <div class="target-title"><strong>{{ liveTarget.name }}</strong><Tag value="Live" severity="success" /><Tag :value="liveTarget.status" :severity="statusSeverity(liveTarget.status)" /></div>
            <strong class="target-destination">{{ destinationLabel(liveTarget) }}</strong>
            <div class="target-meta"><span>Last deployed: {{ formatDeploymentTime(liveTarget) }}</span><span>{{ deploymentState(liveTarget) }}</span></div>
            <Message v-if="liveTarget.cleanupPending" severity="warn" :closable="false">Runtime cleanup needs retry. {{ liveTarget.lastCleanupError || 'The previous cleanup did not complete.' }}</Message>
            <Button label="Advanced" icon="pi pi-chevron-down" size="small" text @click="toggleAdvanced(liveTarget)" />
            <div v-if="isAdvancedVisible(liveTarget)" class="advanced-details"><span>Runner: {{ liveTarget.runner?.name ?? 'None' }}</span><span>Runner ID: {{ liveTarget.runner?.id ?? 'None' }}</span><span>Target ID: {{ liveTarget.id }}</span><span>Runtime transport: {{ runtimeTransport(liveTarget) }}</span><span>Runtime route: {{ targetRoute(liveTarget) }}</span><span>Internal preview URL: {{ targetRoute(liveTarget) }}</span></div>
          </div>
          <div class="target-actions"><Button label="Deploy" icon="pi pi-cloud-upload" size="small" :disabled="liveTarget.runner?.isOnline === false || liveTarget.status === DeploymentTargetStatus.Deploying" @click="emit('deploy', liveTarget)" /><Button label="Open preview" icon="pi pi-external-link" size="small" outlined :disabled="!canOpenPreview(liveTarget)" :title="canOpenPreview(liveTarget) ? undefined : 'Preview is unavailable until the target is deployed and its Runner is online.'" @click="openRoute(liveTarget)" /><Button label="Logs" icon="pi pi-list" size="small" outlined @click="emit('viewLogs', liveTarget)" /><Button label="Edit" icon="pi pi-pencil" size="small" text @click="openEdit(liveTarget)" /></div>
        </article>
      </section>

      <section class="target-group">
        <div class="target-group__heading"><h3>Additional targets</h3><p>Use additional targets for staging, canary testing, or a different self-hosted runtime.</p></div>
        <Message v-if="additionalTargets.length === 0" severity="secondary" :closable="false">No additional targets. Add one only when this app needs another named destination.</Message>
        <div v-else class="target-list">
          <article v-for="target in additionalTargets" :key="target.id" class="target-card">
        <div class="target-main">
          <div class="target-title">
            <strong>{{ target.name }}</strong>
            <Tag :value="target.status" :severity="statusSeverity(target.status)" />
          </div>
          <strong class="target-destination">{{ destinationLabel(target) }}</strong>
          <div class="target-meta"><span>Last deployed: {{ formatDeploymentTime(target) }}</span><span>{{ deploymentState(target) }}</span></div>
          <Message v-if="target.cleanupPending" severity="warn" :closable="false">
            Runtime cleanup needs retry. {{ target.lastCleanupError || 'The previous cleanup did not complete.' }}
          </Message>
          <Button label="Advanced" icon="pi pi-chevron-down" size="small" text @click="toggleAdvanced(target)" />
          <div v-if="isAdvancedVisible(target)" class="advanced-details"><span>Runner: {{ target.runner?.name ?? 'None' }}</span><span>Runner ID: {{ target.runner?.id ?? 'None' }}</span><span>Target ID: {{ target.id }}</span><span>Runtime transport: {{ runtimeTransport(target) }}</span><span>Runtime route: {{ targetRoute(target) }}</span><span>Internal preview URL: {{ targetRoute(target) }}</span></div>
        </div>
        <div class="target-actions">
          <Button label="Deploy" icon="pi pi-cloud-upload" size="small"
            :disabled="!target.runner || target.runner?.isOnline === false || target.status === DeploymentTargetStatus.Deploying"
            @click="emit('deploy', target)" />
          <Button label="Open preview" icon="pi pi-external-link" size="small" outlined :disabled="!canOpenPreview(target)" :title="canOpenPreview(target) ? undefined : 'Preview is unavailable until the target is deployed and its Runner is online.'" @click="openRoute(target)" />
          <Button label="Logs" icon="pi pi-list" size="small" outlined @click="emit('viewLogs', target)" />
          <Button icon="pi pi-pencil" aria-label="Edit target" size="small" text @click="openEdit(target)" />
          <Button label="Promote to Live" size="small" text
            :loading="busyTargetId === target.id" @click="makeDefault(target)" />
          <Button icon="pi pi-trash" aria-label="Delete target" severity="danger"
            size="small" text :loading="busyTargetId === target.id" @click="remove(target)" />
        </div>
      </article>
        </div>
      </section>
    </template>

    <Dialog v-model:visible="showEditor" modal :header="editingTarget ? 'Edit deployment target' : 'Add deployment target'"
      class="target-dialog" :style="{ width: '34rem', maxWidth: 'calc(100vw - 2rem)' }">
      <div class="form-field">
        <label for="target-name">Name</label>
        <InputText id="target-name" v-model="name" placeholder="Server EU" autofocus />
      </div>
      <div class="form-field">
        <label for="target-slug">Slug</label>
        <InputText id="target-slug" v-model="slug" placeholder="server-eu" />
        <small>Used by the CLI and deployment API: {{ normalizedSlug || 'server-eu' }}</small>
      </div>
      <div v-if="!editingTarget" class="form-field">
        <label for="target-purpose">Purpose</label>
        <Select id="target-purpose" v-model="purpose" :options="['Staging', 'Canary', 'Custom']" @change="applyPurposeSuggestion" />
        <small>Used to suggest a name and slug; it is not stored separately.</small>
      </div>
      <div class="form-field">
        <label for="target-runner">Destination</label>
        <Select id="target-runner" v-model="runnerId" :options="destinationRunnerOptions" option-label="label"
          option-value="value" option-disabled="disabled" show-clear placeholder="Assign later" />
        <small>Automaton Hosted does not expose the managed Runner identity. Self-hosted destinations are eligible application Runners only.</small>
      </div>
      <Message v-if="selectedRunnerIsPrivileged" severity="warn" :closable="false">
        <strong>Privileged Host access</strong><br>
        This App may read and modify host files, inspect host processes and Docker containers, affect services and system state, and access host secrets.
        <div v-if="requiresPrivilegedAcknowledgement" class="flex align-items-start gap-2 mt-3">
          <Checkbox v-model="privilegedHostAccessAcknowledged" input-id="privileged-host-acknowledgement" binary />
          <label for="privileged-host-acknowledgement">I understand that this App will have administrator-level access to the selected machine.</label>
        </div>
        <small v-else class="block mt-3">This existing Privileged Host assignment was previously confirmed.</small>
      </Message>
      <template #footer>
        <Button label="Cancel" text @click="showEditor = false" />
        <Button label="Save target" :disabled="!name.trim() || !normalizedSlug || (requiresPrivilegedAcknowledgement && !privilegedHostAccessAcknowledged)"
          :loading="busyTargetId !== null" @click="saveTarget" />
      </template>
    </Dialog>
  </section>
</template>

<style scoped>
.targets-section { display: flex; flex-direction: column; gap: 1rem; }
.targets-header { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
.targets-header h2 { margin: 0 0 .35rem; font-size: 1.25rem; }
.targets-header p { margin: 0; color: var(--p-text-muted-color); }
.target-group { display: flex; flex-direction: column; gap: .75rem; }
.target-group__heading h3 { margin: 0; font-size: 1rem; }
.target-group__heading p { margin: .2rem 0 0; color: var(--p-text-muted-color); }
.target-list { display: grid; gap: .85rem; }
.target-card { display: flex; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid var(--p-content-border-color); border-radius: var(--p-border-radius-lg); background: var(--p-content-background); }
.target-card--live { border-color: color-mix(in srgb, var(--p-green-500) 45%, var(--p-content-border-color)); }
.target-main { min-width: 0; display: flex; flex-direction: column; gap: .55rem; }
.target-destination { font-size: .9rem; }
.target-title, .target-meta, .target-actions { display: flex; align-items: center; flex-wrap: wrap; gap: .65rem; }
.target-meta { color: var(--p-text-muted-color); font-size: .875rem; }
.target-meta .pi-circle-fill { font-size: .5rem; }
.online { color: var(--p-green-600); } .offline { color: var(--p-orange-600); }
.target-actions { justify-content: flex-end; align-self: center; }
.route-link { padding: 0; border: 0; background: none; color: var(--p-primary-color); text-align: left; cursor: pointer; width: fit-content; }
.advanced-details { display: grid; gap: .25rem; padding: .65rem; border-radius: .45rem; background: var(--p-surface-100); color: var(--p-text-muted-color); font-family: var(--p-font-family-monospace, monospace); font-size: .75rem; overflow-wrap: anywhere; }
.live-route-note, .form-field small { color: var(--p-text-muted-color); font-size: .8rem; }
.targets-loading { padding: 2rem; text-align: center; color: var(--p-text-muted-color); }
.form-field { display: flex; flex-direction: column; gap: .4rem; margin-bottom: 1rem; }
.form-field label { font-weight: 600; }
@media (max-width: 700px) {
  .targets-header, .target-card { flex-direction: column; }
  .target-actions { align-self: stretch; justify-content: flex-start; }
  .target-actions :deep(.p-button) { flex: 1 1 auto; }
}
</style>
