<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { isAxiosError } from 'axios'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Message from 'primevue/message'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import { appsService } from '../services/apps.service'
import type { AppSecretSummary, SecretAuditEntry } from '../types/apps.types'

const props = defineProps<{ appId: string }>()
const confirm = useConfirm()
const secrets = ref<AppSecretSummary[]>([])
const name = ref('')
const value = ref('')
const saving = ref(false)
const error = ref<string | null>(null)
const history = ref<SecretAuditEntry[] | null>(null)
const historyLoading = ref(false)
const namePattern = /^[A-Z_][A-Z0-9_]{0,99}$/

onMounted(load)
watch(() => props.appId, load)

async function load() {
  error.value = null
  try {
    secrets.value = await appsService.listSecrets(props.appId)
  } catch (failure) {
    error.value = message(failure, 'Secrets could not be loaded.')
  }
}

function edit(secretName: string) {
  name.value = secretName
  value.value = ''
}

async function save() {
  error.value = null
  const secretName = name.value.trim()
  if (!namePattern.test(secretName) || secretName.startsWith('AUTOMATON_')) {
    error.value = 'Use capital letters, digits and underscores, not starting with a digit or AUTOMATON_.'
    return
  }
  if (!value.value) {
    error.value = 'Enter a value.'
    return
  }
  saving.value = true
  try {
    await appsService.setSecret(props.appId, secretName, value.value)
    name.value = ''
    value.value = ''
    await load()
  } catch (failure) {
    error.value = message(failure, 'The secret could not be saved.')
  } finally {
    saving.value = false
  }
}

function remove(secretName: string) {
  confirm.require({
    header: 'Delete secret?',
    message: `Delete ${secretName}? Actions started afterwards no longer receive it.`,
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    acceptLabel: 'Delete',
    accept: async () => {
      try {
        await appsService.deleteSecret(props.appId, secretName)
        await load()
      } catch (failure) {
        error.value = message(failure, 'The secret could not be deleted.')
      }
    },
  })
}

async function toggleHistory() {
  if (history.value) {
    history.value = null
    return
  }
  historyLoading.value = true
  try {
    history.value = await appsService.listSecretHistory(props.appId)
  } catch (failure) {
    error.value = message(failure, 'Secret history could not be loaded.')
  } finally {
    historyLoading.value = false
  }
}

function describe(entry: SecretAuditEntry) {
  if (entry.delivered.length === 0) return entry.version ? `${entry.name} v${entry.version}` : entry.name ?? ''
  return entry.delivered
    .map((item) => `${item.name}${item.version ? ` v${item.version}` : ''} (${item.from ? `${item.scope}:${item.from}` : item.scope})`)
    .join(', ')
}

function actor(entry: SecretAuditEntry) {
  if (entry.runnerId) return 'Runner'
  return entry.userName || (entry.userId ? 'User' : 'Platform')
}

function message(failure: unknown, fallback: string) {
  return isAxiosError(failure) ? failure.response?.data?.message ?? fallback : fallback
}
</script>

<template>
  <div class="flex flex-column gap-3">
    <p class="m-0 text-color-secondary">
      Secrets are passed to this app as environment variables, for example API keys or storage credentials: to each
      Action run, and to the running app (API or server-rendered site) whenever it starts. They are stored encrypted,
      never shown again after saving, and removed from Action logs and results. A running app picks up a change on its
      next start: redeploy it with <code>automaton deploy</code>. Static sites cannot use secrets, because their files
      are sent to the browser.
    </p>
    <DataTable :value="secrets" size="small" data-key="name">
      <template #empty>No secrets yet.</template>
      <Column field="name" header="Name" />
      <Column header="Version">
        <template #body="{ data }">{{ data.version }}</template>
      </Column>
      <Column header="Updated">
        <template #body="{ data }">{{ new Date(data.updatedAtUtc).toLocaleString() }}</template>
      </Column>
      <Column header="" style="width: 7rem">
        <template #body="{ data }">
          <Button icon="pi pi-pencil" text rounded size="small" aria-label="Replace value" @click="edit(data.name)" />
          <Button icon="pi pi-trash" text rounded size="small" severity="danger" aria-label="Delete secret" @click="remove(data.name)" />
        </template>
      </Column>
    </DataTable>
    <div class="flex gap-2 align-items-end flex-wrap">
      <div class="flex flex-column gap-1">
        <label for="secret-name">Name</label>
        <InputText id="secret-name" v-model="name" placeholder="S3_SECRET_KEY" autocomplete="off" />
      </div>
      <div class="flex flex-column gap-1">
        <label for="secret-value">Value</label>
        <Password input-id="secret-value" v-model="value" :feedback="false" toggle-mask autocomplete="new-password" />
      </div>
      <Button label="Save secret" icon="pi pi-lock" :loading="saving" @click="save" />
    </div>
    <small class="text-color-secondary">Saving an existing name replaces its value.</small>
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    <div>
      <Button
        :label="history ? 'Hide history' : 'Show history'"
        icon="pi pi-history"
        text
        size="small"
        :loading="historyLoading"
        @click="toggleHistory"
      />
    </div>
    <DataTable v-if="history" :value="history" size="small">
      <template #empty>No history yet.</template>
      <Column header="When">
        <template #body="{ data }">{{ new Date(data.createdAtUtc).toLocaleString() }}</template>
      </Column>
      <Column field="message" header="Event" />
      <Column header="Secrets">
        <template #body="{ data }">{{ describe(data) }}</template>
      </Column>
      <Column header="By">
        <template #body="{ data }">{{ actor(data) }}</template>
      </Column>
    </DataTable>
    <small v-if="history" class="text-color-secondary">
      Changes and deliveries to Action runs and runtime starts, newest first, kept for 180 days. Values are never recorded.
    </small>
  </div>
</template>
