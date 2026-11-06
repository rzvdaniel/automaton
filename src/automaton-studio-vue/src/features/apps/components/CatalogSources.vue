<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { isAxiosError } from 'axios'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Password from 'primevue/password'
import Tag from 'primevue/tag'
import ToggleSwitch from 'primevue/toggleswitch'
import Tooltip from 'primevue/tooltip'
import { appsService } from '../services/apps.service'
import type { CatalogSource } from '../types/apps.types'

const emit = defineEmits<{ changed: [] }>()

const sources = ref<CatalogSource[]>([])
const repo = ref('')
const branch = ref('')
const name = ref('')
const token = ref('')
const error = ref<string | null>(null)
const busy = ref(false)
const success = ref<string | null>(null)

const vTooltip = Tooltip
const docsUrl = 'https://automaton.monster/docs/apps-repository'
const repoHelp = 'Use the address of the GitHub repository (https://github.com/owner/name, or just owner/name). The server reads catalog.json from its root before saving the source and tells you why if it cannot. Create a catalog with "automaton catalog init".'
const branchHelp = 'Only fill this in when the catalog.json you want lives on a branch other than main. It is the name of a Git branch, not the word "catalog".'
const nameHelp = 'A short label for the source. It appears next to each app and in the source filter of the Catalog tab.'
const tokenHelp = 'GitHub, Settings, Developer settings, Fine-grained tokens: Generate new token, choose Only select repositories and pick this one, then Repository permissions, Contents: Read-only. Paste the token here. It is encrypted on this server, used only to read the catalog and download its apps, and never shown again.'
watch([repo, branch, name, token], (values) => { if (values.some(Boolean)) success.value = null })
const blocked = computed(() => (repo.value.trim() ? '' : 'Enter the repository address to add a source.'))

async function load() {
  try {
    sources.value = await appsService.getCatalogSources()
  } catch {
    error.value = 'The catalog sources could not be loaded.'
  }
}

onMounted(load)

function describe(failure: unknown, fallback: string) {
  return isAxiosError(failure) ? failure.response?.data?.message ?? fallback : fallback
}

async function add() {
  error.value = null
  success.value = null
  busy.value = true
  try {
    const added = await appsService.addCatalogSource({
      repo: repo.value.trim(),
      branch: branch.value.trim() || undefined,
      name: name.value.trim() || undefined,
      token: token.value.trim() || undefined,
    })
    repo.value = branch.value = name.value = token.value = ''
    success.value = added.appCount === undefined || added.appCount === null
      ? `Added ${added.name}.`
      : `Added ${added.name}: ${added.appCount} ${added.appCount === 1 ? 'app' : 'apps'} found in its catalog.`
    await load()
    emit('changed')
  } catch (failure) {
    error.value = describe(failure, 'The source could not be added.')
  } finally {
    busy.value = false
  }
}

async function toggle(source: CatalogSource, enabled: boolean) {
  error.value = null
  success.value = null
  try {
    await appsService.setCatalogSourceEnabled(source.id, enabled)
    await load()
    emit('changed')
  } catch (failure) {
    error.value = describe(failure, 'The source could not be changed.')
  }
}

async function remove(source: CatalogSource) {
  error.value = null
  success.value = null
  try {
    await appsService.removeCatalogSource(source.id)
    await load()
    emit('changed')
  } catch (failure) {
    error.value = describe(failure, 'The source could not be removed.')
  }
}
</script>

<template>
  <section class="catalog-sources flex flex-column gap-3" aria-label="Catalog sources">
    <small class="text-color-secondary">
      Only add sources you trust: an app is code that runs on your Runners. Every install shows its source, pinned commit and permissions, and permissions always need your approval.
    </small>
    <ul class="catalog-source-list m-0 p-0">
      <li v-for="source in sources" :key="source.id" class="catalog-source-row" :data-source="source.id">
        <div class="flex flex-column gap-1">
          <span class="flex align-items-center gap-2">
            <strong>{{ source.name }}</strong>
            <Tag v-if="source.official" value="Official" severity="secondary" />
            <Tag v-if="source.hasToken" value="Private" icon="pi pi-lock" severity="secondary" />
          </span>
          <small class="text-color-secondary">{{ source.repo }} · branch {{ source.branch }}</small>
        </div>
        <div class="flex align-items-center gap-3 flex-shrink-0">
          <ToggleSwitch :model-value="source.enabled" :aria-label="`Use ${source.name}`" @update:model-value="toggle(source, $event)" />
          <Button v-if="!source.official" icon="pi pi-trash" severity="danger" text :aria-label="`Remove ${source.name}`" @click="remove(source)" />
        </div>
      </li>
    </ul>
    <form class="flex flex-column gap-3" @submit.prevent="add">
      <div class="source-field">
        <div class="flex align-items-center gap-1"><label for="source-repo" class="field-label">Repository address</label><button type="button" class="info-button" v-tooltip.top.focus="repoHelp" aria-label="More about the repository address"><i class="pi pi-info-circle" aria-hidden="true" /></button></div>
        <InputText id="source-repo" v-model="repo" placeholder="https://github.com/acme/apps" aria-describedby="source-repo-help" />
        <small id="source-repo-help" class="text-color-secondary">The GitHub repository that holds the catalog, for example https://github.com/acme/apps. It needs a catalog.json at its root (see the <a :href="docsUrl" target="_blank" rel="noopener">apps-repository format</a>).</small>
      </div>
      <div class="flex gap-3 flex-wrap">
        <div class="source-field flex-1">
          <div class="flex align-items-center gap-1"><label for="source-branch" class="field-label">Branch (optional)</label><button type="button" class="info-button" v-tooltip.top.focus="branchHelp" aria-label="More about the branch"><i class="pi pi-info-circle" aria-hidden="true" /></button></div>
          <InputText id="source-branch" v-model="branch" placeholder="main" aria-describedby="source-branch-help" />
          <small id="source-branch-help" class="text-color-secondary">The branch that holds catalog.json. Leave empty to use the main branch.</small>
        </div>
        <div class="source-field flex-1">
          <div class="flex align-items-center gap-1"><label for="source-name" class="field-label">Name (optional)</label><button type="button" class="info-button" v-tooltip.top.focus="nameHelp" aria-label="More about the name"><i class="pi pi-info-circle" aria-hidden="true" /></button></div>
          <InputText id="source-name" v-model="name" placeholder="Acme apps" aria-describedby="source-name-help" />
          <small id="source-name-help" class="text-color-secondary">How the source is shown in the Catalog tab. Leave empty to use the repository name.</small>
        </div>
      </div>
      <div class="source-field">
        <div class="flex align-items-center gap-1"><label for="source-token" class="field-label">Read token (private repositories only)</label><button type="button" class="info-button" v-tooltip.top.focus="tokenHelp" aria-label="More about the read token"><i class="pi pi-info-circle" aria-hidden="true" /></button></div>
        <Password v-model="token" :feedback="false" toggle-mask autocomplete="new-password" input-id="source-token" input-class="catalog-token" aria-describedby="source-token-help" />
        <small id="source-token-help" class="text-color-secondary">
          Leave empty for a public repository. For a private one create a fine-grained token limited to that repository with read-only Contents access
          (<a href="https://github.com/settings/personal-access-tokens/new" target="_blank" rel="noopener">create it on GitHub</a>: pick the repository, then Repository permissions, Contents, Read-only).
          It is stored encrypted, used only by this server and never shown again.
        </small>
      </div>
      <div class="flex gap-3 flex-wrap align-items-center">
        <Button type="submit" label="Add source" icon="pi pi-plus" :loading="busy" :disabled="!!blocked" :aria-describedby="blocked ? 'add-hint' : undefined" />
        <small v-if="blocked" id="add-hint" class="text-color-secondary" data-testid="add-hint">{{ blocked }}</small>
        <a class="ml-auto" :href="docsUrl" target="_blank" rel="noopener">How do I make my own catalog?</a>
      </div>
    </form>
    <Message v-if="success" severity="success" role="status" :closable="false">{{ success }}</Message>
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
  </section>
</template>

<style scoped>
.catalog-source-list {
  list-style: none;
  display: grid;
  gap: 0.5rem;
}
.catalog-sources,
.catalog-source-row {
  padding: 1rem;
  border: 1px solid var(--p-content-border-color);
  border-radius: var(--p-content-border-radius);
  background: var(--p-content-background);
}
.catalog-source-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  background: transparent;
}
.source-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.field-label {
  font-weight: 600;
}
.info-button {
  border: 0;
  background: none;
  padding: 0;
  cursor: help;
  line-height: 1;
}
.info-button .pi {
  font-size: 0.85rem;
  color: var(--p-text-muted-color);
  cursor: help;
}
</style>
