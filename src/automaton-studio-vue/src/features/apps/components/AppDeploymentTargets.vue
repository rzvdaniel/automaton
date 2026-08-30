<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Menu from 'primevue/menu'
import Message from 'primevue/message'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import type { MenuItem } from 'primevue/menuitem'
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
import { getEnvironmentDisplayName } from '../utils/environment-display'

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
const actionMenu = ref<InstanceType<typeof Menu> | null>(null)
const actionMenuTarget = ref<AppDeploymentTarget | null>(null)

const selectedRunner = computed(() => runners.value.find((runner) => runner.id === runnerId.value))
const selectedRunnerIsPrivileged = computed(() =>
  isPrivilegedSelfHostedRunner(selectedRunner.value)
)
const requiresPrivilegedAcknowledgement = computed(() =>
  requiresPrivilegedHostAcknowledgement(
    selectedRunner.value,
    runnerId.value,
    editingTarget.value?.runner?.id ?? null
  )
)
const runnerOptions = computed(() => getDeploymentTargetRunnerOptions(runners.value))
const liveTarget = computed(() => targets.value.find((target) => target.isDefault) ?? null)
const additionalTargets = computed(() => targets.value.filter((target) => !target.isDefault))
const hostedRunners = computed(() => runners.value.filter((runner) => runner.isHostedRunner))
const selfHostedRunnerOptions = computed(() =>
  runnerOptions.value.filter((option) => {
    const runner = runners.value.find((candidate) => candidate.id === option.value)
    return !runner?.isHostedRunner
  })
)
const hostedDestinationOption = computed(() => {
  const existingRunnerId = editingTarget.value?.runner?.isHostedRunner
    ? editingTarget.value.runner.id
    : null
  const runner =
    runners.value.find(
      (candidate) =>
        candidate.id === resolveAutomatonHostedRunnerId(runners.value, existingRunnerId)
    ) ?? null
  if (runner)
    return { label: 'Automaton Hosted', value: runner.id, disabled: runner.status !== 'Online' }
  return {
    label:
      hostedRunners.value.length > 1
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

const normalizedSlug = computed(() =>
  slug.value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
)

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
    showError(error, 'Failed to load environments')
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
  const suggestion =
    purpose.value === 'Staging'
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
      const hasRuntime = editingTarget.value.legacyControllerRuntimeDeployed ||
        editingTarget.value.genericRuntimeDeployed
      if (runnerChanged && hasRuntime) {
        confirm.require({
          header: 'Move deployed environment?',
          message:
            'Changing the Runner will stop and remove this environment runtime from its current Runner. Continue?',
          icon: 'pi pi-exclamation-triangle',
          acceptClass: 'p-button-danger',
          accept: () => void updateExistingTarget(true),
          reject: () => {
            busyTargetId.value = null
          },
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
      toast.add({
        severity: 'success',
        summary: 'Environment created',
        detail: `${name.value.trim()} is ready to configure.`,
        life: 2500,
      })
      await load()
    }
  } catch (error) {
    busyTargetId.value = null
    showError(error, 'Failed to save environment')
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
        privilegedHostAccessAcknowledged.value
      )
    }
    showEditor.value = false
    toast.add({
      severity: 'success',
      summary: 'Environment updated',
      detail: `${name.value.trim()} was updated.`,
      life: 2500,
    })
    await load()
  } catch (error) {
    showError(error, 'Failed to update environment')
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
    message:
      'This changes which environment is served through the App’s normal private and published URLs. It does not redeploy either environment.',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Promote to Live',
    accept: () => void promoteToLive(target),
  })
}

async function promoteToLive(target: AppDeploymentTarget) {
  busyTargetId.value = target.id
  try {
    await appsService.makeDefaultDeploymentTarget(props.appId, target.id)
    toast.add({
      severity: 'success',
      summary: 'Live environment changed',
      detail: `${target.name} now serves this app’s normal routes.`,
      life: 2500,
    })
    await load()
  } catch (error) {
    showError(error, 'Failed to promote the Live environment')
  } finally {
    busyTargetId.value = null
  }
}

function remove(target: AppDeploymentTarget) {
  confirm.require({
    header: 'Delete environment?',
    message: `${target.name} and its deployed runtime will be removed. Historical execution records are preserved.`,
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      busyTargetId.value = target.id
      try {
        await appsService.deleteDeploymentTarget(props.appId, target.id)
        toast.add({
          severity: 'success',
          summary: 'Environment deleted',
          detail: `${target.name} was deleted.`,
          life: 2500,
        })
        await load()
      } catch (error) {
        showError(error, 'Failed to delete environment')
      } finally {
        busyTargetId.value = null
      }
    },
  })
}

function statusClass(status: AppDeploymentTarget['status']): string {
  if (status === DeploymentTargetStatus.Running) return 'target-status--running'
  if (status === DeploymentTargetStatus.Failed) return 'target-status--failed'
  if (status === DeploymentTargetStatus.RunnerOffline) return 'target-status--offline'
  if (status === DeploymentTargetStatus.Deploying) return 'target-status--deploying'
  return 'target-status--quiet'
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

function isPrivilegedTarget(target: AppDeploymentTarget): boolean {
  return (
    !!target.runner &&
    !target.runner.isHostedRunner &&
    target.runner.applicationProfile === 'PrivilegedHost'
  )
}

function destinationLabel(target: AppDeploymentTarget): string {
  if (!target.runner) return 'No runtime assigned'
  return target.runner.isHostedRunner ? 'Automaton Hosted' : 'Self-hosted'
}

function runtimeTransport(target: AppDeploymentTarget): string {
  return target.runner?.isHostedRunner ? 'Direct edge routing' : 'API ↔ Runner tunnel'
}

function canOpenPreview(target: AppDeploymentTarget): boolean {
  return (
    !!target.runner &&
    target.genericRuntimeDeployed &&
    target.runner.isOnline &&
    target.status !== DeploymentTargetStatus.RunnerOffline
  )
}

function deploymentState(target: AppDeploymentTarget): string {
  if (target.genericRuntimeDeployed) return 'Generic runtime deployed'
  return target.legacyControllerRuntimeDeployed ? 'Legacy runtime deployed' : ''
}

function canPromote(target: AppDeploymentTarget): boolean {
  return (
    !!target.runner &&
    target.runner.isOnline &&
    target.genericRuntimeDeployed &&
    target.status !== DeploymentTargetStatus.Deploying
  )
}

function formatDeploymentTime(target: AppDeploymentTarget): string {
  if (!target.lastSuccessfulDeploymentAt) return 'Never deployed'
  const elapsedSeconds = Math.max(
    0,
    Math.round((Date.now() - new Date(target.lastSuccessfulDeploymentAt).getTime()) / 1000)
  )
  if (elapsedSeconds < 60) return 'Deployed just now'
  const units: Array<[Intl.RelativeTimeFormatUnit, number]> = [
    ['year', 31_536_000],
    ['month', 2_592_000],
    ['week', 604_800],
    ['day', 86_400],
    ['hour', 3_600],
    ['minute', 60],
  ]
  const [, unitSeconds] = units.find(([, seconds]) => elapsedSeconds >= seconds) ?? ['second', 1]
  const unit = units.find(([, seconds]) => seconds === unitSeconds)?.[0] ?? 'second'
  return `Deployed ${new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' }).format(-Math.round(elapsedSeconds / unitSeconds), unit)}`
}

function deploymentTimeTitle(target: AppDeploymentTarget): string | undefined {
  return target.lastSuccessfulDeploymentAt
    ? new Date(target.lastSuccessfulDeploymentAt).toLocaleString()
    : undefined
}

function toggleActionMenu(target: AppDeploymentTarget, event: Event) {
  actionMenuTarget.value = target
  actionMenu.value?.toggle(event)
}

const actionMenuItems = computed<MenuItem[]>(() => {
  const target = actionMenuTarget.value
  if (!target) return []
  return [
    { label: 'View logs', icon: 'pi pi-list', command: () => emit('viewLogs', target) },
    { label: 'Edit environment', icon: 'pi pi-pencil', command: () => openEdit(target) },
    ...(target.isDefault
      ? []
      : [
          {
            label: 'Delete environment',
            icon: 'pi pi-trash',
            class: 'p-menuitem-danger',
            command: () => remove(target),
          },
        ]),
  ]
})

function toggleAdvanced(target: AppDeploymentTarget) {
  advancedTargetIds.value = advancedTargetIds.value.includes(target.id)
    ? advancedTargetIds.value.filter((id) => id !== target.id)
    : [...advancedTargetIds.value, target.id]
}

function isAdvancedVisible(target: AppDeploymentTarget): boolean {
  return advancedTargetIds.value.includes(target.id)
}

function showError(error: unknown, fallback: string) {
  const detail =
    typeof error === 'object' && error && 'response' in error
      ? ((error as { response?: { data?: { message?: string; detail?: string } } }).response?.data
          ?.message ??
        (error as { response?: { data?: { detail?: string } } }).response?.data?.detail ??
        fallback)
      : error instanceof Error
        ? error.message
        : fallback
  toast.add({ severity: 'error', summary: 'Environment error', detail, life: 5000 })
}

onMounted(load)
watch(() => props.refreshRevision, load)
</script>

<template>
  <section class="targets-section">
    <div class="targets-header">
      <div>
        <h2>Environments</h2>
        <p>Manage the Live environment and optional named environments separately.</p>
      </div>
      <Button label="Add environment" icon="pi pi-plus" :disabled="isLoading" @click="openCreate" />
    </div>

    <div v-if="isLoading" class="targets-loading">
      <i class="pi pi-spin pi-spinner"></i> Loading environments…
    </div>
    <template v-else>
      <section class="target-group">
        <div class="target-group__heading">
          <h3>Live environment</h3>
          <p>The version served by the App’s normal private and published URLs.</p>
        </div>
        <article v-if="liveTarget" class="target-card target-card--live">
          <div class="target-main">
            <div class="target-title">
              <span class="target-name">{{ getEnvironmentDisplayName(liveTarget) }}</span>
              <Tag value="LIVE" severity="success" />
              <span class="target-status" :class="statusClass(liveTarget.status)"
                ><i class="pi pi-circle-fill"></i>{{ liveTarget.status }}</span
              >
            </div>
            <div class="target-destination">
              <i class="pi pi-server"></i><span>{{ destinationLabel(liveTarget) }}</span
              ><Tag
                v-if="isPrivilegedTarget(liveTarget)"
                value="Privileged access"
                icon="pi pi-shield"
                severity="warn"
              />
            </div>
            <div class="target-meta" :title="deploymentTimeTitle(liveTarget)">
              <span v-if="deploymentState(liveTarget)">{{ deploymentState(liveTarget) }}</span
              ><span v-if="deploymentState(liveTarget)">·</span
              ><span>{{ formatDeploymentTime(liveTarget) }}</span>
            </div>
            <Message v-if="liveTarget.cleanupPending" severity="warn" :closable="false"
              >Runtime cleanup needs retry.
              {{ liveTarget.lastCleanupError || 'The previous cleanup did not complete.' }}</Message
            >
            <Button
              :label="isAdvancedVisible(liveTarget) ? 'Hide advanced details' : 'Advanced details'"
              :icon="isAdvancedVisible(liveTarget) ? 'pi pi-chevron-up' : 'pi pi-chevron-down'"
              :aria-expanded="isAdvancedVisible(liveTarget)"
              size="small"
              text
              class="advanced-toggle"
              @click="toggleAdvanced(liveTarget)"
            />
            <div v-if="isAdvancedVisible(liveTarget)" class="advanced-details">
              <span>Environment slug: {{ liveTarget.slug }}</span
              ><span>Runner: {{ liveTarget.runner?.name ?? 'None' }}</span
              ><span>Runner ID: {{ liveTarget.runner?.id ?? 'None' }}</span
              ><span>Target ID: {{ liveTarget.id }}</span
              ><span>Runtime transport: {{ runtimeTransport(liveTarget) }}</span
              ><span>Runtime route: {{ targetRoute(liveTarget) }}</span
              ><span>Internal preview URL: {{ targetRoute(liveTarget) }}</span>
            </div>
          </div>
          <div class="target-actions">
            <Button
              label="Deploy"
              icon="pi pi-cloud-upload"
              size="small"
              :disabled="
                liveTarget.runner?.isOnline === false ||
                liveTarget.status === DeploymentTargetStatus.Deploying
              "
              @click="emit('deploy', liveTarget)"
            />
            <Button
              label="Open preview"
              icon="pi pi-external-link"
              size="small"
              outlined
              :disabled="!canOpenPreview(liveTarget)"
              :title="
                canOpenPreview(liveTarget)
                  ? undefined
                  : 'Preview is unavailable until the target is deployed and its Runner is online.'
              "
              @click="openRoute(liveTarget)"
            />
            <Button
              icon="pi pi-ellipsis-h"
              aria-label="More actions for Live environment"
              size="small"
              text
              @click="toggleActionMenu(liveTarget, $event)"
            />
          </div>
        </article>
      </section>

      <section class="target-group">
        <div class="target-group__heading">
          <h3>Additional environments</h3>
          <p>Use additional environments for staging, canary testing, or a different runtime.</p>
        </div>
        <Message v-if="additionalTargets.length === 0" severity="secondary" :closable="false"
          >No additional environments. Add one only when this app needs another named
          destination.</Message
        >
        <div v-else class="target-list">
          <article v-for="target in additionalTargets" :key="target.id" class="target-card">
            <div class="target-main">
              <div class="target-title">
                <span class="target-name">{{ getEnvironmentDisplayName(target) }}</span>
                <span class="target-status" :class="statusClass(target.status)"
                  ><i class="pi pi-circle-fill"></i>{{ target.status }}</span
                >
              </div>
              <div class="target-destination">
                <i class="pi pi-server"></i><span>{{ destinationLabel(target) }}</span
                ><Tag
                  v-if="isPrivilegedTarget(target)"
                  value="Privileged access"
                  icon="pi pi-shield"
                  severity="warn"
                />
              </div>
              <div class="target-meta" :title="deploymentTimeTitle(target)">
                <span v-if="deploymentState(target)">{{ deploymentState(target) }}</span
                ><span v-if="deploymentState(target)">·</span
                ><span>{{ formatDeploymentTime(target) }}</span>
              </div>
              <Message v-if="target.cleanupPending" severity="warn" :closable="false">
                Runtime cleanup needs retry.
                {{ target.lastCleanupError || 'The previous cleanup did not complete.' }}
              </Message>
              <Button
                :label="isAdvancedVisible(target) ? 'Hide advanced details' : 'Advanced details'"
                :icon="isAdvancedVisible(target) ? 'pi pi-chevron-up' : 'pi pi-chevron-down'"
                :aria-expanded="isAdvancedVisible(target)"
                size="small"
                text
                class="advanced-toggle"
                @click="toggleAdvanced(target)"
              />
              <div v-if="isAdvancedVisible(target)" class="advanced-details">
                <span>Environment slug: {{ target.slug }}</span
                ><span>Runner: {{ target.runner?.name ?? 'None' }}</span
                ><span>Runner ID: {{ target.runner?.id ?? 'None' }}</span
                ><span>Target ID: {{ target.id }}</span
                ><span>Runtime transport: {{ runtimeTransport(target) }}</span
                ><span>Runtime route: {{ targetRoute(target) }}</span
                ><span>Internal preview URL: {{ targetRoute(target) }}</span>
              </div>
            </div>
            <div class="target-actions">
              <Button
                label="Deploy"
                icon="pi pi-cloud-upload"
                size="small"
                :disabled="
                  !target.runner ||
                  target.runner?.isOnline === false ||
                  target.status === DeploymentTargetStatus.Deploying
                "
                @click="emit('deploy', target)"
              />
              <Button
                label="Open preview"
                icon="pi pi-external-link"
                size="small"
                outlined
                :disabled="!canOpenPreview(target)"
                :title="
                  canOpenPreview(target)
                    ? undefined
                    : 'Preview is unavailable until the target is deployed and its Runner is online.'
                "
                @click="openRoute(target)"
              />
              <Button
                label="Promote to Live"
                size="small"
                outlined
                class="promote-action"
                :disabled="!canPromote(target)"
                :title="
                  canPromote(target)
                    ? undefined
                    : 'Deploy this environment on an online runtime before promoting it to Live.'
                "
                :loading="busyTargetId === target.id"
                @click="makeDefault(target)"
              />
              <Button
                icon="pi pi-ellipsis-h"
                :aria-label="`More actions for ${getEnvironmentDisplayName(target)}`"
                size="small"
                text
                @click="toggleActionMenu(target, $event)"
              />
            </div>
          </article>
        </div>
      </section>
    </template>

    <Menu ref="actionMenu" :model="actionMenuItems" popup />

    <Dialog
      v-model:visible="showEditor"
      modal
      :header="editingTarget ? 'Edit environment' : 'Add environment'"
      class="target-dialog"
      :style="{ width: '34rem', maxWidth: 'calc(100vw - 2rem)' }"
    >
      <div class="form-field">
        <label for="target-name">Name</label>
        <InputText id="target-name" v-model="name" placeholder="Server EU" autofocus />
      </div>
      <div class="form-field">
        <label for="target-slug">Environment slug</label>
        <InputText id="target-slug" v-model="slug" placeholder="server-eu" />
        <small>Used by the CLI and deployment API: {{ normalizedSlug || 'server-eu' }}</small>
      </div>
      <div v-if="!editingTarget" class="form-field">
        <label for="target-purpose">Purpose</label>
        <Select
          id="target-purpose"
          v-model="purpose"
          :options="['Staging', 'Canary', 'Custom']"
          @change="applyPurposeSuggestion"
        />
        <small>Used to suggest a name and slug; it is not stored separately.</small>
      </div>
      <div class="form-field">
        <label for="target-runner">Destination</label>
        <Select
          id="target-runner"
          v-model="runnerId"
          :options="destinationRunnerOptions"
          option-label="label"
          option-value="value"
          option-disabled="disabled"
          show-clear
          placeholder="Assign later"
        />
        <small
          >Automaton Hosted does not expose the managed Runner identity. Self-hosted destinations
          are eligible application Runners only.</small
        >
      </div>
      <Message v-if="selectedRunnerIsPrivileged" severity="warn" :closable="false">
        <strong>Privileged Host access</strong><br />
        This App may read and modify host files, inspect host processes and Docker containers,
        affect services and system state, and access host secrets.
        <div v-if="requiresPrivilegedAcknowledgement" class="flex align-items-start gap-2 mt-3">
          <Checkbox
            v-model="privilegedHostAccessAcknowledged"
            input-id="privileged-host-acknowledgement"
            binary
          />
          <label for="privileged-host-acknowledgement"
            >I understand that this App will have administrator-level access to the selected
            machine.</label
          >
        </div>
        <small v-else class="block mt-3"
          >This existing Privileged Host assignment was previously confirmed.</small
        >
      </Message>
      <template #footer>
        <Button label="Cancel" text @click="showEditor = false" />
        <Button
          label="Save environment"
          :disabled="
            !name.trim() ||
            !normalizedSlug ||
            (requiresPrivilegedAcknowledgement && !privilegedHostAccessAcknowledged)
          "
          :loading="busyTargetId !== null"
          @click="saveTarget"
        />
      </template>
    </Dialog>
  </section>
</template>

<style scoped>
.targets-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.targets-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}
.targets-header h2 {
  margin: 0 0 0.35rem;
  font-size: 1.25rem;
}
.targets-header p {
  margin: 0;
  color: var(--p-text-muted-color);
}
.target-group {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
.target-group__heading h3 {
  margin: 0;
  font-size: 1rem;
}
.target-group__heading p {
  margin: 0.2rem 0 0;
  color: var(--p-text-muted-color);
}
.target-list {
  display: grid;
  gap: 0.85rem;
}
.target-card {
  display: flex;
  justify-content: space-between;
  gap: 1.5rem;
  padding: 1.25rem 1.35rem;
  border: 1px solid var(--p-content-border-color);
  border-radius: var(--p-border-radius-lg);
  background: var(--p-content-background);
}
.target-card--live {
  border-left: 4px solid var(--p-green-500);
  padding-left: calc(1.35rem - 3px);
  background: color-mix(in srgb, var(--p-green-50) 35%, var(--p-content-background));
}
.target-main {
  min-width: 0;
  flex: 1 1 auto;
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}
.target-title,
.target-meta,
.target-actions,
.target-destination {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.65rem;
}
.target-name {
  color: var(--p-text-color);
  font-size: 1.05rem;
  font-weight: 700;
}
.target-destination {
  color: var(--p-text-muted-color);
  font-size: 0.9rem;
}
.target-destination .pi {
  color: var(--p-text-secondary-color);
}
.target-status {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.85rem;
  font-weight: 600;
}
.target-status .pi-circle-fill {
  font-size: 0.45rem;
}
.target-status--running {
  color: var(--p-green-600);
}
.target-status--failed {
  color: var(--p-red-600);
}
.target-status--offline {
  color: var(--p-orange-600);
}
.target-status--deploying {
  color: var(--p-blue-600);
}
.target-status--quiet {
  color: var(--p-text-muted-color);
}
.target-meta {
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}
.target-actions {
  justify-content: flex-end;
  align-self: center;
  flex: 0 1 auto;
}
.target-actions .promote-action {
  margin-left: 0.35rem;
}
.advanced-toggle {
  align-self: flex-start;
  padding-left: 0;
  color: var(--p-text-secondary-color);
}
.route-link {
  padding: 0;
  border: 0;
  background: none;
  color: var(--p-primary-color);
  text-align: left;
  cursor: pointer;
  width: fit-content;
}
.advanced-details {
  display: grid;
  gap: 0.25rem;
  padding: 0.65rem;
  border-radius: 0.45rem;
  background: var(--p-surface-100);
  color: var(--p-text-muted-color);
  font-family: var(--p-font-family-monospace, monospace);
  font-size: 0.75rem;
  overflow-wrap: anywhere;
}
.live-route-note,
.form-field small {
  color: var(--p-text-muted-color);
  font-size: 0.8rem;
}
.targets-loading {
  padding: 2rem;
  text-align: center;
  color: var(--p-text-muted-color);
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  margin-bottom: 1rem;
}
.form-field label {
  font-weight: 600;
}
@media (max-width: 700px) {
  .targets-header,
  .target-card {
    flex-direction: column;
  }
  .target-actions {
    align-self: stretch;
    justify-content: flex-start;
  }
  .target-actions :deep(.p-button) {
    flex: 0 1 auto;
  }
  .target-actions .promote-action {
    margin-left: 0;
  }
}
</style>
