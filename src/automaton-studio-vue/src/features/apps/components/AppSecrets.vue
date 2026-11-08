<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { isAxiosError } from 'axios'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import { appsService } from '../services/apps.service'
import type { AppSecretSummary, ApplicationSecretDeclaration } from '../types/apps.types'
import { formatWhen } from '../utils/app-overview'

const props = defineProps<{ appId: string; declared?: ApplicationSecretDeclaration[] }>()
/** Secrets the app declares that have no value yet. */
const missing = computed(() => (props.declared ?? []).filter((item) => !secrets.value.some((secret) => secret.name === item.name)))
const confirm = useConfirm()
const secrets = ref<AppSecretSummary[]>([])
const name = ref('')
const value = ref('')
const saving = ref(false)
const loaded = ref(false)
const adding = ref(false)
const error = ref<string | null>(null)
const namePattern = /^[A-Z_][A-Z0-9_]{0,99}$/

/** Tells password managers (1Password, LastPass, Bitwarden) this is not a login form. */
const noAutofill = { 'data-1p-ignore': 'true', 'data-lpignore': 'true', 'data-bwignore': 'true', 'data-form-type': 'other' }

onMounted(load)
watch(() => props.appId, load)

async function load() {
  error.value = null
  try {
    secrets.value = await appsService.listSecrets(props.appId)
    loaded.value = true
  } catch (failure) {
    error.value = message(failure, 'Secrets could not be loaded.')
  }
}

function edit(secretName: string) {
  name.value = secretName
  value.value = ''
  document.getElementById('secret-value')?.focus()
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
    header: 'Remove secret?',
    message: `Remove ${secretName}? Actions started afterwards no longer receive it.`,
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    acceptLabel: 'Remove',
    accept: async () => {
      try {
        await appsService.deleteSecret(props.appId, secretName)
        await load()
      } catch (failure) {
        error.value = message(failure, 'The secret could not be removed.')
      }
    },
  })
}

function message(failure: unknown, fallback: string) {
  return isAxiosError(failure) ? failure.response?.data?.message ?? fallback : fallback
}
</script>

<template>
  <section v-if="error || adding || (loaded && (secrets.length > 0 || (declared?.length ?? 0) > 0))" class="card" aria-labelledby="secrets-title">
    <div class="card-head">
      <h2 id="secrets-title">Secrets</h2>
      <span class="hint">Encrypted and never shown again. Used from the next start.</span>
    </div>
    <p v-if="secrets.length === 0 && missing.length === 0" class="hint">No secrets yet.</p>
    <div v-for="secret in secrets" :key="secret.name" class="row">
      <code>{{ secret.name }}</code>
      <span class="hint">
        set {{ formatWhen(secret.updatedAtUtc) }} ·
        <button type="button" class="link" :aria-label="`Replace ${secret.name}`" @click="edit(secret.name)">replace</button> ·
        <button type="button" class="link" :aria-label="`Remove ${secret.name}`" @click="remove(secret.name)">remove</button>
      </span>
    </div>
    <div v-if="missing.length > 0" data-testid="missing-secrets">
      <div v-for="item in missing" :key="item.name" class="row">
        <span><code>{{ item.name }}</code> <span class="hint">needed, not set<template v-if="item.description"> · {{ item.description }}</template></span></span>
        <button type="button" class="link" @click="edit(item.name)">set</button>
      </div>
    </div>
    <div class="add">
      <label for="secret-name">Name
        <InputText id="secret-name" v-model="name" name="secret-name" placeholder="S3_SECRET_KEY" autocomplete="off" v-bind="noAutofill" @keyup.enter="save" />
      </label>
      <label for="secret-value">Value
        <InputText id="secret-value" v-model="value" name="secret-value" type="password" autocomplete="new-password" v-bind="noAutofill" @keyup.enter="save" />
      </label>
      <Button label="Save secret" icon="pi pi-lock" :loading="saving" @click="save" />
    </div>
    <small class="hint">Saving an existing name replaces its value.</small>
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
  </section>
  <div v-else-if="loaded" class="empty" data-testid="no-secrets">
    No secrets · <button type="button" class="link" @click="adding = true">Add a secret</button>
  </div>
</template>

<style scoped>
.card { display: flex; flex-direction: column; gap: 10px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.card-head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 12px; }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.hint { color: var(--ink-soft); font-size: 13px; }
.empty { padding: 4px 4px; color: var(--ink-soft); font-size: 13px; }
.row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 0; border-bottom: 1px solid var(--row-line); }
code { font-family: var(--mono); font-size: 13px; }
.link { padding: 0; border: 0; background: none; color: var(--signal-ink); font: inherit; text-decoration: underline; cursor: pointer; }
.add { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 12px; margin-top: 6px; }
.add label { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
</style>
