<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import { appsService } from '../services/apps.service'
import type { AppPermissionsResponse } from '../types/apps.types'
import { useAuthStore } from '@/features/auth/stores/auth.store'

const props = defineProps<{ appId: string }>()

const authStore = useAuthStore()
const state = ref<AppPermissionsResponse | null>(null)
const loading = ref(false)
const failed = ref(false)

const isAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)

function severity(status: string) {
  switch (status) {
    case 'Approved': return 'success'
    case 'Pending': return 'warn'
    case 'Rejected':
    case 'Revoked': return 'danger'
    default: return 'secondary'
  }
}

function label(status: string) {
  return status === 'Pending' ? 'Waiting for approval' : status
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
  <div class="app-permissions">
    <p class="intro">
      An app asks for platform access in <code>automaton.json</code> under <code>application.permissions</code>.
      It gets that access only after a system administrator approves it; the app then receives its key
      as <code>APP_AUTOMATON_API_KEY</code> on its next start.
    </p>

    <Message v-if="failed" severity="error" :closable="false">Could not load the app's permissions.</Message>
    <div v-else-if="loading && !state" class="muted">Loading…</div>
    <div v-else-if="state && state.permissions.length === 0" class="muted">This app does not request any permissions.</div>

    <template v-else-if="state">
      <ul class="permission-list">
        <li v-for="item in state.permissions" :key="item.permission" class="permission-row">
          <code>{{ item.permission }}</code>
          <Tag :value="label(item.status)" :severity="severity(item.status)" />
        </li>
      </ul>

      <Message v-if="state.waiting.length > 0" severity="warn" :closable="false">
        <template v-if="isAdministrator">
          You can approve this as a system administrator.
        </template>
        <template v-else>
          A system administrator of this server must approve {{ state.waiting.join(', ') }}. Send them the approval link.
        </template>
        <div v-if="state.approvalUrl" class="approval-link">
          <Button
            v-if="isAdministrator"
            as="a"
            :href="state.approvalUrl"
            label="Review and approve"
            icon="pi pi-arrow-right"
            icon-pos="right"
            size="small"
          />
          <code v-else class="url">{{ state.approvalUrl }}</code>
        </div>
      </Message>
    </template>
  </div>
</template>

<style scoped>
.app-permissions {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.intro,
.muted {
  margin: 0;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

.permission-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.permission-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.approval-link {
  margin-top: 0.5rem;
}

.url {
  overflow-wrap: anywhere;
}
</style>
