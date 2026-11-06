<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { isAxiosError } from 'axios'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Drawer from 'primevue/drawer'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import CatalogSources from './CatalogSources.vue'
import { appsService } from '../services/apps.service'
import type { CatalogResponse } from '../types/apps.types'

const router = useRouter()
const authStore = useAuthStore()
const isSystemAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
const catalog = ref<CatalogResponse | null>(null)
const loading = ref(true)
const failed = ref(false)
const gitUrl = ref('')
const gitError = ref<string | null>(null)
const resolving = ref(false)
const showSources = ref(false)
const showGit = ref(false)

const showTime = computed(() => (catalog.value?.fetchedAt ? new Date(catalog.value.fetchedAt).toLocaleString() : ''))

async function load(refresh = false) {
  loading.value = true
  failed.value = false
  try {
    catalog.value = await appsService.getCatalog(refresh)
  } catch {
    catalog.value = null
    failed.value = true
  } finally {
    loading.value = false
  }
}

onMounted(() => load())

async function installFromUrl() {
  gitError.value = null
  resolving.value = true
  try {
    const source = await appsService.resolveGitUrl(gitUrl.value.trim())
    await router.push({ name: 'app-install-source', query: { repo: source.repo, folder: source.folder || undefined, commit: source.commit } })
  } catch (failure) {
    gitError.value = isAxiosError(failure) ? failure.response?.data?.message ?? 'That address could not be used.' : 'That address could not be used.'
  } finally {
    resolving.value = false
  }
}
</script>

<template>
  <div class="catalog flex flex-column gap-4">
    <div class="flex align-items-start justify-content-between gap-3 flex-wrap">
    <p class="m-0 text-color-secondary">
      Apps you can install on this server, read from the official
      <a href="https://github.com/rzvdaniel/automaton-apps" target="_blank" rel="noopener">automaton-apps</a> repository
      and any sources an administrator added.
      Installing fetches the app at the listed commit and asks for its settings, secrets and permissions first.
    </p>
    <div class="flex gap-2 flex-shrink-0">
      <Button v-if="isSystemAdministrator" label="Manage sources" icon="pi pi-cog" severity="secondary" outlined @click="showSources = true" />
      <Button label="Install from URL" icon="pi pi-link" severity="secondary" outlined @click="showGit = true" />
    </div>
    </div>

    <p v-if="loading" class="m-0 text-color-secondary">Loading…</p>

    <div v-else-if="failed || catalog?.unavailable" class="catalog-empty flex flex-column gap-3" role="status">
      <strong>The catalog could not be loaded</strong>
      <span class="text-color-secondary">
        {{ catalog?.message ?? 'The server could not answer.' }} This server reads the list from GitHub and has no saved copy yet.
        Check that the server can reach github.com, then try again. You can also use Install from URL.
      </span>
      <div><Button label="Retry" icon="pi pi-refresh" @click="load(true)" /></div>
    </div>

    <template v-else-if="catalog">
      <Message v-if="catalog.stale" severity="warn" :closable="false">{{ catalog.message }}</Message>
      <p v-if="catalog.apps.length === 0" class="m-0 text-color-secondary">The catalog has no apps yet.</p>
      <ul v-else class="catalog-list m-0 p-0">
        <li v-for="app in catalog.apps" :key="`${app.repo}/${app.folder}`" class="catalog-row">
          <div class="flex flex-column gap-1">
            <span class="flex align-items-center gap-2">
              <strong>{{ app.name }}</strong>
              <Tag :value="app.category" severity="secondary" />
              <Tag v-if="app.customSource" :value="`Source: ${app.sourceName}`" severity="warn" icon="pi pi-link" />
              <Tag v-if="app.installedAppId" value="Installed" severity="success" icon="pi pi-check" />
            </span>
            <span class="text-color-secondary">{{ app.summary }}</span>
            <small class="text-color-secondary">
              {{ app.repo }} · commit {{ app.commit.slice(0, 7) }}<template v-if="app.permissions.length"> · asks for {{ app.permissions.join(', ') }}</template>
            </small>
          </div>
          <div class="flex gap-2 flex-shrink-0">
            <RouterLink v-if="app.installedAppId" :to="{ name: 'app-editor', params: { id: app.installedAppId } }" class="p-button p-component p-button-outlined catalog-button">
              Manage
            </RouterLink>
            <small v-if="app.requiresNewerServer" class="text-color-secondary align-self-center">Needs Automaton Server {{ app.minServerVersion }} or newer</small>
            <RouterLink
              v-else
              :to="{ name: 'app-install-source', query: { repo: app.repo, folder: app.folder || undefined, commit: app.commit } }"
              class="p-button p-component catalog-button"
              :class="{ 'p-button-outlined': !!app.installedAppId }"
              :aria-label="`${app.installedAppId ? 'Install another copy of' : 'Install'} ${app.name}`"
            >
              {{ app.installedAppId ? 'Install again' : 'Install' }}
            </RouterLink>
          </div>
        </li>
      </ul>
      <div class="flex align-items-center gap-3">
        <Button label="Refresh" icon="pi pi-refresh" severity="secondary" outlined @click="load(true)" />
        <small v-if="showTime" class="text-color-secondary">Read {{ showTime }}</small>
      </div>
    </template>

    <Drawer v-if="isSystemAdministrator" v-model:visible="showSources" position="right" header="Catalog sources" class="catalog-drawer">
      <CatalogSources @changed="load()" />
    </Drawer>

    <Dialog v-model:visible="showGit" modal header="Install from a Git address" :style="{ width: '32rem' }">
      <div class="flex flex-column gap-2">
        <small class="text-color-secondary">
          A public GitHub repository (or <code>/tree/branch/folder</code>) with an <code>automaton.json</code>. It is pinned to its current commit and shown to you before anything is created.
        </small>
        <form class="flex gap-2" @submit.prevent="installFromUrl">
          <InputText v-model="gitUrl" class="flex-1" placeholder="https://github.com/owner/name" aria-label="Git address" />
          <Button type="submit" label="Continue" icon="pi pi-arrow-right" icon-pos="right" :loading="resolving" :disabled="!gitUrl.trim()" />
        </form>
        <Message v-if="gitError" severity="error" :closable="false">{{ gitError }}</Message>
      </div>
    </Dialog>
  </div>
</template>

<style scoped>
.catalog {
  max-width: 56rem;
}
.catalog-list {
  list-style: none;
  display: grid;
  gap: 0.75rem;
}
.catalog-row,
.catalog-empty {
  padding: 1rem;
  border: 1px solid var(--p-content-border-color);
  border-radius: var(--p-content-border-radius);
  background: var(--p-content-background);
}
.catalog a:not(.p-button) {
  color: var(--p-primary-color);
}
.catalog-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}
.catalog-button {
  text-decoration: none;
}
</style>
