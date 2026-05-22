<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Textarea from 'primevue/textarea'
import Divider from 'primevue/divider'
import { adminSettingsService } from '../services/adminSettingsService'
import type { AppDeployRulesSettings } from '../types/admin.types'

const toast = useToast()

const loading = ref(false)
const saving = ref(false)

const form = ref({
  sourceExcludeText: '',
  frontendExcludeText: '',
  backendExcludeText: '',
})

const builtInRules = [
  '.env',
  '.env.*',
  '.git',
  'node_modules',
  'venv',
  '.venv',
  '__pycache__',
  '*.pyc',
  '.DS_Store',
]

const parsedPreview = computed(() => ({
  source: parseRules(form.value.sourceExcludeText),
  frontend: parseRules(form.value.frontendExcludeText),
  backend: parseRules(form.value.backendExcludeText),
}))

onMounted(async () => {
  await loadSettings()
})

async function loadSettings() {
  loading.value = true
  try {
    applySettings(await adminSettingsService.getAppDeployRules())
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load app deploy rules',
      life: 3000,
    })
  } finally {
    loading.value = false
  }
}

async function saveSettings() {
  saving.value = true
  try {
    const saved = await adminSettingsService.saveAppDeployRules({
      sourceExclude: parsedPreview.value.source,
      frontendExclude: parsedPreview.value.frontend,
      backendExclude: parsedPreview.value.backend,
    })
    applySettings(saved)
    toast.add({
      severity: 'success',
      summary: 'Saved',
      detail: 'Deploy exclusion rules updated. Changes are effective immediately.',
      life: 3000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to save app deploy rules',
      life: 3000,
    })
  } finally {
    saving.value = false
  }
}

function applySettings(settings: AppDeployRulesSettings) {
  form.value.sourceExcludeText = settings.sourceExclude.join('\n')
  form.value.frontendExcludeText = settings.frontendExclude.join('\n')
  form.value.backendExcludeText = settings.backendExclude.join('\n')
}

function parseRules(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
}
</script>

<template>
  <div class="app-deploy-rules-view">
    <div class="flex align-items-center justify-content-between mb-4 gap-3 flex-wrap">
      <div>
        <h2 class="text-2xl font-bold m-0">App Deploy Rules</h2>
        <p class="text-color-secondary mt-1 mb-0">
          Default deploy exclusions stored in the database. Changes apply immediately to CLI and
          Studio uploads without a server restart.
        </p>
      </div>
      <div class="flex gap-2">
        <Button
          label="Reload"
          icon="pi pi-refresh"
          severity="secondary"
          outlined
          :disabled="saving"
          :loading="loading"
          @click="loadSettings"
        />
        <Button
          label="Save Changes"
          icon="pi pi-save"
          :loading="saving"
          :disabled="loading"
          @click="saveSettings"
        />
      </div>
    </div>

    <div class="grid">
      <div class="col-12 lg:col-5">
        <Card>
          <template #title>Platform Baseline</template>
          <template #content>
            <p class="text-color-secondary text-sm mt-0 mb-3">
              These built-in defaults are always merged with the rules below unless the server-side
              safety core blocks the path first.
            </p>
            <div class="flex flex-wrap gap-2">
              <span
                v-for="rule in builtInRules"
                :key="rule"
                class="surface-200 border-round px-2 py-1 text-800 text-sm"
              >
                {{ rule }}
              </span>
            </div>

            <Divider />

            <p class="text-color-secondary text-sm mb-2">
              Hardcoded safety still applies before these defaults:
            </p>
            <ul class="mt-0 mb-0 pl-3 text-sm text-color-secondary">
              <li>No absolute paths</li>
              <li>No path traversal</li>
              <li>No invalid or empty paths</li>
              <li>No unsafe filesystem escapes</li>
            </ul>
          </template>
        </Card>
      </div>

      <div class="col-12 lg:col-7">
        <Card>
          <template #title>Database-Backed Exclusions</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div class="field">
                <label class="font-medium block mb-2">Source Upload Excludes</label>
                <p class="text-color-secondary text-sm mt-0 mb-2">
                  Applied when CLI or Studio uploads editable source files into <code>source/</code>.
                </p>
                <Textarea
                  v-model="form.sourceExcludeText"
                  rows="6"
                  class="w-full font-mono"
                  auto-resize
                  placeholder=".cache&#10;coverage/**&#10;large-models/**"
                />
              </div>

              <div class="field">
                <label class="font-medium block mb-2">Frontend Live Upload Excludes</label>
                <p class="text-color-secondary text-sm mt-0 mb-2">
                  Reserved for future frontend live-package filtering. Kept in the same settings
                  model now so admins can manage all deploy defaults in one place.
                </p>
                <Textarea
                  v-model="form.frontendExcludeText"
                  rows="5"
                  class="w-full font-mono"
                  auto-resize
                  placeholder="*.tmp&#10;debug/**"
                />
              </div>

              <div class="field">
                <label class="font-medium block mb-2">Backend Live Upload Excludes</label>
                <p class="text-color-secondary text-sm mt-0 mb-2">
                  Applied when backend packages are uploaded into <code>live/backend</code> and
                  when Studio enumerates runner deploy files.
                </p>
                <Textarea
                  v-model="form.backendExcludeText"
                  rows="6"
                  class="w-full font-mono"
                  auto-resize
                  placeholder="cache/**&#10;large-models/**&#10;*.tmp"
                />
              </div>
            </div>
          </template>
        </Card>
      </div>

      <div class="col-12">
        <Card>
          <template #title>Parsed Preview</template>
          <template #content>
            <p class="text-color-secondary text-sm mt-0 mb-3">
              One rule per line. Empty lines are ignored. These are the exact values that will be
              persisted in the <code>Settings</code> table.
            </p>

            <div class="grid">
              <div class="col-12 md:col-4">
                <div class="font-medium mb-2">Source</div>
                <pre class="rules-preview">{{ parsedPreview.source }}</pre>
              </div>
              <div class="col-12 md:col-4">
                <div class="font-medium mb-2">Frontend</div>
                <pre class="rules-preview">{{ parsedPreview.frontend }}</pre>
              </div>
              <div class="col-12 md:col-4">
                <div class="font-medium mb-2">Backend</div>
                <pre class="rules-preview">{{ parsedPreview.backend }}</pre>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.rules-preview {
  margin: 0;
  padding: 0.75rem;
  border-radius: 0.5rem;
  background: var(--surface-100);
  overflow-x: auto;
  font-size: 0.9rem;
}
</style>
