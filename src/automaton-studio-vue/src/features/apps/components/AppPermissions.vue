<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { appsService } from '../services/apps.service'
import type { AppPermissionsResponse } from '../types/apps.types'
import Button from 'primevue/button'
import { adminAppsService } from '@/features/admin/services/adminAppsService'
import { useAuthStore } from '@/features/auth/stores/auth.store'

const props = defineProps<{ appId: string }>()

const authStore = useAuthStore()
const state = ref<AppPermissionsResponse | null>(null)
const loading = ref(false)
const failed = ref(false)

const isAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)

const saving = ref<string | null>(null)
const actionFailed = ref(false)

async function setPermission(permission: string, approved: boolean) {
  saving.value = permission
  actionFailed.value = false
  try {
    await adminAppsService.setPermission(props.appId, permission, approved)
    await load()
  } catch {
    actionFailed.value = true
  } finally {
    saving.value = null
  }
}

function pillClass(status: string) {
  switch (status) {
    case 'Approved': return 'pill--ok'
    case 'Pending': return 'pill--wait'
    case 'Rejected':
    case 'Revoked': return 'pill--bad'
    default: return ''
  }
}

function label(status: string) {
  return status === 'Pending' ? 'waiting for approval' : status.toLowerCase()
}

async function load() {
  loading.value = true
  failed.value = false
  try {
    state.value = await appsService.getPermissions(props.appId)
  } catch {
    failed.value = true
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <section v-if="failed || (state && state.permissions.length > 0)" class="card" aria-labelledby="permissions-title">
    <h2 id="permissions-title">Permissions</h2>
    <p v-if="failed" class="hint">Could not load the app's permissions.</p>
    <template v-else-if="state">
      <div v-for="item in state.permissions" :key="item.permission" class="row">
        <code>{{ item.permission }}</code>
        <span class="pill" :class="pillClass(item.status)">{{ label(item.status) }}</span>
        <span v-if="isAdministrator" class="actions">
          <Button v-if="item.status === 'Approved'" label="Revoke" :aria-label="`Revoke ${item.permission}`" severity="danger" outlined size="small" :loading="saving === item.permission" :disabled="saving !== null" @click="setPermission(item.permission, false)" />
          <template v-else>
            <Button label="Approve" :aria-label="`Approve ${item.permission}`" size="small" :loading="saving === item.permission" :disabled="saving !== null" @click="setPermission(item.permission, true)" />
            <Button v-if="item.status === 'Pending'" label="Reject" :aria-label="`Reject ${item.permission}`" severity="secondary" outlined size="small" :disabled="saving !== null" @click="setPermission(item.permission, false)" />
          </template>
        </span>
      </div>
      <p v-if="actionFailed" class="hint" role="alert">Could not change the permission.</p>
      <p v-if="state.waiting.length > 0 && !isAdministrator" class="hint">
        A system administrator of this server must approve {{ state.waiting.join(', ') }}.
        <code v-if="state.approvalUrl" class="url">{{ state.approvalUrl }}</code>
      </p>
    </template>
  </section>
</template>

<style scoped>
.card { display: flex; flex-direction: column; gap: 10px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 0; border-bottom: 1px solid var(--row-line); }
code { font-family: var(--mono); font-size: 13px; }
.pill { padding: 3px 10px; border-radius: 999px; font-family: var(--mono); font-size: 12px; }
.pill--ok { border: 1px solid var(--ok); color: var(--ok); }
.pill--wait { border: 1px solid var(--signal); color: var(--signal-ink); }
.pill--bad { background: color-mix(in srgb, var(--signal) 18%, transparent); color: var(--signal-ink); }
.actions { display: flex; gap: 8px; }
.url { overflow-wrap: anywhere; }
</style>
