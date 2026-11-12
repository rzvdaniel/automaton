<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { isAxiosError } from 'axios'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import { appsService } from '../services/apps.service'
import type { App, AppSecretSummary, AppSettingValue, ApplicationSettingDeclaration } from '../types/apps.types'
import { formatWhen } from '../utils/app-overview'
import AppRestartButton from './AppRestartButton.vue'

const props = defineProps<{ appId: string; app: App; declared: ApplicationSettingDeclaration[] }>()
/** Values saved as settings can be shown again; `saved` lists older overrides stored as secrets, which cannot. */
const saved = ref<AppSecretSummary[]>([])
const values = ref<AppSettingValue[]>([])
const editing = ref<string | null>(null)
const value = ref('')
const saving = ref(false)
const error = ref<string | null>(null)
const changed = ref(false)

const rows = computed(() => props.declared.map((item) => ({ ...item, current: values.value.find((setting) => setting.name === item.name), earlier: saved.value.find((secret) => secret.name === item.name) })))

onMounted(load)
watch(() => props.appId, load)

async function load() {
  try {
    const [secrets, current] = await Promise.all([appsService.listSecrets(props.appId), appsService.listSettingValues(props.appId)])
    saved.value = secrets
    values.value = current
  } catch {
    error.value = 'Settings could not be loaded.'
  }
}

function edit(name: string) {
  editing.value = name
  value.value = values.value.find((setting) => setting.name === name)?.value ?? rows.value.find((row) => row.name === name)?.default ?? ''
  error.value = null
}

function failureMessage(failure: unknown, fallback: string) {
  return isAxiosError(failure) ? failure.response?.data?.message ?? fallback : fallback
}

async function save() {
  if (!editing.value) return
  if (!value.value.trim()) {
    error.value = 'Enter a value, or use the default.'
    return
  }
  saving.value = true
  try {
    await appsService.setSettingValue(props.appId, editing.value, value.value.trim())
    editing.value = null
    changed.value = true
    await load()
  } catch (failure) {
    error.value = failureMessage(failure, 'The setting could not be saved.')
  } finally {
    saving.value = false
  }
}

async function useDefault(name: string) {
  try {
    await appsService.deleteSettingValue(props.appId, name)
    changed.value = true
    await load()
  } catch (failure) {
    error.value = failureMessage(failure, 'The setting could not be reset.')
  }
}
</script>

<template>
  <section class="card" aria-labelledby="settings-title" data-testid="settings-card">
    <div class="card-head">
      <h2 id="settings-title">Settings</h2>
      <AppRestartButton v-if="!app.isPaused && !app.actionsOnly" :app-id="appId" />
    </div>
    <p v-if="app.actionsOnly" class="hint" data-testid="settings-note">Plain values the app reads as environment variables. Changes apply on the next run.</p>
    <p v-else class="hint" data-testid="settings-note">Plain values the app reads as environment variables. A change applies when the app restarts.</p>
    <p v-if="changed && !app.isPaused && !app.actionsOnly" class="hint" data-testid="settings-restart-hint">Changed. Restart the app to apply it.</p>
    <div v-for="item in rows" :key="item.name" class="row" data-testid="setting-row">
      <span>
        <code>{{ item.name }}</code>
        <span v-if="item.required" class="hint"> (required)</span><br>
        <span class="hint">
          <template v-if="item.current"><code data-testid="setting-current">{{ item.current.value }}</code> (custom, set {{ formatWhen(item.current.updatedAtUtc) }})</template>
          <template v-else-if="item.earlier">set earlier, save again to show the value</template>
          <template v-else-if="item.default">default: <code>{{ item.default }}</code></template>
          <template v-else>not set</template>
          <template v-if="item.description"> · {{ item.description }}</template>
        </span>
      </span>
      <span class="hint">
        <button type="button" class="link" :aria-label="`Set ${item.name}`" @click="edit(item.name)">{{ item.current || item.earlier ? 'replace' : 'set' }}</button>
        <template v-if="item.current || item.earlier"> · <button type="button" class="link" :aria-label="`Use the default for ${item.name}`" @click="useDefault(item.name)">use default</button></template>
      </span>
    </div>
    <form v-if="editing" class="add" @submit.prevent="save">
      <label for="setting-value">{{ editing }}
        <InputText id="setting-value" v-model="value" autocomplete="off" />
      </label>
      <Button type="submit" label="Save setting" :loading="saving" />
      <Button type="button" label="Cancel" severity="secondary" text @click="editing = null" />
    </form>
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
  </section>
</template>

<style scoped>
.card { display: flex; flex-direction: column; gap: 10px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.card-head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.hint { color: var(--ink-soft); font-size: 13px; }
.row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 0; border-bottom: 1px solid var(--row-line); }
code { font-family: var(--mono); font-size: 13px; }
.link { padding: 0; border: 0; background: none; color: var(--signal-ink); font: inherit; text-decoration: underline; cursor: pointer; }
.add { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 12px; margin-top: 6px; }
.add label { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
</style>
