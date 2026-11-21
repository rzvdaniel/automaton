<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { useToast } from 'primevue/usetoast'
import { appsService } from '@/features/apps/services/apps.service'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type { AdminAppDetails } from '../types/admin.types'
import { adminAppsService } from '../services/adminAppsService'
import type { AdminAppPermissionStatus, RunnerMaintenancePermission } from '../services/adminAppsService'

const props = defineProps<{ appId: string; appName: string; paused: boolean }>()
const emit = defineEmits<{ 'paused-changed': []; 'permissions-changed': [] }>()

const toast = useToast()
// Only system administrators approve permissions (the endpoint is SysAdmin-only).
const canApprove = computed(() => useAuthStore().user?.roles?.includes('SysAdmin') ?? false)

const details = ref<AdminAppDetails | null>(null)
const permissions = ref<AdminAppPermissionStatus[]>([])
const machines = ref<RunnerMaintenancePermission[]>([])
const saving = ref<string | null>(null)
const maintainApproved = computed(() => permissions.value.some((item) => item.permission === 'runner:maintain' && item.approved))

const reviewStatus = ref('approved')
const accessStatus = ref('active')
const moderationReason = ref('')
const savingModeration = ref(false)
const pausing = ref(false)
const REVIEW = [{ label: 'Approved', value: 'approved' }, { label: 'Flagged', value: 'flagged' }]
const ACCESS = [{ label: 'Active', value: 'active' }, { label: 'Hidden', value: 'hidden' }, { label: 'Disabled', value: 'disabled' }]

function fail(summary: string) {
  toast.add({ severity: 'error', summary, life: 3000 })
}

async function loadMachines() {
  machines.value = maintainApproved.value ? await adminAppsService.getRunnerPermissions(props.appId).catch(() => []) : []
}

async function load() {
  try {
    details.value = await adminAppsService.getApp(props.appId)
    reviewStatus.value = details.value.app.reviewStatus || 'approved'
    accessStatus.value = details.value.app.accessStatus || 'active'
    moderationReason.value = details.value.app.marketplaceModerationReason || ''
  } catch {
    fail('Failed to load admin app details')
  }
  if (!canApprove.value) return
  try {
    permissions.value = await adminAppsService.getPermissions(props.appId)
    await loadMachines()
  } catch {
    fail('Failed to load app permissions')
  }
}
onMounted(load)

const severity = (status: string) => status === 'Approved' ? 'success' : status === 'Pending' ? 'warn' : status === 'Rejected' || status === 'Revoked' ? 'danger' : 'secondary'

async function decide(item: AdminAppPermissionStatus, approved: boolean) {
  saving.value = item.permission
  try {
    permissions.value = await adminAppsService.setPermission(props.appId, item.permission, approved)
    emit('permissions-changed')
    await loadMachines()
    toast.add({ severity: 'success', summary: approved ? 'Permission approved' : item.approved ? 'Permission revoked' : 'Permission rejected', life: 2500 })
  } catch {
    fail('Permission update failed')
  } finally {
    saving.value = null
  }
}

async function decideMachine(machine: RunnerMaintenancePermission, approved: boolean) {
  saving.value = machine.runnerId
  try {
    machines.value = await adminAppsService.setRunnerPermission(props.appId, machine.runnerId, approved)
    toast.add({ severity: 'success', summary: approved ? 'Machine approved' : 'Machine revoked', life: 2500 })
  } catch {
    fail('Machine update failed')
  } finally {
    saving.value = null
  }
}

async function saveModeration() {
  savingModeration.value = true
  try {
    await adminAppsService.updateModeration(props.appId, {
      reviewStatus: reviewStatus.value,
      accessStatus: accessStatus.value,
      marketplaceModerationReason: moderationReason.value.trim() || undefined,
    })
    toast.add({ severity: 'success', summary: 'Moderation updated', life: 2500 })
  } catch {
    fail('Could not save moderation settings')
  } finally {
    savingModeration.value = false
  }
}

const pauseReason = ref('')
const askingReason = ref(false)

async function setPaused(pause: boolean, reason?: string) {
  pausing.value = true
  try {
    await appsService.setPaused(props.appId, pause, reason)
    toast.add({ severity: 'success', summary: pause ? 'Paused' : 'Resumed', life: 3000 })
    emit('paused-changed')
  } catch (error) {
    toast.add({ severity: 'error', summary: pause ? 'The app could not be paused' : 'The app could not be resumed', detail: error instanceof Error ? error.message : undefined, life: 5000 })
  } finally {
    pausing.value = false
    askingReason.value = false
    pauseReason.value = ''
  }
}

function askPause() {
  if (props.paused) return void setPaused(false)
  askingReason.value = !askingReason.value
}
</script>

<template>
  <div class="admin" data-testid="admin-tab">
    <p class="hint">Only administrators see this tab. Admin → Apps lists every app on the server and opens this page.</p>

    <section v-if="canApprove" class="card" aria-labelledby="perm-title" data-testid="admin-permissions">
      <h2 id="perm-title">Permission requests</h2>
      <p v-if="permissions.length === 0" class="hint">This app has not requested any permissions.</p>
      <div v-for="item in permissions" :key="item.permission" class="row">
        <code>{{ item.permission }}</code>
        <Tag :value="item.status" :severity="severity(item.status)" rounded />
        <span class="spacer"></span>
        <Button v-if="item.approved" label="Revoke" :aria-label="`Revoke ${item.permission}`" severity="danger" outlined size="small" :loading="saving === item.permission" :disabled="saving !== null" @click="decide(item, false)" />
        <template v-else>
          <Button v-if="item.status === 'Pending'" label="Deny" :aria-label="`Deny ${item.permission}`" severity="secondary" outlined size="small" :disabled="saving !== null" @click="decide(item, false)" />
          <Button label="Approve" :aria-label="`Approve ${item.permission}`" size="small" :loading="saving === item.permission" :disabled="saving !== null" @click="decide(item, true)" />
        </template>
      </div>
      <div v-if="maintainApproved" class="machines">
        <p class="hint">runner:maintain also needs an approval for each machine. Without it the app cannot run maintenance there.</p>
        <p v-if="machines.length === 0" class="hint">No machine with a Runner yet.</p>
        <div v-for="machine in machines" :key="machine.runnerId" class="row">
          <code>{{ machine.name }}</code>
          <Tag :value="machine.approved ? 'Approved' : 'Not approved'" :severity="machine.approved ? 'success' : 'warn'" rounded />
          <span class="spacer"></span>
          <Button v-if="machine.approved" label="Revoke" :aria-label="`Revoke maintenance on ${machine.name}`" severity="danger" outlined size="small" :loading="saving === machine.runnerId" :disabled="saving !== null" @click="decideMachine(machine, false)" />
          <Button v-else label="Approve" :aria-label="`Approve maintenance on ${machine.name}`" size="small" :loading="saving === machine.runnerId" :disabled="saving !== null" @click="decideMachine(machine, true)" />
        </div>
      </div>
    </section>

    <section class="card" aria-labelledby="mod-title" data-testid="admin-moderation">
      <div class="line">
        <div>
          <h2 id="mod-title">Moderation</h2>
          <p class="hint">Owner: {{ details?.ownerName || '…' }}<template v-if="details?.ownerEmail"> ({{ details.ownerEmail }})</template>. Pausing stops the app and shows visitors a paused page.</p>
        </div>
        <Button :label="paused ? 'Resume app' : 'Pause app…'" :icon="paused ? 'pi pi-play' : 'pi pi-pause'" severity="secondary" outlined :loading="pausing" data-testid="pause-app" @click="askPause" />
      </div>
      <div v-if="askingReason && !paused" class="field" data-testid="pause-form">
        <label>Reason (the owner sees it)<Textarea v-model="pauseReason" rows="2" auto-resize maxlength="500" placeholder="Why is this app being paused? Optional" data-testid="pause-reason" /></label>
        <div class="line">
          <Button label="Pause app" icon="pi pi-pause" severity="danger" :loading="pausing" data-testid="pause-confirm" @click="setPaused(true, pauseReason)" />
          <Button label="Cancel" severity="secondary" text @click="askingReason = false" />
        </div>
      </div>
      <div class="fields">
        <label>Review status<Select v-model="reviewStatus" :options="REVIEW" option-label="label" option-value="value" class="w-full" /></label>
        <label>Access status<Select v-model="accessStatus" :options="ACCESS" option-label="label" option-value="value" class="w-full" /></label>
      </div>
      <p class="hint">Hidden removes the app from the store and public access without blame. Disabled is for enforcement or safety blocks. Setting access back to Active republishes the public address.</p>
      <label class="field">Moderation note<Textarea v-model="moderationReason" rows="2" auto-resize placeholder="Optional moderation note" /></label>
      <div class="end"><Button label="Save moderation" icon="pi pi-save" outlined :loading="savingModeration" :disabled="!details" @click="saveModeration" /></div>
    </section>
  </div>
</template>

<style scoped>
.admin { display: flex; flex-direction: column; gap: 18px; }
.card { display: flex; flex-direction: column; gap: 10px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.line { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.row { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; padding: 6px 0; border-bottom: 1px solid var(--row-line); }
.spacer { flex: 1; }
code { font-family: var(--mono); font-size: 13px; }
.machines { display: flex; flex-direction: column; gap: 6px; margin-top: 6px; }
.fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }
.fields label, .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
.end { display: flex; justify-content: flex-end; }
</style>
