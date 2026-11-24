<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { useToast } from 'primevue/usetoast'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type { AdminAppDetails } from '../types/admin.types'
import { adminAppsService } from '../services/adminAppsService'
import type { AdminAppPermissionStatus, HostExecPermission, RunnerMaintenancePermission } from '../services/adminAppsService'

const props = defineProps<{ appId: string; appName: string }>()
const emit = defineEmits<{ 'permissions-changed': [] }>()

const toast = useToast()
// Only system administrators approve permissions (the endpoint is SysAdmin-only).
const canApprove = computed(() => useAuthStore().user?.roles?.includes('SysAdmin') ?? false)

const details = ref<AdminAppDetails | null>(null)
const permissions = ref<AdminAppPermissionStatus[]>([])
const machines = ref<RunnerMaintenancePermission[]>([])
const hostMachines = ref<HostExecPermission[]>([])
const hostEnabled = ref(false)
const saving = ref<string | null>(null)
const hostExecRequested = computed(() => permissions.value.some((item) => item.permission === 'runner:host-exec'))
const maintainApproved = computed(() => permissions.value.some((item) => item.permission === 'runner:maintain' && item.approved))

const reviewStatus = ref('approved')
const accessStatus = ref('active')
const moderationReason = ref('')
const savingModeration = ref(false)
const REVIEW = [{ label: 'Approved', value: 'approved' }, { label: 'Flagged', value: 'flagged' }]
const ACCESS = [{ label: 'Active', value: 'active' }, { label: 'Hidden', value: 'hidden' }, { label: 'Disabled', value: 'disabled' }]

function fail(summary: string) {
  toast.add({ severity: 'error', summary, life: 3000 })
}

async function loadMachines() {
  machines.value = maintainApproved.value ? await adminAppsService.getRunnerPermissions(props.appId).catch(() => []) : []
}

async function loadHostExec() {
  if (!hostExecRequested.value) { hostMachines.value = []; return }
  hostEnabled.value = await adminAppsService.getHostExecEnabled().catch(() => false)
  hostMachines.value = await adminAppsService.getHostExecPermissions(props.appId).catch(() => [])
}

async function toggleHostExec() {
  const turningOn = !hostEnabled.value
  if (turningOn && !window.confirm('Apps owned by a system administrator will be able to run ANY command as root on the machines you approve. Turn host access on?')) return
  saving.value = 'host-exec-switch'
  try {
    hostEnabled.value = await adminAppsService.setHostExecEnabled(turningOn)
    await loadHostExec()
    toast.add({ severity: 'success', summary: turningOn ? 'Host access turned on' : 'Host access turned off; approvals revoked', life: 3000 })
  } catch {
    fail('Host access switch failed')
  } finally {
    saving.value = null
  }
}

async function decideHostMachine(machine: HostExecPermission, approved: boolean) {
  if (approved && !window.confirm(`${props.appName} will be able to run any command as root on ${machine.name}. Approve for ${machine.currentCode}?`)) return
  saving.value = machine.runnerId
  try {
    hostMachines.value = await adminAppsService.setHostExecPermission(props.appId, machine.runnerId, approved)
    toast.add({ severity: 'success', summary: approved ? 'Machine approved' : 'Machine revoked', life: 2500 })
  } catch (error) {
    const message = (error as { response?: { data?: { message?: string } } }).response?.data?.message
    toast.add({ severity: 'error', summary: 'Machine update failed', detail: message, life: 5000 })
  } finally {
    saving.value = null
  }
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
    await loadHostExec()
  } catch {
    fail('Failed to load app permissions')
  }
}
onMounted(load)


async function decide(item: AdminAppPermissionStatus, approved: boolean) {
  saving.value = item.permission
  try {
    permissions.value = await adminAppsService.setPermission(props.appId, item.permission, approved)
    emit('permissions-changed')
    await loadMachines()
    await loadHostExec()
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

const waiting = computed(() => permissions.value.filter((item) => item.status === 'Pending'))
const decided = computed(() => permissions.value.filter((item) => item.status !== 'Pending'))
const find = ref('')
const showAll = ref(false)
const SHOWN = 5
const matches = computed(() => {
  const term = find.value.trim().toLowerCase()
  return term ? decided.value.filter((item) => item.permission.toLowerCase().includes(term)) : decided.value
})
const listed = computed(() => showAll.value || find.value.trim() ? matches.value : matches.value.slice(0, SHOWN))
const approvedCount = computed(() => permissions.value.filter((item) => item.approved).length)
const summary = computed(() => [
  { label: 'Owner', value: details.value?.ownerName || '…' },
  { label: 'Review', value: REVIEW.find((item) => item.value === reviewStatus.value)?.label ?? reviewStatus.value },
  { label: 'Access', value: ACCESS.find((item) => item.value === accessStatus.value)?.label ?? accessStatus.value },
  ...(canApprove.value ? [{ label: 'Permissions', value: waiting.value.length ? `${waiting.value.length} waiting · ${approvedCount.value} approved` : `${approvedCount.value} approved` }] : []),
])
</script>

<template>
  <div class="admin" data-testid="admin-tab">
    <dl class="summary" data-testid="admin-summary">
      <div v-for="item in summary" :key="item.label"><dt>{{ item.label }}</dt><dd>{{ item.value }}</dd></div>
    </dl>

    <div class="columns">
    <section v-if="canApprove" class="card" aria-labelledby="perm-title" data-testid="admin-permissions">
      <h2 id="perm-title">Permissions</h2>
      <p v-if="permissions.length === 0" class="hint">This app has not requested any permissions.</p>
      <div v-if="waiting.length" class="waiting" data-testid="admin-waiting">
        <div v-for="item in waiting" :key="item.permission" class="row">
          <span><code>{{ item.permission }}</code> <span class="needs">waiting</span></span>
          <span class="spacer"></span>
          <Button label="Decline" :aria-label="`Deny ${item.permission}`" severity="secondary" outlined size="small" :disabled="saving !== null" @click="decide(item, false)" />
          <Button label="Approve" :aria-label="`Approve ${item.permission}`" size="small" :loading="saving === item.permission" :disabled="saving !== null" @click="decide(item, true)" />
        </div>
      </div>
      <input v-if="decided.length > SHOWN" v-model="find" class="find" type="search" placeholder="Find a permission" aria-label="Find a permission" data-testid="admin-find">
      <div v-for="item in listed" :key="item.permission" class="row">
        <code>{{ item.permission }}</code>
        <span class="state">{{ item.status.toLowerCase() }}</span>
        <span class="spacer"></span>
        <Button v-if="item.approved" label="Revoke" :aria-label="`Revoke ${item.permission}`" class="revoke" text size="small" :loading="saving === item.permission" :disabled="saving !== null" @click="decide(item, false)" />
        <Button v-else label="Approve" :aria-label="`Approve ${item.permission}`" size="small" :loading="saving === item.permission" :disabled="saving !== null" @click="decide(item, true)" />
      </div>
      <button v-if="!find.trim() && matches.length > SHOWN" type="button" class="more" data-testid="admin-show-more" @click="showAll = !showAll">{{ showAll ? 'Show fewer' : `Show ${matches.length - SHOWN} more` }}</button>
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
      <div v-if="hostExecRequested" class="machines" data-testid="host-exec">
        <div class="row">
          <strong>Host access (runner:host-exec)</strong>
          <Tag :value="hostEnabled ? 'On for this server' : 'Off for this server'" :severity="hostEnabled ? 'danger' : 'secondary'" rounded />
          <span class="spacer"></span>
          <Button :label="hostEnabled ? 'Turn off' : 'Turn on…'" :severity="hostEnabled ? 'secondary' : 'danger'" outlined size="small" :loading="saving === 'host-exec-switch'" :disabled="saving !== null" data-testid="host-exec-switch" @click="toggleHostExec" />
        </div>
        <p class="hint">Warning: this app can run any command as root on a machine you approve. It works only for apps owned by a system administrator, and an approval ends when the app's code or owner changes. Turning the switch off revokes every approval.</p>
        <div v-for="machine in hostMachines" :key="machine.runnerId" class="row">
          <code>{{ machine.name }}</code>
          <Tag :value="machine.suspended ? 'Suspended: app changed' : machine.approved ? 'Approved' : 'Not approved'" :severity="machine.suspended ? 'warn' : machine.approved ? 'success' : 'secondary'" rounded />
          <span v-if="machine.suspended" class="hint">Approved for {{ machine.approvedFor }}; now {{ machine.currentCode }}</span>
          <span class="spacer"></span>
          <Button v-if="machine.approved" label="Revoke" :aria-label="`Revoke host commands on ${machine.name}`" severity="danger" outlined size="small" :disabled="saving !== null" @click="decideHostMachine(machine, false)" />
          <Button v-if="!machine.approved || machine.suspended" :label="machine.suspended ? 'Approve again' : 'Approve'" :aria-label="`Approve host commands on ${machine.name}`" size="small" :loading="saving === machine.runnerId" :disabled="saving !== null || !hostEnabled" @click="decideHostMachine(machine, true)" />
        </div>
      </div>
    </section>

    <section class="card" aria-labelledby="mod-title" data-testid="admin-moderation">
      <h2 id="mod-title">Moderation</h2>
      <p class="hint">Owner: {{ details?.ownerName || '…' }}<template v-if="details?.ownerEmail"> ({{ details.ownerEmail }})</template>. Pause the app from the ⋯ menu at the top of the page.</p>
      <div class="fields">
        <label>Review status<Select v-model="reviewStatus" :options="REVIEW" option-label="label" option-value="value" class="w-full" /></label>
        <label>Access status<Select v-model="accessStatus" :options="ACCESS" option-label="label" option-value="value" class="w-full" /></label>
      </div>
      <p class="hint">Hidden removes the app from the store and public access without blame. Disabled is for enforcement or safety blocks. Setting access back to Active republishes the public address.</p>
      <label class="field">Moderation note<Textarea v-model="moderationReason" rows="2" auto-resize placeholder="Optional moderation note" /></label>
      <div class="end"><Button label="Save moderation" icon="pi pi-save" outlined :loading="savingModeration" :disabled="!details" @click="saveModeration" /></div>
    </section>
    </div>
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
.summary { display: flex; flex-wrap: wrap; gap: 8px 32px; margin: 0; padding: 14px 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.summary dt { color: var(--ink-soft); font-size: 12px; }
.summary dd { margin: 0; font-weight: 600; }
.columns { display: grid; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); gap: 16px; align-items: start; }
.needs { color: var(--signal-ink); font-size: 13px; font-weight: 600; }
.state { color: var(--ink-soft); font-size: 13px; }
.waiting { padding: 4px 12px; border-radius: 12px; background: color-mix(in srgb, var(--signal) 14%, var(--paper)); }
.find { min-height: 36px; padding: 0 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--paper); color: var(--ink); font: inherit; }
.more { align-self: flex-start; padding: 0; border: 0; background: none; color: var(--signal-ink); font: inherit; text-decoration: underline; cursor: pointer; }
.revoke { color: var(--danger, #b42318); }
code { font-family: var(--mono); font-size: 13px; }
.machines { display: flex; flex-direction: column; gap: 6px; margin-top: 6px; }
.fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }
.fields label, .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
.end { display: flex; justify-content: flex-end; }
</style>
