<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import axios from 'axios'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Password from 'primevue/password'
import Tag from 'primevue/tag'
import { smtpSettingsService } from '../services/smtp-settings.service'
import type { SmtpSettingsSummary, SmtpSettingsUpdate } from '../types/install.types'

const summary = ref<SmtpSettingsSummary | null>(null)
const form = ref<SmtpSettingsUpdate>({ emailHost: '', emailPort: 587, emailUseSsl: true,
  emailAccount: '', emailPassword: '', fromEmail: '', supportEmail: '' })
const loading = ref(true)
const saving = ref(false)
const verifying = ref(false)
const sendingTest = ref(false)
const testRecipient = ref('')
const error = ref('')
const notice = ref('')
const unsaved = computed(() => summary.value !== null && (
  form.value.emailHost !== summary.value.emailHost || form.value.emailPort !== (summary.value.emailPort || 587)
  || form.value.emailUseSsl !== summary.value.emailUseSsl || form.value.emailAccount !== summary.value.emailAccount
  || form.value.emailPassword !== '' || form.value.fromEmail !== summary.value.fromEmail
  || form.value.supportEmail !== summary.value.supportEmail))

function message(cause: unknown, fallback: string): string {
  return axios.isAxiosError(cause) && typeof cause.response?.data?.message === 'string'
    ? cause.response.data.message : fallback
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const current = await smtpSettingsService.get()
    summary.value = current
    form.value = { emailHost: current.emailHost, emailPort: current.emailPort || 587,
      emailUseSsl: current.emailUseSsl, emailAccount: current.emailAccount, emailPassword: '',
      fromEmail: current.fromEmail, supportEmail: current.supportEmail }
  } catch (cause) {
    summary.value = null
    error.value = message(cause, 'Could not load SMTP settings.')
  } finally { loading.value = false }
}

async function save() {
  saving.value = true
  error.value = ''
  notice.value = ''
  try {
    await smtpSettingsService.save(form.value)
    await load()
    notice.value = 'SMTP settings saved. Check the connection to verify authentication.'
  } catch (cause) {
    error.value = message(cause, 'Could not save SMTP settings.')
  } finally { saving.value = false }
}

async function verify() {
  verifying.value = true
  error.value = ''
  notice.value = ''
  try {
    await smtpSettingsService.verify()
    await load()
    notice.value = 'SMTP authentication succeeded. No message was sent.'
  } catch (cause) {
    const failure = message(cause, 'SMTP connection or authentication failed.')
    await load()
    error.value = failure
  } finally { verifying.value = false }
}

async function sendTest() {
  sendingTest.value = true
  error.value = ''
  notice.value = ''
  try {
    await smtpSettingsService.sendTest(testRecipient.value.trim())
    notice.value = 'The SMTP server accepted the test message. Check the recipient inbox to confirm delivery.'
  } catch (cause) {
    error.value = message(cause, 'The SMTP server did not accept the test message.')
  } finally { sendingTest.value = false }
}

onMounted(load)
</script>

<template>
  <div class="flex flex-column gap-3">
    <p class="text-color-secondary m-0">Configure outgoing mail for invitations and password recovery. Only system administrators can change these settings.</p>
    <div v-if="loading" role="status">Loading SMTP settings…</div>
    <template v-else-if="summary">
      <div class="flex align-items-center gap-2 flex-wrap">
        <Tag :value="summary.source === 'studio' ? 'Studio-managed' : summary.source === 'file' ? 'Server file' : 'Not configured'"
          :severity="summary.source === 'none' ? 'secondary' : 'info'" />
        <Tag :value="summary.verifiedAt ? 'Authentication verified' : 'Authentication unverified'"
          :severity="summary.verifiedAt ? 'success' : 'secondary'" />
      </div>
      <Message v-if="summary.source === 'file'" severity="info" :closable="false">
        The server file currently supplies SMTP settings. Enter its password and save to move to Studio-managed settings; Studio cannot read the saved password back.
      </Message>
      <form class="flex flex-column gap-3" @submit.prevent="save">
        <div class="flex flex-column gap-1"><label for="smtp-host">SMTP host</label><InputText id="smtp-host" v-model="form.emailHost" required maxlength="255" /></div>
        <div class="flex flex-column gap-1"><label for="smtp-port">Port</label><InputNumber input-id="smtp-port" v-model="form.emailPort" :min="1" :max="65535" required /></div>
        <div class="flex align-items-center gap-2"><Checkbox input-id="smtp-tls" v-model="form.emailUseSsl" binary /><label for="smtp-tls">Use TLS</label></div>
        <small class="text-color-secondary">Port 587 uses STARTTLS and port 465 uses TLS from connection start.</small>
        <div class="flex flex-column gap-1"><label for="smtp-account">SMTP account</label><InputText id="smtp-account" v-model="form.emailAccount" required maxlength="320" autocomplete="username" /></div>
        <div class="flex flex-column gap-1"><label for="smtp-password">SMTP password</label>
          <Password input-id="smtp-password" v-model="form.emailPassword" :feedback="false" toggle-mask fluid autocomplete="new-password" />
          <small class="text-color-secondary">{{ summary.source === 'studio' ? 'Leave blank to keep the saved password.' : 'Required when first saving settings in Studio.' }}</small>
        </div>
        <div class="flex flex-column gap-1"><label for="smtp-from">Sender email</label><InputText id="smtp-from" v-model="form.fromEmail" type="email" required maxlength="320" /></div>
        <div class="flex flex-column gap-1"><label for="smtp-support">Support email</label><InputText id="smtp-support" v-model="form.supportEmail" type="email" required maxlength="320" /></div>
        <div class="flex gap-2 flex-wrap">
          <Button type="submit" label="Save SMTP settings" :loading="saving" :disabled="saving || verifying" />
          <Button type="button" label="Check connection" outlined :loading="verifying" :disabled="summary.source !== 'studio' || unsaved || saving || verifying" @click="verify" />
        </div>
        <small v-if="unsaved" class="text-color-secondary">Save changes before checking the connection. Verification applies to saved settings.</small>
        <div class="flex flex-column gap-1"><label for="smtp-test-recipient">Test recipient email</label>
          <InputText id="smtp-test-recipient" v-model="testRecipient" type="email" autocomplete="email" />
        </div>
        <Button type="button" label="Send test email" outlined :loading="sendingTest"
          :disabled="summary.source !== 'studio' || unsaved || !testRecipient || saving || verifying || sendingTest" @click="sendTest" />
      </form>
    </template>
    <Button v-else type="button" label="Retry" outlined @click="load" />
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    <Message v-if="notice" severity="success" :closable="false">{{ notice }}</Message>
    <small class="text-color-secondary">Connection verification checks TLS and account authentication. A successful test send means the SMTP server accepted the message; confirm arrival in the inbox. Back up the database with the API Data Protection key ring.</small>
  </div>
</template>
