<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { isAxiosError } from 'axios'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Password from 'primevue/password'
import Tag from 'primevue/tag'
import ToggleSwitch from 'primevue/toggleswitch'
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
  busy.value = true
  try {
    await appsService.addCatalogSource({
      repo: repo.value.trim(),
      branch: branch.value.trim() || undefined,
      name: name.value.trim() || undefined,
      token: token.value.trim() || undefined,
    })
    repo.value = branch.value = name.value = token.value = ''
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
  <section class="catalog-sources flex flex-column gap-3" aria-labelledby="catalog-sources-title">
    <h3 id="catalog-sources-title" class="m-0">Catalog sources</h3>
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
    <form class="flex flex-column gap-2" @submit.prevent="add">
      <div class="flex gap-2 flex-wrap">
        <InputText v-model="repo" class="flex-1" placeholder="https://github.com/owner/name" aria-label="Source repository" />
        <InputText v-model="branch" placeholder="Branch (catalog)" aria-label="Source branch" />
        <InputText v-model="name" placeholder="Name (optional)" aria-label="Source name" />
      </div>
      <div class="flex gap-2 flex-wrap align-items-center">
        <Password v-model="token" :feedback="false" toggle-mask autocomplete="new-password" placeholder="Read token for a private repository (optional)" input-class="catalog-token" aria-label="Read token" />
        <Button type="submit" label="Add source" icon="pi pi-plus" :loading="busy" :disabled="!repo.trim()" />
      </div>
      <small class="text-color-secondary">
        The repository needs a <code>catalog.json</code> in its main branch (another branch can be named). A token (fine-grained, read-only Contents access) is stored encrypted, used only by this server, and never shown again.
      </small>
    </form>
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
</style>
