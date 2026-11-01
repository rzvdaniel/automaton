<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Password from 'primevue/password'
import SelectButton from 'primevue/selectbutton'
import { adminUsersService, type AdminProvisionedUser } from '../services/adminUsersService'

const props = defineProps<{ canGrantAdministrator: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ added: [user: AdminProvisionedUser] }>()

type Mode = 'invite' | 'create'
const modes = [
  { label: 'Invite by email', value: 'invite' as Mode },
  { label: 'Create with password', value: 'create' as Mode },
]

const mode = ref<Mode>('invite')
const email = ref('')
const name = ref('')
const password = ref('')
const generatePassword = ref(true)
const administrator = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)
const result = ref<AdminProvisionedUser | null>(null)
const copied = ref(false)

const canSubmit = computed(() =>
  email.value.trim().length > 3 && (mode.value === 'invite' || generatePassword.value || password.value.length > 0))

function reset() {
  mode.value = 'invite'
  email.value = ''
  name.value = ''
  password.value = ''
  generatePassword.value = true
  administrator.value = false
  error.value = null
  result.value = null
  copied.value = false
}

// Forget everything, the generated password included, when the dialog closes.
watch(visible, (open) => {
  if (!open) reset()
})

async function submit() {
  saving.value = true
  error.value = null
  try {
    const request = {
      email: email.value.trim(),
      name: name.value.trim() || undefined,
      administrator: props.canGrantAdministrator && administrator.value,
    }
    result.value = mode.value === 'invite'
      ? await adminUsersService.inviteUser(request)
      : await adminUsersService.createUser({ ...request, password: generatePassword.value ? undefined : password.value })
    emit('added', result.value)
  } catch (err) {
    const response = (err as { response?: { data?: { message?: string } } }).response
    error.value = response?.data?.message ?? 'The user could not be added.'
  } finally {
    saving.value = false
  }
}

async function copyPassword() {
  if (!result.value?.generatedPassword) return
  await navigator.clipboard.writeText(result.value.generatedPassword)
  copied.value = true
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Add user" :style="{ width: '32rem' }" :breakpoints="{ '640px': '95vw' }">
    <template v-if="!result">
      <div class="form">
        <SelectButton v-model="mode" :options="modes" option-label="label" option-value="value" :allow-empty="false" aria-label="How to add the user" />
        <p class="hint">
          <template v-if="mode === 'invite'">
            The person receives an email with a link to set their password. The link works once and expires after 24 hours.
          </template>
          <template v-else>
            No email is sent. Give the person the password yourself, for example for test accounts or people without email.
          </template>
        </p>

        <label class="field">
          <span>Email</span>
          <InputText v-model="email" type="email" autocomplete="off" placeholder="name@example.com" />
        </label>
        <label class="field">
          <span>Name <small>(optional)</small></span>
          <InputText v-model="name" autocomplete="off" />
        </label>

        <template v-if="mode === 'create'">
          <label class="checkbox">
            <Checkbox v-model="generatePassword" binary input-id="generate-password" />
            <span>Generate a strong password</span>
          </label>
          <label v-if="!generatePassword" class="field">
            <span>Password</span>
            <Password v-model="password" toggle-mask :feedback="false" autocomplete="new-password" input-class="w-full" />
          </label>
        </template>

        <label v-if="canGrantAdministrator" class="checkbox">
          <Checkbox v-model="administrator" binary input-id="make-administrator" />
          <span>Administrator (can manage users and apps)</span>
        </label>

        <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
      </div>
    </template>

    <template v-else>
      <div class="form">
        <Message v-if="result.warning" severity="warn" :closable="false">{{ result.warning }}</Message>
        <Message v-else-if="result.invitationSent" severity="success" :closable="false">
          {{ result.resent ? 'Invitation sent again' : 'Invitation sent' }} to {{ result.email }}.
        </Message>
        <Message v-else severity="success" :closable="false">Created {{ result.email }}.</Message>

        <div v-if="result.generatedPassword" class="password-once">
          <span>Password, shown only now:</span>
          <code>{{ result.generatedPassword }}</code>
          <Button :label="copied ? 'Copied' : 'Copy'" :icon="copied ? 'pi pi-check' : 'pi pi-copy'" size="small" outlined @click="copyPassword" />
        </div>
      </div>
    </template>

    <template #footer>
      <template v-if="!result">
        <Button label="Cancel" text @click="visible = false" />
        <Button
          :label="mode === 'invite' ? 'Send invitation' : 'Create user'"
          :loading="saving"
          :disabled="!canSubmit || saving"
          @click="submit"
        />
      </template>
      <template v-else>
        <Button label="Add another" text @click="reset" />
        <Button label="Done" @click="visible = false" />
      </template>
    </template>
  </Dialog>
</template>

<style scoped>
.form {
  display: flex;
  flex-direction: column;
  gap: 0.9rem;
}

.hint {
  margin: 0;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
  font-size: 0.9rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.field :deep(input) {
  width: 100%;
}

.checkbox {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.password-once {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.password-once code {
  padding: 0.35rem 0.5rem;
  border-radius: 6px;
  background: var(--p-content-hover-background, var(--p-surface-100));
  font-size: 0.95rem;
  overflow-wrap: anywhere;
}
</style>
