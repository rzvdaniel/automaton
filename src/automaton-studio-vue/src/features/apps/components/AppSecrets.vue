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
import type { AppSecretSummary } from '../types/apps.types'

const props = defineProps<{ appId: string }>()
const confirm = useConfirm()
const secrets = ref<AppSecretSummary[]>([])
const name = ref('')
const value = ref('')
const saving = ref(false)
const error = ref<string | null>(null)
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

function message(failure: unknown, fallback: string) {
  return isAxiosError(failure) ? failure.response?.data?.message ?? fallback : fallback
}
</script>

<template>
  <div class="flex flex-column gap-3">
    <p class="m-0 text-color-secondary">
      Secrets are passed to this app's Actions as environment variables, for example API keys or storage credentials.
      They are stored encrypted, never shown again after saving, and removed from Action logs and results. Running
      apps do not receive them yet.
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
  </div>
</template>
