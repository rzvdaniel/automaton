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
  remixSourceExcludeText: '',
  remixFrontendExcludeText: '',
})

const parsedPreview = computed(() => ({
  source: parseRules(form.value.sourceExcludeText),
  frontend: parseRules(form.value.frontendExcludeText),
  backend: parseRules(form.value.backendExcludeText),
  remixSource: parseRules(form.value.remixSourceExcludeText),
  remixFrontend: parseRules(form.value.remixFrontendExcludeText),
}))

const exclusionCards = computed(() => [
  {
    key: 'source',
    title: 'Source Upload Excludes',
    tone: 'Packaging',
    description:
      'Applied when CLI or Studio uploads editable source files into source/.',
    model: 'sourceExcludeText',
    rows: 6,
    placeholder: '.cache\ncoverage/**\nlarge-models/**',
  },
  {
    key: 'frontend',
    title: 'Frontend Live Upload Excludes',
    tone: 'Runtime',
    description:
      'Reserved for future frontend live-package filtering. Kept here so deploy defaults stay in one settings model.',
    model: 'frontendExcludeText',
    rows: 5,
    placeholder: '*.tmp\ndebug/**',
  },
  {
    key: 'backend',
    title: 'Backend Live Upload Excludes',
    tone: 'Runtime',
    description:
      'Applied when backend packages are uploaded into live/backend and when Studio enumerates runner deploy files.',
    model: 'backendExcludeText',
    rows: 6,
    placeholder: 'cache/**\nlarge-models/**\n*.tmp',
  },
  {
    key: 'remix-source',
    title: 'Remix Source Excludes',
    tone: 'Remix',
    description:
      'Applied when users remix or duplicate portable source files from source/. Use this for org-specific secret or state patterns.',
    model: 'remixSourceExcludeText',
    rows: 6,
    placeholder: 'custom-secrets/**\ntenant-data/**\n*.private.json',
  },
  {
    key: 'remix-frontend',
    title: 'Remix Frontend Runtime Excludes',
    tone: 'Remix',
    description:
      'Applied when a public frontend runtime baseline is copied from live/frontend into a remix. This does not affect live/backend.',
    model: 'remixFrontendExcludeText',
    rows: 5,
    placeholder: 'debug-assets/**\n*.diagnostics.json',
  },
])

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
      remixSourceExclude: parsedPreview.value.remixSource,
      remixFrontendExclude: parsedPreview.value.remixFrontend,
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
  form.value.remixSourceExcludeText = settings.remixSourceExclude.join('\n')
  form.value.remixFrontendExcludeText = settings.remixFrontendExclude.join('\n')
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
        <h2 class="text-2xl font-bold m-0">App File Exclusions</h2>
        <p class="text-color-secondary mt-1 mb-0">
          Configure additive database exclusions for app packaging and remix copy rules. Changes
          apply immediately without a server restart.
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
      <div class="col-12">
        <Card>
          <template #content>
            <div class="baseline-strip">
              <div class="baseline-main">
                <div class="baseline-title-row">
                  <div class="baseline-title">Platform Baseline</div>
                  <span class="rule-chip">Immutable</span>
                </div>
                <p class="text-color-secondary text-sm mt-2 mb-0">
                  Baseline rules come from server configuration in <code>appsettings</code>. The
                  controls below add database-only overrides. Effective behavior is:
                  <strong>config baseline</strong>, then <strong>admin rules</strong>, then
                  structural path-safety checks in code.
                </p>
              </div>

              <Divider layout="vertical" class="baseline-divider hidden lg:flex" />

              <div class="baseline-safety">
                <div class="text-sm font-medium mb-2">Always enforced in code</div>
                <div class="baseline-pills">
                  <span class="baseline-pill">No absolute paths</span>
                  <span class="baseline-pill">No path traversal</span>
                  <span class="baseline-pill">No invalid or empty paths</span>
                  <span class="baseline-pill">No filesystem escapes</span>
                </div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <div class="col-12">
        <Card>
          <template #title>Admin Exclusions</template>
          <template #content>
            <p class="text-color-secondary text-sm mt-0 mb-4">
              Each card adds database-only rules on top of the immutable platform baseline from
              <code>appsettings</code>.
            </p>

            <div class="grid">
              <div
                v-for="card in exclusionCards"
                :key="card.key"
                class="col-12 xl:col-6"
              >
                <div class="rule-card h-full">
                  <div class="flex align-items-start justify-content-between gap-3 mb-2">
                    <div>
                      <div class="font-semibold">{{ card.title }}</div>
                      <p class="text-color-secondary text-sm mt-2 mb-0">
                        {{ card.description }}
                      </p>
                    </div>
                    <span class="rule-chip">{{ card.tone }}</span>
                  </div>

                  <Textarea
                    v-model="form[card.model as keyof typeof form]"
                    :rows="card.rows"
                    class="w-full font-mono rule-textarea"
                    :placeholder="card.placeholder"
                  />
                </div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.baseline-strip {
  display: grid;
  grid-template-columns: minmax(0, 1.6fr) auto minmax(18rem, 1fr);
  gap: 1.25rem;
  align-items: start;
}

.baseline-main,
.baseline-safety {
  min-width: 0;
}

.baseline-title-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}

.baseline-title {
  font-size: 1.15rem;
  font-weight: 700;
}

.baseline-divider {
  align-self: stretch;
}

.baseline-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.baseline-pill {
  padding: 0.4rem 0.7rem;
  border-radius: 999px;
  background: var(--surface-100);
  border: 1px solid var(--surface-border);
  color: var(--text-color-secondary);
  font-size: 0.85rem;
}

.rule-card {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid var(--surface-border);
  border-radius: 0.75rem;
  background: linear-gradient(180deg, var(--surface-0) 0%, var(--surface-50) 100%);
}

.rule-textarea {
  min-height: 11rem;
}

.rule-textarea :deep(textarea) {
  min-height: 11rem;
  max-height: 11rem;
  overflow-y: auto;
  resize: none;
}

.rule-chip {
  flex-shrink: 0;
  padding: 0.3rem 0.55rem;
  border-radius: 999px;
  background: var(--primary-50);
  color: var(--primary-700);
  font-size: 0.75rem;
  font-weight: 600;
}

@media (max-width: 991px) {
  .baseline-strip {
    grid-template-columns: 1fr;
  }
}
</style>
