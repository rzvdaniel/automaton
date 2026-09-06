<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Checkbox from 'primevue/checkbox'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import { appsService } from '../services/apps.service'
import type { ForkAppSource } from '../types/apps.types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const source = ref<ForkAppSource | null>(null)
const appName = ref('')
const publishImmediately = ref(false)
const loading = ref(true)
const submitting = ref(false)
const error = ref('')

const sourceAppId = computed(() => String(route.params.sourceAppId || ''))
const localPullReference = computed(() => source.value?.publicUrl || source.value?.id || '')

const lineageLabel = computed(() => {
  if (!source.value) return ''
  return `Remixed from ${source.value.name} by ${source.value.creatorDisplayName}`
})

function defaultRemixName(name: string) {
  return `${name} Remix`
}

async function loadForkSource() {
  loading.value = true
  error.value = ''

  try {
    source.value = await appsService.getForkSource(sourceAppId.value)
    appName.value = defaultRemixName(source.value.name)
  } catch {
    error.value = 'This app is not available to remix.'
  } finally {
    loading.value = false
  }
}

async function submitFork() {
  if (!source.value || submitting.value) return

  const name = appName.value.trim()
  if (!name) {
    error.value = 'Choose a name for your remix.'
    return
  }

  submitting.value = true
  error.value = ''

  try {
    const app = await appsService.fork(source.value.id, {
      name,
      publishImmediately: publishImmediately.value,
    })

    toast.add({
      severity: 'success',
      summary: 'Remix created',
      detail: 'Your editable app copy is ready.',
      life: 3000,
    })

    await router.push(`/apps/${app.id}`)
  } catch {
    error.value = 'Could not remix this app right now.'
  } finally {
    submitting.value = false
  }
}

function cancel() {
  void router.back()
}

function goToRunners() {
  void router.push('/runners')
}

onMounted(loadForkSource)
</script>

<template>
  <div class="fork-page">
    <Card class="fork-card">
      <template #content>
        <div v-if="loading" class="fork-loading">
          <Skeleton height="2rem" width="60%" />
          <Skeleton height="1rem" width="45%" />
          <Skeleton height="12rem" />
        </div>

        <div v-else-if="source" class="fork-layout">
          <div class="fork-intro">
            <p class="eyebrow">Remix app</p>
            <h1>Remix “{{ source.name }}”</h1>
            <p>Create your own editable copy of this app.</p>
          </div>

          <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>

          <div class="field">
            <label for="fork-app-name">App name</label>
            <InputText
              id="fork-app-name"
              v-model="appName"
              class="w-full"
              autocomplete="off"
              @keydown.enter.prevent="submitFork"
            />
          </div>

          <section class="choice-panel" aria-labelledby="fork-visibility">
            <h2 id="fork-visibility">Visibility</h2>
            <label class="choice-row">
              <Checkbox :model-value="!publishImmediately" binary @update:model-value="publishImmediately = !$event" />
              <span>
                <strong>Private draft</strong>
                <small>Recommended. Publish later when your remix is ready.</small>
              </span>
            </label>
            <label class="choice-row">
              <Checkbox v-model="publishImmediately" binary />
              <span>
                <strong>Public immediately</strong>
                <small>Creates a public URL as part of the remix.</small>
              </span>
            </label>
          </section>

          <section class="info-panel" :class="{ warning: source.hasApplicationSource }">
            <h2>Runtime</h2>
            <p>{{ source.runtimeLabel }}</p>
            <template v-if="source.hasApplicationSource">
              <p class="warning-copy">This app uses backend execution.</p>
              <ul>
                <li>To run your remix, you need your own Runner.</li>
                <li>Managed Runners will be available when hosted execution rolls out.</li>
              </ul>
            </template>
          </section>

          <section class="info-panel">
            <h2>What gets copied</h2>
            <p>Remix creates an editable copy of this app’s source. Deployed runtime files and secrets are not copied.</p>
          </section>

          <section class="info-panel">
            <h2>Work locally</h2>
            <p>After creating the remix, you can pull its source to your computer with <code>automaton pull {{ localPullReference }}</code>.</p>
          </section>

          <section class="info-panel">
            <h2>Attribution</h2>
            <p>{{ lineageLabel }}</p>
          </section>

          <div class="actions">
            <Button label="Cancel" severity="secondary" outlined @click="cancel" />
            <Button
              v-if="source.hasApplicationSource"
              label="Set up runner"
              severity="secondary"
              outlined
              @click="goToRunners"
            />
            <Button
              :label="source.hasApplicationSource ? 'Continue remix' : 'Remix app'"
              icon="pi pi-code-branch"
              :loading="submitting"
              @click="submitFork"
            />
          </div>
        </div>

        <Message v-else severity="error" :closable="false">
          {{ error || 'This app is not available to remix.' }}
        </Message>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.fork-page {
  width: min(860px, calc(100% - 32px));
  margin: 0 auto;
  padding: 42px 0;
}

.fork-card {
  border: 1px solid var(--surface-border);
}

.fork-loading,
.fork-layout {
  display: grid;
  gap: 22px;
}

.fork-intro {
  display: grid;
  gap: 7px;
}

.eyebrow {
  margin: 0;
  color: var(--primary-color);
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

h1,
h2,
p {
  margin: 0;
}

h1 {
  color: var(--text-color);
  font-size: clamp(2rem, 4vw, 3rem);
  line-height: 1;
}

.fork-intro p {
  color: var(--text-color-secondary);
}

.field {
  display: grid;
  gap: 8px;
}

.field label,
.choice-panel h2,
.info-panel h2 {
  color: var(--text-color);
  font-size: 0.9rem;
  font-weight: 800;
}

.choice-panel,
.info-panel {
  display: grid;
  gap: 12px;
  padding: 16px;
  border: 1px solid var(--surface-border);
  border-radius: 8px;
  background: var(--surface-ground);
}

.choice-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  cursor: pointer;
}

.choice-row span {
  display: grid;
  gap: 2px;
}

.choice-row small,
.info-panel p,
.info-panel li {
  color: var(--text-color-secondary);
}

.info-panel.warning {
  border-color: color-mix(in srgb, var(--orange-400), transparent 50%);
  background: color-mix(in srgb, var(--orange-50), var(--surface-ground) 82%);
}

.warning-copy {
  font-weight: 800;
}

.info-panel ul {
  margin: 0;
  padding-left: 1.2rem;
}

.actions {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 10px;
}

@media (max-width: 640px) {
  .fork-page {
    width: min(100% - 20px, 860px);
    padding: 20px 0;
  }

  .actions {
    display: grid;
  }
}
</style>
