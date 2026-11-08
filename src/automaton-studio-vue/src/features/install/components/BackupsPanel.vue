<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
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

// The Offsite Backup maintenance app, when it is installed on this server.
const offsiteApp = computed(() => apps.value.find((app) => /off-?site\s*backup/i.test(app.name)) ?? null)

const staleHours = 36
const stale = computed(() => {
  const last = status.value?.lastSuccessAt
  return !last || Date.now() - new Date(last).getTime() > staleHours * 3600_000
})

onMounted(() => { load(); loadApps() })

async function loadApps() {
  try {
    apps.value = (await appsService.getApps()).sort((a, b) => a.name.localeCompare(b.name))
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

// The host timer is nightly; Studio is not told its exact time, so the next run is estimated from the last one.
const nextBackup = computed(() => {
  const last = status.value?.lastAttemptAt ?? status.value?.lastSuccessAt
  const next = last ? new Date(last).getTime() + 24 * 3600_000 : 0
  return next > Date.now() ? new Date(next).toLocaleString() : null
})

async function load() {
  loading.value = true
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
  <div class="flex flex-column gap-4">
    <Message v-if="loadError" severity="error" :closable="false">{{ loadError }}</Message>
    <template v-else-if="!loading && status">
      <Message v-if="!status.available" severity="warn" :closable="false">
        Nightly backups are not set up on this server yet. Run the latest server update
        (<code>sudo bash /opt/automaton-server/update.sh</code>) to enable them.
      </Message>
      <Message v-else-if="status.lastError" severity="error" :closable="false">{{ status.lastError }}</Message>
      <Message v-else-if="stale" severity="warn" :closable="false">
        The last successful backup is older than {{ staleHours }} hours. Check the server's automaton-backup timer.
      </Message>

      <div class="grid">
        <div class="col-12 md:col-4">
          <div class="surface-card border-1 surface-border border-round p-3 h-full" data-test="tile-last">
            <div class="text-color-secondary text-sm">Last backup</div>
            <div class="text-xl font-bold mt-1">{{ ago(status.lastSuccessAt) }}</div>
            <div class="text-sm text-color-secondary mt-1">
              {{ status.lastSuccessAt ? new Date(status.lastSuccessAt).toLocaleString() : 'No backup yet' }}
            </div>
          </div>
        </div>
        <div class="col-12 md:col-4">
          <div class="surface-card border-1 surface-border border-round p-3 h-full" data-test="tile-next">
            <div class="text-color-secondary text-sm">Next backup</div>
            <div class="text-xl font-bold mt-1">{{ status.available ? 'Tonight' : 'Not scheduled' }}</div>
            <div class="text-sm text-color-secondary mt-1">
              <template v-if="status.available && nextBackup">About {{ nextBackup }}, then every night</template>
              <template v-else-if="status.available">Every night</template>
              <template v-else>Nightly backups are off</template>
            </div>
          </div>
        </div>
        <div class="col-12 md:col-4">
          <div class="surface-card border-1 surface-border border-round p-3 h-full" data-test="tile-offsite">
            <div class="text-color-secondary text-sm">Offsite copy</div>
            <template v-if="offsiteApp">
              <div class="text-xl font-bold mt-1">{{ offsiteApp.name }}</div>
              <RouterLink :to="`/apps/${offsiteApp.id}`" class="text-sm">Open the app to see its runs</RouterLink>
            </template>
            <template v-else-if="appsLoaded">
              <div class="text-xl font-bold mt-1">Not set up</div>
              <RouterLink to="/apps/catalog" class="text-sm">Install the Offsite Backup app from the Catalog</RouterLink>
            </template>
            <div v-else class="text-sm text-color-secondary mt-1">Checking...</div>
          </div>
        </div>
      </div>

      <template v-if="status.available">
        <div class="flex align-items-center gap-2 flex-wrap">
          <strong>Back up now</strong>
          <span class="text-color-secondary">Run on the server:</span>
          <code class="text-sm">{{ status.backupNowCommand }}</code>
          <Button
            :icon="copied === status.backupNowCommand ? 'pi pi-check' : 'pi pi-copy'"
            text rounded size="small" aria-label="Copy backup command" @click="copy(status.backupNowCommand)"
          />
        </div>

        <div>
          <h2 class="text-lg font-bold mt-0 mb-2">History</h2>
          <DataTable :value="status.backups" size="small" data-key="name" scrollable>
            <template #empty>No backups yet.</template>
            <Column header="Backup">
              <template #body="{ data }">{{ new Date(data.createdAt).toLocaleString() }}</template>
            </Column>
            <Column header="Size">
              <template #body="{ data }">{{ formatSize(data.sizeBytes) }}</template>
            </Column>
            <Column header="Encrypted archive">
              <template #body="{ data }">
                <span v-if="data.archiveSizeBytes !== null">{{ formatSize(data.archiveSizeBytes) }}</span>
                <span v-else class="text-color-secondary">Not available</span>
              </template>
            </Column>
            <Column header="Restore the whole server">
              <template #body="{ data }">
                <div class="flex align-items-center gap-2">
                  <code class="text-sm">{{ data.restoreCommand }}</code>
                  <Button
                    :icon="copied === data.restoreCommand ? 'pi pi-check' : 'pi pi-copy'"
                    text rounded size="small" aria-label="Copy restore command" @click="copy(data.restoreCommand)"
                  />
                </div>
              </template>
            </Column>
          </DataTable>
          <Message class="mt-3" severity="warn" :closable="false">
            Restoring replaces the whole database and its keys with the backup; everything written after it is lost. The
            command asks you to type the server domain and saves the current state as a new backup first.
          </Message>
        </div>
      </template>

      <div v-if="apps.length" data-test="app-restore">
        <h2 class="text-lg font-bold mt-0 mb-2">Restore one app</h2>
        <p class="mt-0 text-color-secondary">Open an app's Advanced tab (Appearance, restore and delete) to restore only that app.</p>
        <div class="flex flex-wrap gap-3">
          <RouterLink v-for="app in apps" :key="app.id" :to="{ path: `/apps/${app.id}`, query: { view: 'more' } }">
            {{ app.name }}
          </RouterLink>
        </div>
      </div>

      <Message v-if="status.backupRecipient" severity="warn" :closable="false">
        Encrypted archives cannot be restored without the backup key. Save the recipient in your backup configuration
        and store the private key in a password manager now. The key remains on this server:
        <code>{{ status.backupKeyCommand }}</code>
        <div class="mt-2"><code>{{ status.backupRecipient }}</code></div>
      </Message>
      <Message severity="info" :closable="false">
        These backups live on the same server. Copy the encrypted archive and private key to separate off-site storage,
        and keep a protected copy of /etc/automaton for the other server credentials. Also turn on your hosting provider's
        server backups (snapshots).
      </Message>
    </template>
  </div>
</template>
