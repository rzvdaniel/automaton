<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import { useToast } from 'primevue/usetoast'
import { appsService } from '../services/apps.service'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { App, AppDeploymentTarget } from '../types/apps.types'
import AppAdvanced from './AppAdvanced.vue'
import AppPermissions from './AppPermissions.vue'
import AppSecrets from './AppSecrets.vue'

const props = defineProps<{ app: App; targets: ReadonlyArray<AppDeploymentTarget>; openAdvanced?: boolean }>()
const emit = defineEmits<{ targetsChanged: [] }>()

const toast = useToast()
const editorStore = useAppEditorStore()
const name = ref('')
const summary = ref('')
const saving = ref(false)
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
    <AppSecrets v-if="canEdit" :app-id="app.id" :declared="app.application?.secrets" />
    <AppPermissions :app-id="app.id" />
    <AppAdvanced v-if="canEdit" :app="app" :targets="targets" :open="openAdvanced" @changed="emit('targetsChanged')" />
  </div>
</template>

<style scoped>
.settings-page { display: flex; flex-direction: column; gap: 18px; }
.card { display: flex; flex-direction: column; gap: 10px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.about { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 12px; }
.about label { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
.actions { display: flex; justify-content: flex-end; }
</style>
