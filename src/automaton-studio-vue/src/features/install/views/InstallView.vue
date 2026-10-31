<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import axios from 'axios'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import SetupChecks from '../components/SetupChecks.vue'
import { installService } from '../services/install.service'

const router = useRouter()
const checking = ref(true)
const statusError = ref('')
const complete = ref(false)
const tokenRequired = ref(true)
const setupToken = ref('')
const ready = ref(false)
const saving = ref(false)
const error = ref('')
const name = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
async function loadStatus() {
  checking.value = true
  statusError.value = ''
  try {
    const status = await installService.getStatus()
    complete.value = status.installationComplete
    tokenRequired.value = status.setupTokenRequired
  } catch { statusError.value = 'Studio cannot reach the API. Check the server and retry.' }
  finally { checking.value = false }
}
onMounted(loadStatus)
async function submit() {
  error.value = ''
  if (!ready.value) { error.value = 'Check the installation before continuing.'; return }
  if (password.value !== confirmPassword.value) { error.value = 'Passwords do not match.'; return }
  if (password.value.length < 12 || !/[a-z]/.test(password.value) || !/[A-Z]/.test(password.value)
    || !/[0-9]/.test(password.value) || !/[^a-zA-Z0-9]/.test(password.value)) {
    error.value = 'Use at least 12 characters, including uppercase, lowercase, a number and a symbol.'
    return
  }
  saving.value = true
  try {
    await installService.install({ name: name.value.trim(), email: email.value.trim(), password: password.value }, setupToken.value)
    setupToken.value = ''
    password.value = ''
    confirmPassword.value = ''
    await router.replace('/login?setup=complete')
  } catch (cause) {
    error.value = axios.isAxiosError(cause) && typeof cause.response?.data?.message === 'string'
      ? cause.response.data.message : 'Setup could not be completed. Check your entries and retry.'
  } finally { saving.value = false }
}
</script>
<template>
  <main class="install-view min-h-screen flex align-items-center justify-content-center p-3">
    <Card class="install-card w-full">
      <template #title>Welcome to Automaton</template>
      <template #subtitle>Create the first system administrator for your server.</template>
      <template #content>
        <div v-if="checking" class="text-center p-4" role="status">Checking installation…</div>
        <div v-else-if="statusError" class="flex flex-column gap-3">
          <Message severity="error" :closable="false">{{ statusError }}</Message>
          <Button label="Retry connection" outlined @click="loadStatus" />
        </div>
        <div v-else-if="complete" class="flex flex-column gap-3">
          <Message severity="success" :closable="false">Your administrator account is already configured.</Message>
          <Button label="Sign in" @click="router.push('/login')" />
        </div>
        <form v-else class="flex flex-column gap-4" @submit.prevent="submit">
          <div v-if="tokenRequired" class="flex flex-column gap-2">
            <label for="setup-token">Server setup token</label>
            <Password input-id="setup-token" v-model="setupToken" :feedback="false" toggle-mask fluid :disabled="saving" autocomplete="off" />
            <small class="text-color-secondary">Use the setup token saved by the installer on your server. It grants access only until administrator setup is complete.</small>
          </div>
          <SetupChecks :setup-token="setupToken" @ready="ready = $event" />
          <Message severity="info" :closable="false">Read the setup token on the server with <code>sudo cat /etc/automaton/secrets/setup-token; echo</code>. Studio never displays it. Public DNS (api, studio, store, apps and *.apps) and inbound TCP 80, 443 and 5671 cannot be verified from this server; check them from another network.</Message>
          <Message severity="info" :closable="false">Email delivery is optional during setup. Use an address you control; configure SMTP later for invitations and password recovery.</Message>
          <div class="flex flex-column gap-2">
            <label for="admin-name">Your name</label>
            <InputText id="admin-name" v-model="name" required maxlength="256" autocomplete="name" :disabled="saving" />
          </div>
          <div class="flex flex-column gap-2">
            <label for="admin-email">Administrator email</label>
            <InputText id="admin-email" v-model="email" type="email" required autocomplete="email" :disabled="saving" />
          </div>
          <div class="flex flex-column gap-2">
            <label for="admin-password">Password</label>
            <Password input-id="admin-password" v-model="password" required toggle-mask fluid autocomplete="new-password" :disabled="saving" />
            <small class="text-color-secondary">At least 12 characters with uppercase, lowercase, a number and a symbol.</small>
          </div>
          <div class="flex flex-column gap-2">
            <label for="admin-confirm">Confirm password</label>
            <Password input-id="admin-confirm" v-model="confirmPassword" required :feedback="false" toggle-mask fluid autocomplete="new-password" :disabled="saving" />
          </div>
          <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
          <Button type="submit" label="Create administrator" :loading="saving" :disabled="!ready || saving" />
        </form>
      </template>
    </Card>
  </main>
</template>
<style scoped>
.install-view { background: var(--p-content-background); }
.install-card { max-width: 36rem; }
</style>
