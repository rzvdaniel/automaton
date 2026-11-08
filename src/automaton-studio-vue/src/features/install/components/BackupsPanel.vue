<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { RouterLink } from 'vue-router'
import { installService } from '../services/install.service'
import { appsService } from '@/features/apps/services/apps.service'
import type { AppListItem } from '@/features/apps/types/apps.types'
import type { InstallationBackupStatus } from '../types/install.types'

const status = ref<InstallationBackupStatus | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const copied = ref<string | null>(null)
const apps = ref<AppListItem[]>([])
const appsLoaded = ref(false)

// The Offsite Backup maintenance app, when it is installed on this server: matched by its catalog id, not its name.
const OFFSITE_CATALOG_ID = 'offsite-backup'
const offsiteAppId = ref<string | null>(null)
const offsiteApp = computed(() => apps.value.find((app) => app.id === offsiteAppId.value) ?? null)
const requesting = ref(false)

const staleHours = 36
const stale = computed(() => {
  const last = status.value?.lastSuccessAt
  return !last || Date.now() - new Date(last).getTime() > staleHours * 3600_000
})

onMounted(() => { load(); loadApps() })

async function loadApps() {
  try {
    const [list, catalog] = await Promise.all([
      appsService.getApps(),
      appsService.getCatalog(false, { installed: true, pageSize: 100 }),
    ])
    apps.value = list.sort((a, b) => a.name.localeCompare(b.name))
    offsiteAppId.value = catalog.apps.find((entry) => entry.id === OFFSITE_CATALOG_ID)?.installedAppId ?? null
  } catch {
    apps.value = [] // the offsite tile and the restore list are hints; the history stays usable
  } finally {
    appsLoaded.value = true
  }
}

function ago(iso: string | null) {
  if (!iso) return 'Never'
  const hours = Math.floor((Date.now() - new Date(iso).getTime()) / 3600_000)
  if (hours < 1) return 'Less than an hour ago'
  return hours < 48 ? `${hours} hour${hours === 1 ? '' : 's'} ago` : `${Math.floor(hours / 24)} days ago`
}

// The host reports when its timer fires next; older servers do not, so the next run is estimated from the last one.
const nextBackup = computed(() => {
  const reported = status.value?.nextRunAt
  if (reported) return { at: new Date(reported).toLocaleString(), estimated: false }
  const last = status.value?.lastAttemptAt ?? status.value?.lastSuccessAt
  const next = last ? new Date(last).getTime() + 24 * 3600_000 : 0
  return next > Date.now() ? { at: new Date(next).toLocaleString(), estimated: true } : null
})

async function backUpNow() {
  requesting.value = true
  loadError.value = null
  const before = status.value?.lastAttemptAt ?? null
  try {
    await installService.startBackup()
    // The host runs the backup within moments; refresh until it reports a newer attempt.
    for (let attempt = 0; attempt < 24; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, 5000))
      await load(false)
      if (status.value?.lastAttemptAt !== before) break
    }
  } catch (error) {
    const message = (error as { response?: { data?: { message?: string } } }).response?.data?.message
    loadError.value = message ?? 'The backup could not be started.'
  } finally {
    requesting.value = false
  }
}

async function load(showLoading = true) {
  loading.value = showLoading
  loadError.value = null
  try {
    status.value = await installService.getBackups()
  } catch {
    loadError.value = 'Backup status could not be loaded.'
  } finally {
    loading.value = false
  }
}

function formatSize(bytes: number) {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
}

async function copy(command: string) {
  try {
    await navigator.clipboard.writeText(command)
    copied.value = command
  } catch {
    loadError.value = 'Select the command and copy it manually.'
  }
}
</script>

<template>
  <div class="flex flex-column gap-3 studio-page">
    <Message v-if="loadError" severity="error" :closable="false">{{ loadError }}</Message>
    <template v-else-if="!loading && status">
      <Message v-if="!status.available" severity="warn" :closable="false">
        Nightly backups are not set up on this server yet. Update the server to turn them on
        (<code>sudo bash /opt/automaton-server/update.sh</code>).
      </Message>
      <Message v-else-if="status.lastError" severity="error" :closable="false">{{ status.lastError }}</Message>
      <Message v-else-if="stale" severity="warn" :closable="false">
        The last successful backup is older than {{ staleHours }} hours. Check the server's automaton-backup timer.
      </Message>

      <Message v-if="status.hint" severity="warn" :closable="false">{{ status.hint }}</Message>

      <div class="grid">
        <div class="col-12 md:col-4">
          <div class="studio-card h-full" data-test="tile-last">
            <div class="studio-muted">Last backup</div>
            <div class="text-xl font-bold mt-1">{{ ago(status.lastSuccessAt) }}</div>
            <div class="studio-muted mt-1">
              {{ status.lastSuccessAt ? new Date(status.lastSuccessAt).toLocaleString() : 'No backup yet' }}
            </div>
          </div>
        </div>
        <div class="col-12 md:col-4">
          <div class="studio-card h-full" data-test="tile-next">
            <div class="studio-muted">Next backup</div>
            <div class="text-xl font-bold mt-1">{{ status.available ? (nextBackup && !nextBackup.estimated ? nextBackup.at : 'Tonight') : 'Not scheduled' }}</div>
            <div class="studio-muted mt-1">
              <template v-if="status.available && nextBackup?.estimated">About {{ nextBackup.at }}, then every night</template>
              <template v-else-if="status.available && nextBackup">Then every night</template>
              <template v-else-if="status.available">Every night</template>
              <template v-else>Nightly backups are off</template>
            </div>
          </div>
        </div>
        <div class="col-12 md:col-4">
          <div class="studio-card h-full" data-test="tile-offsite">
            <div class="studio-muted">Offsite copy</div>
            <template v-if="offsiteApp">
              <div class="text-xl font-bold mt-1">{{ offsiteApp.name }}</div>
              <RouterLink :to="`/apps/${offsiteApp.id}`" class="text-sm">Open the app to see its runs</RouterLink>
            </template>
            <template v-else-if="appsLoaded">
              <div class="text-xl font-bold mt-1">Not set up</div>
              <RouterLink to="/apps/catalog" class="text-sm">Install the Offsite Backup app from the Catalog</RouterLink>
            </template>
            <div v-else class="studio-muted mt-1">Checking...</div>
          </div>
        </div>
      </div>

      <template v-if="status.available">
        <div class="flex align-items-center gap-2 flex-wrap">
          <Button
            v-if="status.triggerAvailable" label="Back up now" class="btn-main"
            :loading="requesting" :disabled="requesting || (status.requestPending && !status.hint)" data-test="back-up-now" @click="backUpNow"
          />
          <span v-if="requesting || (status.requestPending && !status.hint)" class="text-color-secondary" data-test="backup-requested">
            Backup requested. It starts within moments.
          </span>
          <template v-if="!status.triggerAvailable">
            <strong>Back up now</strong>
            <span class="text-color-secondary">Run on the server:</span>
            <code class="text-sm">{{ status.backupNowCommand }}</code>
            <Button
              :icon="copied === status.backupNowCommand ? 'pi pi-check' : 'pi pi-copy'"
              text rounded size="small" aria-label="Copy backup command" @click="copy(status.backupNowCommand)"
            />
          </template>
        </div>

        <div>
          <h2 class="studio-section">History <span class="studio-section__count">{{ status.backups.length }}</span></h2>
          <div class="studio-card">
            <p v-if="!status.backups.length" class="studio-muted m-0">No backups yet.</p>
            <div v-for="data in status.backups" :key="data.name" class="studio-row backup-row">
              <span class="studio-row__text">
                <span class="studio-row__name">{{ new Date(data.createdAt).toLocaleString() }}</span>
                <span class="studio-row__line">{{ formatSize(data.sizeBytes) }}<template v-if="data.archiveSizeBytes !== null"> · encrypted copy {{ formatSize(data.archiveSizeBytes) }}</template></span>
              </span>
              <span class="studio-pill" :class="data.archiveSizeBytes !== null ? 'studio-pill--ok' : 'studio-pill--off'">{{ data.archiveSizeBytes !== null ? 'Encrypted' : 'No archive' }}</span>
              <details class="backup-restore">
                <summary class="p-button btn-small">Restore the whole server</summary>
                <div class="flex align-items-center gap-2 mt-2">
                  <code class="text-sm">{{ data.restoreCommand }}</code>
                  <Button
                    :icon="copied === data.restoreCommand ? 'pi pi-check' : 'pi pi-copy'"
                    text rounded size="small" aria-label="Copy restore command" @click="copy(data.restoreCommand)"
                  />
                </div>
              </details>
            </div>
          </div>
          <Message severity="warn" :closable="false">
            Restoring replaces the whole database and its keys; everything written after the backup is lost.
          </Message>
        </div>
      </template>

      <div v-if="apps.length" data-test="app-restore">
        <h2 class="studio-section">Restore one app</h2>
        <p class="studio-muted mt-0">Open an app, then Settings, then Advanced.</p>
        <div class="flex flex-wrap gap-2">
          <RouterLink v-for="app in apps" :key="app.id" :to="{ path: `/apps/${app.id}`, query: { view: 'settings' } }" class="p-button btn-small">
            {{ app.name }}
          </RouterLink>
        </div>
      </div>

      <Message v-if="status.backupRecipient" severity="warn" :closable="false">
        Encrypted archives need the backup key. Store the private key in a password manager. It stays on this server:
        <code>{{ status.backupKeyCommand }}</code>
        <div class="mt-2"><code>{{ status.backupRecipient }}</code></div>
      </Message>
      <Message severity="info" :closable="false">
        These backups live on this server. Copy them, the private key and /etc/automaton to separate storage, and turn on your host's snapshots.
      </Message>
    </template>
  </div>
</template>

<style scoped>
.backup-restore summary { list-style: none; cursor: pointer; display: inline-flex; align-items: center; padding: 4px 14px; }
.backup-restore summary::-webkit-details-marker { display: none; }
.backup-restore[open] { flex-basis: 100%; }
.backup-row { flex-wrap: wrap; }
</style>
