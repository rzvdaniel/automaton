<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import { useToast } from 'primevue/usetoast'
import { appsService } from '../services/apps.service'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { App } from '../types/apps.types'
import { useDeleteApp } from '../utils/use-delete-app'
import AppSettingValues from './AppSettingValues.vue'

const props = defineProps<{ app: App }>()

const toast = useToast()
const editorStore = useAppEditorStore()
const name = ref('')
const summary = ref('')
const saving = ref(false)
const { deleting, askDelete } = useDeleteApp(() => props.app)
/** Read-only access (canUpdate is false) sees the page but cannot change anything. */
const canEdit = computed(() => props.app.canUpdate !== false)

watch(() => [props.app.id, props.app.name, props.app.summary] as const, ([, appName, appSummary]) => {
  name.value = appName
  summary.value = appSummary ?? ''
}, { immediate: true })

const changed = computed(() => name.value.trim() !== props.app.name || summary.value.trim() !== (props.app.summary ?? ''))

async function save() {
  if (!name.value.trim()) {
    toast.add({ severity: 'error', summary: 'The app needs a name', life: 3000 })
    return
  }
  saving.value = true
  try {
    await appsService.updateSettings(props.app.id, { name: name.value.trim(), summary: summary.value.trim() })
    if (editorStore.currentApp) {
      editorStore.currentApp.name = name.value.trim()
      editorStore.currentApp.summary = summary.value.trim()
    }
    toast.add({ severity: 'success', summary: 'Saved', life: 2500 })
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Could not save', detail: error instanceof Error ? error.message : undefined, life: 5000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="settings-page">
    <section class="card" aria-labelledby="about-title">
      <h2 id="about-title">About</h2>
      <div class="about">
        <label for="app-name">Name
          <InputText id="app-name" v-model="name" :disabled="!canEdit" autocomplete="off" maxlength="200" @keyup.enter="save" />
        </label>
        <label for="app-summary">Description
          <InputText id="app-summary" v-model="summary" :disabled="!canEdit" autocomplete="off" maxlength="300" @keyup.enter="save" />
        </label>
      </div>
      <div v-if="canEdit && changed" class="actions"><Button label="Save" icon="pi pi-check" :loading="saving" @click="save" /></div>
    </section>
    <AppSettingValues v-if="canEdit && app.application?.settings?.length" :app-id="app.id" :app="app" :declared="app.application.settings" />
    <section v-if="canEdit" class="card danger" aria-labelledby="danger-title" data-testid="danger-zone">
      <div>
        <h2 id="danger-title">Danger zone</h2>
        <p class="hint">Delete {{ app.name }}, its data and its public address. This can't be undone.</p>
      </div>
      <Button label="Delete app…" severity="danger" outlined :loading="deleting" data-testid="delete-app" @click="askDelete" />
    </section>
  </div>
</template>

<style scoped>
.settings-page { display: flex; flex-direction: column; gap: 18px; }
.card { display: flex; flex-direction: column; gap: 10px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.about { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 12px; }
.about label { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
.actions { display: flex; justify-content: flex-end; }
.danger { flex-direction: row; flex-wrap: wrap; align-items: center; justify-content: space-between; border-color: var(--signal-ink); }
.hint { margin: 4px 0 0; color: var(--ink-soft); font-size: 13px; }
</style>
