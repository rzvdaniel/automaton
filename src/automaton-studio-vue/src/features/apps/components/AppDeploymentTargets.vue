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
import { RunnerApplicationProfiles, RunnerStatus, type Runner } from '@/features/runners/types/runners.types'
import { classifyRunner, isVisibleApplicationRunner } from '@/features/runners/utils/runner-classification'
import { appsService } from '../services/apps.service'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
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

const selectedRunner = computed(() => runners.value.find((runner) => runner.id === runnerId.value))
const selectedRunnerIsPrivileged = computed(() => selectedRunner.value?.applicationProfile === RunnerApplicationProfiles.PrivilegedHost && !selectedRunner.value.isHostedRunner)
const requiresPrivilegedAcknowledgement = computed(() => selectedRunnerIsPrivileged.value && runnerId.value !== (editingTarget.value?.runner?.id ?? null))
const runnerOptions = computed(() => runners.value.filter(isVisibleApplicationRunner).map((runner) => ({
  label: `${runner.name} · ${runnerSourceLabel(runner)} · ${runner.status}`,
  value: runner.id,
  disabled: runner.status !== RunnerStatus.Online,
})))

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
      runnersService.list(props.projectId ?? undefined),
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
  privilegedHostAccessAcknowledged.value = false
  showEditor.value = true
}

function openEdit(target: AppDeploymentTarget) {
  editingTarget.value = target
  name.value = target.name
  slug.value = target.slug
  runnerId.value = target.runner?.id ?? null
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

function runnerSourceLabel(runner: Runner) {
  switch (classifyRunner(runner)) {
    case 'automaton-hosted': return 'Automaton-hosted'
    case 'self-hosted-privileged': return 'Self-hosted · Privileged Host'
    default: return 'Self-hosted · Standard'
  }
}

watch(runnerId, () => { privilegedHostAccessAcknowledged.value = false })

async function makeDefault(target: AppDeploymentTarget) {
  busyTargetId.value = target.id
  try {
    await appsService.makeDefaultDeploymentTarget(props.appId, target.id)
    toast.add({ severity: 'success', summary: 'Default changed', detail: `${target.name} is now the default target.`, life: 2500 })
    await load()
  } catch (error) {
    showError(error, 'Failed to change the default target')
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
  if (target.isDefault) return null
  return getDeploymentTargetUrl(props.appId, target.routePath)
}

function openRoute(target: AppDeploymentTarget) {
  const route = targetRoute(target)
  if (route) window.open(route, '_blank', 'noopener,noreferrer')
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
        <p>Keep the default deployment simple, or add named targets for other Runners.</p>
      </div>
      <Button label="Add target" icon="pi pi-plus" :disabled="isLoading" @click="openCreate" />
    </div>

    <Message v-if="!isLoading && targets.length === 1" severity="secondary" :closable="false">
      You have one deployment target. Add another only when this app needs to run on a different Runner.
    </Message>

    <div v-if="isLoading" class="targets-loading"><i class="pi pi-spin pi-spinner"></i> Loading targets…</div>
    <div v-else class="target-list">
      <article v-for="target in targets" :key="target.id" class="target-card">
        <div class="target-main">
          <div class="target-title">
            <strong>{{ target.name }}</strong>
            <Tag v-if="target.isDefault" value="Default" severity="success" />
            <Tag :value="target.status" :severity="statusSeverity(target.status)" />
          </div>
          <code>{{ target.slug }}</code>
          <div class="target-meta">
            <span><i class="pi pi-server"></i> {{ target.runner?.name ?? 'No Runner assigned' }}</span>
            <span v-if="target.runner" :class="target.runner.isOnline ? 'online' : 'offline'">
              <i class="pi pi-circle-fill"></i> {{ target.runner.isOnline ? 'Online' : 'Offline' }}
            </span>
            <span><i class="pi pi-box"></i> {{ target.frontendDeployed || target.backendDeployed ? 'Runtime deployed' : 'Not deployed' }}</span>
          </div>
          <Message v-if="target.cleanupPending" severity="warn" :closable="false">
            Runtime cleanup needs retry. {{ target.lastCleanupError || 'The previous cleanup did not complete.' }}
          </Message>
          <button v-if="targetRoute(target)" class="route-link" type="button" @click="openRoute(target)">
            {{ target.routePath }} <i class="pi pi-external-link"></i>
          </button>
          <span v-else class="default-route-note">Uses the app’s normal private and published routes.</span>
        </div>
        <div class="target-actions">
          <Button label="Deploy" icon="pi pi-cloud-upload" size="small"
            :disabled="(!target.isDefault && !target.runner) || target.runner?.isOnline === false || target.status === DeploymentTargetStatus.Deploying"
            @click="emit('deploy', target)" />
          <Button label="Logs" icon="pi pi-list" size="small" outlined @click="emit('viewLogs', target)" />
          <Button icon="pi pi-pencil" aria-label="Edit target" size="small" text @click="openEdit(target)" />
          <Button v-if="!target.isDefault" label="Make default" size="small" text
            :loading="busyTargetId === target.id" @click="makeDefault(target)" />
          <Button v-if="!target.isDefault" icon="pi pi-trash" aria-label="Delete target" severity="danger"
            size="small" text :loading="busyTargetId === target.id" @click="remove(target)" />
        </div>
      </article>
    </div>

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
      <div class="form-field">
        <label for="target-runner">Runner</label>
        <Select id="target-runner" v-model="runnerId" :options="runnerOptions" option-label="label"
          option-value="value" option-disabled="disabled" show-clear placeholder="Assign later" />
      </div>
      <Message v-if="selectedRunnerIsPrivileged" severity="warn" :closable="false">
        <strong>Privileged Host access</strong><br>
        This App may read and modify host files, inspect host processes and Docker containers, affect services and system state, and access host secrets.
        <div class="flex align-items-start gap-2 mt-3">
          <Checkbox v-model="privilegedHostAccessAcknowledged" input-id="privileged-host-acknowledgement" binary />
          <label for="privileged-host-acknowledgement">I understand that this App will have administrator-level access to the selected machine.</label>
        </div>
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
.target-list { display: grid; gap: .85rem; }
.target-card { display: flex; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid var(--p-content-border-color); border-radius: var(--p-border-radius-lg); background: var(--p-content-background); }
.target-main { min-width: 0; display: flex; flex-direction: column; gap: .55rem; }
.target-title, .target-meta, .target-actions { display: flex; align-items: center; flex-wrap: wrap; gap: .65rem; }
.target-meta { color: var(--p-text-muted-color); font-size: .875rem; }
.target-meta .pi-circle-fill { font-size: .5rem; }
.online { color: var(--p-green-600); } .offline { color: var(--p-orange-600); }
.target-actions { justify-content: flex-end; align-self: center; }
.route-link { padding: 0; border: 0; background: none; color: var(--p-primary-color); text-align: left; cursor: pointer; width: fit-content; }
.default-route-note, .form-field small { color: var(--p-text-muted-color); font-size: .8rem; }
.targets-loading { padding: 2rem; text-align: center; color: var(--p-text-muted-color); }
.form-field { display: flex; flex-direction: column; gap: .4rem; margin-bottom: 1rem; }
.form-field label { font-weight: 600; }
@media (max-width: 700px) {
  .targets-header, .target-card { flex-direction: column; }
  .target-actions { align-self: stretch; justify-content: flex-start; }
  .target-actions :deep(.p-button) { flex: 1 1 auto; }
}
</style>
