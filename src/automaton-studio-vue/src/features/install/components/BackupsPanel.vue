<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import { installService } from '../services/install.service'
import type { InstallationBackupStatus } from '../types/install.types'

const status = ref<InstallationBackupStatus | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const copied = ref<string | null>(null)

const staleHours = 36
const stale = computed(() => {
  const last = status.value?.lastSuccessAt
  return !last || Date.now() - new Date(last).getTime() > staleHours * 3600_000
})

onMounted(load)

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
  <div class="flex flex-column gap-3">
    <p class="m-0 text-color-secondary">
      The server keeps a nightly backup of the database and the Data Protection keys that decrypt it. Backups run and
      restore on the server; Studio shows their status and the commands to run there with sudo.
    </p>
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
      <template v-if="status.available">
        <DataTable :value="status.backups" size="small" data-key="name">
          <template #empty>No backups yet.</template>
          <Column header="Backup">
            <template #body="{ data }">{{ new Date(data.createdAt).toLocaleString() }}</template>
          </Column>
          <Column header="Size">
            <template #body="{ data }">{{ formatSize(data.sizeBytes) }}</template>
          </Column>
          <Column header="Restore command">
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
        <Message severity="warn" :closable="false">
          Restoring replaces the whole database and its keys with the backup; everything written after it is lost. The
          command asks you to type the server domain and saves the current state as a new backup first.
        </Message>
        <div class="flex align-items-center gap-2 flex-wrap">
          <span>Back up now:</span>
          <code class="text-sm">{{ status.backupNowCommand }}</code>
          <Button
            :icon="copied === status.backupNowCommand ? 'pi pi-check' : 'pi pi-copy'"
            text rounded size="small" aria-label="Copy backup command" @click="copy(status.backupNowCommand)"
          />
        </div>
      </template>
      <Message severity="info" :closable="false">
        These backups live on the same server. Also turn on your hosting provider's server backups (snapshots), and copy
        the backup folder and /etc/automaton to storage elsewhere.
      </Message>
    </template>
  </div>
</template>
