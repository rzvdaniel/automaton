<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Select from 'primevue/select'
import { adminUsersService } from '@/features/admin/services/adminUsersService'
import { organizationsService } from '@/features/organizations/services/organizationsService'
import type { OrganizationListItem, OrganizationRoleType } from '@/features/organizations/types'

const props = defineProps<{
  /** Teams the viewer administers: the only ones they can invite into. */
  teams: OrganizationListItem[]
  /** Administrators create the account; everyone else can only invite into their teams. */
  canInviteAccounts: boolean
}>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ done: [] }>()

const roles: { label: string; value: OrganizationRoleType }[] = [
  { label: 'App user (uses shared apps)', value: 'AppUser' },
  { label: 'Developer (builds apps)', value: 'Developer' },
  { label: 'Team admin (manages the team)', value: 'OrgAdmin' },
]

const email = ref('')
const name = ref('')
const teamId = ref<string | null>(null)
const role = ref<OrganizationRoleType>('AppUser')
const saving = ref(false)
const error = ref<string | null>(null)
const messages = ref<string[]>([])

const canSubmit = computed(() =>
  email.value.includes('@') && (props.canInviteAccounts || teamId.value !== null) && !saving.value)

function reset() {
  email.value = ''
  name.value = ''
  teamId.value = null
  role.value = 'AppUser'
  error.value = null
  messages.value = []
}

watch(visible, (open) => {
  if (!open) reset()
})

function messageOf(err: unknown, fallback: string) {
  const response = (err as { response?: { status?: number; data?: { message?: string } } }).response
  return response?.data?.message ?? fallback
}

async function submit() {
  saving.value = true
  error.value = null
  messages.value = []
  const address = email.value.trim()
  try {
    if (props.canInviteAccounts) {
      try {
        const account = await adminUsersService.inviteUser({ email: address, name: name.value.trim() || undefined })
        messages.value.push(account.warning ?? `${account.resent ? 'Invitation sent again' : 'Invitation sent'} to ${address}.`)
      } catch (err) {
        // An existing account is fine when a team was chosen: it is invited into the team below.
        const conflict = (err as { response?: { status?: number } }).response?.status === 409
        if (!conflict || !teamId.value) throw err
        messages.value.push(`${address} already has an account.`)
      }
    }
    if (teamId.value) {
      await organizationsService.inviteMember(teamId.value, { email: address, role: role.value })
      const team = props.teams.find((t) => t.id === teamId.value)
      messages.value.push(`Invited to ${team?.name ?? 'the team'}. They join when they accept.`)
    }
    emit('done')
  } catch (err) {
    error.value = messageOf(err, 'The invitation could not be sent.')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Invite people" :style="{ width: '30rem' }" :breakpoints="{ '640px': '95vw' }">
    <div class="form">
      <p class="hint">
        <template v-if="canInviteAccounts">
          They get an email with a link to set a password. Pick a team to add them to it as well.
        </template>
        <template v-else>They get an email invitation to join the team you pick.</template>
      </p>
      <label class="field">
        <span>Email</span>
        <InputText v-model="email" type="email" autocomplete="off" placeholder="name@example.com" />
      </label>
      <label v-if="canInviteAccounts" class="field">
        <span>Name <small>(optional)</small></span>
        <InputText v-model="name" autocomplete="off" />
      </label>
      <label class="field">
        <span>Team <small v-if="canInviteAccounts">(optional)</small></span>
        <Select v-model="teamId" :options="teams" option-label="name" option-value="id" :show-clear="canInviteAccounts" placeholder="Choose a team" />
      </label>
      <label v-if="teamId" class="field">
        <span>Role in the team</span>
        <Select v-model="role" :options="roles" option-label="label" option-value="value" />
      </label>
      <Message v-for="text in messages" :key="text" severity="success" :closable="false">{{ text }}</Message>
      <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    </div>
    <template #footer>
      <Button :label="messages.length ? 'Done' : 'Cancel'" text @click="visible = false" />
      <Button v-if="!messages.length" label="Send invitation" :loading="saving" :disabled="!canSubmit" @click="submit" />
    </template>
  </Dialog>
</template>

<style scoped>
.form { display: flex; flex-direction: column; gap: 0.9rem; }
.hint { margin: 0; color: var(--p-text-muted-color); font-size: 0.9rem; }
.field { display: flex; flex-direction: column; gap: 0.35rem; }
.field :deep(input) { width: 100%; }
</style>
