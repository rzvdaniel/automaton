<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { isAxiosError } from 'axios'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Drawer from 'primevue/drawer'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Paginator from 'primevue/paginator'
import AppFilterBar, { type FilterPill } from './AppFilterBar.vue'
import AppRow from './AppRow.vue'
import CatalogSources from './CatalogSources.vue'
import { appsService } from '../services/apps.service'
import { catalogName } from '../utils/app-list-display'
import type { CatalogEntry, CatalogQuery, CatalogResponse } from '../types/apps.types'

const PAGE_SIZE = 24
const router = useRouter()
const route = useRoute()
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

const one = (value: unknown) => (typeof value === 'string' ? value : '')
const query = computed<CatalogQuery>(() => ({
  q: one(route.query.q) || undefined,
  category: one(route.query.category) || undefined,
  source: one(route.query.source) || undefined,
  installed: route.query.installed === '1' || undefined,
  page: Math.max(1, Number(one(route.query.page)) || 1),
  pageSize: PAGE_SIZE,
}))
const search = ref(one(route.query.q))
let searchTimer: ReturnType<typeof setTimeout> | undefined
const filtered = computed(() => !!(query.value.q || query.value.category || query.value.source || query.value.installed))
const categories = computed(() => catalog.value?.categories ?? [])
const sourceOptions = computed(() => catalog.value?.sources ?? [])
/** Only administrators can read the sources, so only they see which are private. */
const privateSources = ref<Set<string>>(new Set())
async function loadPrivate() {
  if (!isSystemAdministrator.value) return
  try {
    privateSources.value = new Set((await appsService.getCatalogSources()).filter((s) => s.hasToken).map((s) => s.id))
  } catch { privateSources.value = new Set() }
}
const sourceUrl = (app: CatalogEntry) => `https://github.com/${app.repo}/tree/${app.commit}${app.folder ? `/${app.folder}` : ''}`
/** GitHub 404s for people without access, so a custom source's link shows only when an administrator knows it is public. */
const showSourceLink = (app: CatalogEntry) => !app.customSource || (isSystemAdministrator.value && !privateSources.value.has(app.sourceId))
function reload() { load(); loadPrivate() }

/** The address keeps the view, so a filtered page can be bookmarked or shared. */
function show(patch: Record<string, string | number | boolean | undefined>) {
  const next: Record<string, string> = {}
  for (const [key, value] of Object.entries({ ...route.query, ...patch })) {
    if (value === undefined || value === '' || value === false || (key === 'page' && Number(value) <= 1)) continue
    next[key] = value === true ? '1' : String(value)
  }
  router.replace({ query: next })
}

function onSearch() {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => show({ q: search.value.trim(), page: 1 }), 300)
}

function clearFilters() {
  search.value = ''
  show({ q: undefined, category: undefined, source: undefined, installed: undefined, page: undefined })
}

const INSTALLED = '__installed__'
const details = ref<CatalogEntry | null>(null)
const showDetails = computed({ get: () => !!details.value, set: (open: boolean) => { if (!open) details.value = null } })
const access = (app: CatalogEntry) => (app.permissions.length ? `Asks for ${app.permissions.join(', ')}` : '')
const installTo = (app: CatalogEntry) => ({ name: 'app-install-source', query: { repo: app.repo, folder: app.folder || undefined, commit: app.commit } })
const sourcePills = computed<FilterPill[]>(() => [
  ...(sourceOptions.value.length > 1 ? [{ value: '', label: 'All catalogs' }] : []),
  ...(sourceOptions.value.length > 1 ? sourceOptions.value : []).map((o) => ({ value: o.value, label: `${o.label} · ${o.count}`, icon: privateSources.value.has(o.value) ? 'pi pi-lock' : undefined })),
  { value: INSTALLED, label: 'Installed' },
])
const categoryPills = computed<FilterPill[]>(() => [
  { value: '', label: `All · ${catalog.value?.totalApps ?? catalog.value?.apps.length ?? 0}` },
  ...categories.value.map((c) => ({ value: c.value, label: `${c.label} · ${c.count}` })),
])
const pickSource = (value: string) => (value === INSTALLED ? show({ installed: !query.value.installed, page: 1 }) : show({ source: value || undefined, page: 1 }))
const sourceActive = (value: string) => (value === INSTALLED ? !!query.value.installed : (query.value.source ?? '') === value)
const categoryActive = (value: string) => (value === '' ? !query.value.category : query.value.category?.toLowerCase() === value.toLowerCase())

const showTime = computed(() => (catalog.value?.fetchedAt ? new Date(catalog.value.fetchedAt).toLocaleString() : ''))

let latest = 0
async function load(refresh = false) {
  const mine = ++latest
  loading.value = !catalog.value
  failed.value = false
  try {
    const response = await appsService.getCatalog(refresh, query.value)
    if (mine === latest) catalog.value = response
  } catch {
    if (mine === latest) {
      catalog.value = null
      failed.value = true
    }
  } finally {
    if (mine === latest) loading.value = false
  }
}

onMounted(reload)
watch(query, () => load())
watch(() => route.query.q, (q) => { search.value = one(q) })
onBeforeUnmount(() => clearTimeout(searchTimer))

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
  <div class="catalog">
    <header class="apps-head">
      <div>
        <h1 class="apps-head__title">Catalog</h1>
        <p class="apps-head__line">Apps you can install on this server.</p>
      </div>
      <div v-if="isSystemAdministrator" class="apps-head__buttons">
        <Button label="Add a catalog" outlined class="btn-small" @click="showSources = true" />
        <Button label="Install from URL" outlined class="btn-small" @click="showGit = true" />
      </div>
    </header>

    <p v-if="loading" class="catalog-note">Loading…</p>

    <div v-else-if="failed || catalog?.unavailable" class="apps-card apps-empty" role="status">
      <p class="apps-empty__title">The catalog could not be loaded</p>
      <p class="apps-empty__line">
        {{ catalog?.message ?? 'The server could not answer.' }} This server reads the list from GitHub and has no saved copy yet.
        Check that the server can reach github.com, then try again.
      </p>
      <Button label="Retry" icon="pi pi-refresh" @click="load(true)" />
    </div>

    <template v-else-if="catalog">
      <Message v-if="catalog.stale" severity="warn" :closable="false">{{ catalog.message }}</Message>
      <template v-if="(catalog.totalApps ?? catalog.apps.length) > 0">
        <AppFilterBar
          v-model:search="search"
          label="Search apps"
          group-label="Category"
          placeholder="Search apps"
          :pills="categoryPills"
          :active="categoryActive"
          @update:search="onSearch"
          @select="show({ category: $event || undefined, page: 1 })"
        />
        <AppFilterBar :label="sourcePills.length > 1 ? 'Catalog' : 'Show'" :pills="sourcePills" :active="sourceActive" :show-label="true" @select="pickSource" />
      </template>
      <p v-if="(catalog.totalApps ?? catalog.apps.length) === 0" class="catalog-note">The catalog has no apps yet.</p>
      <div v-else-if="catalog.apps.length === 0" class="apps-card apps-empty" role="status">
        <p class="apps-empty__title">No apps match</p>
        <p class="apps-empty__line">Nothing in the catalog matches these filters.</p>
        <Button v-if="filtered" label="Clear filters" outlined class="btn-small" @click="clearFilters" />
      </div>
      <div v-else class="apps-card">
        <AppRow
          v-for="app in catalog.apps"
          :id="app.id"
          :key="`${app.repo}/${app.folder}`"
          :name="catalogName(app.name)"
          :line="app.summary"
          :image-url="app.icon"
          :label="`Details of ${catalogName(app.name)}`"
          @open="details = app"
        >
          <template #meta>
            <span v-if="access(app)" class="catalog-access">{{ access(app) }}</span>
          </template>
          <template #tag>
            <span class="catalog-category">{{ app.category }}<template v-if="app.customSource"> · {{ app.sourceName }}</template></span>
          </template>
          <template #action>
            <RouterLink v-if="app.installedAppId" :to="{ name: 'app-editor', params: { id: app.installedAppId } }" class="catalog-installed" :aria-label="`Open ${catalogName(app.name)}, installed`">Installed</RouterLink>
            <span v-else-if="app.requiresNewerServer" class="catalog-access">Needs Automaton Server {{ app.minServerVersion }} or newer</span>
            <RouterLink v-else :to="installTo(app)" class="p-button p-component p-button-outlined p-button-sm catalog-install" :aria-label="`Install ${catalogName(app.name)}`">Install</RouterLink>
          </template>
        </AppRow>
      </div>
      <Paginator
        v-if="(catalog.total ?? 0) > PAGE_SIZE"
        :rows="PAGE_SIZE"
        :first="((catalog.page ?? 1) - 1) * PAGE_SIZE"
        :total-records="catalog.total"
        template="PrevPageLink PageLinks NextPageLink"
        @page="show({ page: $event.page + 1 })"
      />
      <div class="catalog-foot">
        <Button label="Refresh" icon="pi pi-refresh" severity="secondary" outlined class="btn-small" @click="load(true)" />
        <small v-if="showTime" class="catalog-note">Read {{ showTime }}</small>
      </div>
    </template>

    <Dialog v-model:visible="showDetails" modal :header="details ? catalogName(details.name) : ''" :style="{ width: '32rem', maxWidth: '100vw' }">
      <div v-if="details" class="catalog-details">
        <p class="catalog-details__summary">{{ details.summary }}</p>
        <dl class="catalog-details__list">
          <dt>Category</dt><dd>{{ details.category }}<template v-if="details.customSource"> · {{ details.sourceName }}</template></dd>
          <dt>Asks for</dt><dd>{{ details.permissions.length ? details.permissions.join(', ') : 'No access' }}</dd>
          <dt>Version</dt><dd>Commit <code>{{ details.commit.slice(0, 7) }}</code>, needs Automaton Server {{ details.minServerVersion }} or newer</dd>
          <dt v-if="showSourceLink(details)">Source</dt>
          <dd v-if="showSourceLink(details)"><a :href="sourceUrl(details)" target="_blank" rel="noopener" :aria-label="`Source of ${catalogName(details.name)}`">{{ details.repo }}</a></dd>
        </dl>
        <div class="catalog-details__actions">
          <RouterLink v-if="details.installedAppId" :to="{ name: 'app-editor', params: { id: details.installedAppId } }" class="p-button p-component p-button-outlined catalog-button">Manage</RouterLink>
          <RouterLink v-if="!details.requiresNewerServer" :to="installTo(details)" class="p-button p-component catalog-button catalog-details__install" :aria-label="`${details.installedAppId ? 'Install another copy of' : 'Install'} ${catalogName(details.name)}`">
            {{ details.installedAppId ? 'Install again' : 'Install' }}
          </RouterLink>
        </div>
      </div>
    </Dialog>

    <Drawer v-if="isSystemAdministrator" v-model:visible="showSources" position="right" header="Add a catalog" :style="{ width: '32rem', maxWidth: '100vw' }">
      <CatalogSources @changed="reload" />
    </Drawer>

    <Dialog v-if="isSystemAdministrator" v-model:visible="showGit" modal header="Install from a Git address" :style="{ width: '32rem', maxWidth: '100vw' }" @hide="gitError = null; gitUrl = ''">
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
.catalog { max-width: 1400px; margin: 0 auto; }
.apps-head { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: 16px; margin-bottom: 20px; }
.apps-head__title { margin: 0; font-family: var(--display); font-size: 28px; font-weight: 700; color: var(--ink); }
.apps-head__line { margin: 4px 0 0; color: var(--ink-soft); font-size: 15px; }
.apps-head__buttons { display: flex; flex-wrap: wrap; gap: 8px; }
.btn-small.p-button { min-height: 36px; background: transparent; border: 1px solid var(--line); color: var(--ink); border-radius: 999px; font-weight: 600; font-size: 13px; }
.apps-card {
  display: flex; flex-direction: column; gap: 8px; padding: 16px; margin-bottom: 16px;
  background: var(--paper); border: 1px solid var(--line); border-radius: 18px;
}
.apps-empty { align-items: center; text-align: center; padding: 40px 16px; }
.apps-empty__title { margin: 0; font-family: var(--display); font-size: 20px; font-weight: 600; }
.apps-empty__line { margin: 0 0 8px; color: var(--ink-soft); }
.catalog-note { margin: 0; color: var(--ink-soft); }
.catalog-foot { display: flex; align-items: center; gap: 12px; }
.catalog-category { flex: none; color: var(--ink-soft); font-size: 12.5px; }
.catalog-access { color: var(--ink-soft); font-size: 12px; opacity: 0.85; }
.catalog-installed { flex: none; color: var(--ink-soft); font-family: var(--mono); font-size: 12px; text-decoration: none; }
.catalog-installed:hover { color: var(--ink); text-decoration: underline; }
.catalog-install.p-button { flex: none; min-height: 36px; background: transparent; border: 1px solid var(--line); color: var(--ink); border-radius: 999px; font-weight: 600; font-size: 13px; text-decoration: none; }
.catalog-install.p-button:hover { border-color: var(--signal); background: transparent; color: var(--ink); }
.catalog-button { text-decoration: none; }
.catalog-details__install.p-button { background: var(--ink); border-color: var(--ink); color: var(--paper); border-radius: 999px; font-weight: 600; }
.catalog-details__install.p-button:hover { background: var(--ink); border-color: var(--signal); color: var(--paper); }
.catalog-details { display: flex; flex-direction: column; gap: 12px; }
.catalog-details__summary { margin: 0; }
.catalog-details__list { display: grid; grid-template-columns: max-content 1fr; gap: 6px 16px; margin: 0; }
.catalog-details__list dt { color: var(--ink-soft); }
.catalog-details__list dd { margin: 0; min-width: 0; overflow-wrap: anywhere; }
.catalog-details__actions { display: flex; align-items: center; gap: 8px; justify-content: flex-end; }
.catalog a:not(.p-button), .catalog-details a:not(.p-button) { color: var(--p-primary-color); }
@media (max-width: 640px) {
  .catalog-category { display: none; }
  .catalog-install.p-button { min-height: 44px; }
}
</style>
