<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import { isAxiosError } from 'axios'
import { serverCleanupService } from '../services/server-cleanup.service'
import { formatGiB } from '../utils/format'
import type { ServerCleanup, ServerCleanupSettings } from '../types/server.types'

const cleanup = ref<ServerCleanup | null>(null)
const form = ref<ServerCleanupSettings>({ timeUtc: '05:00', keepDays: 7, journalLimitMb: 200 })
const loadError = ref<string | null>(null)
const actionError = ref<string | null>(null)
const busy = ref(false)

const lastRun = computed(() => cleanup.value?.runs[0] ?? null)

function utc(value: string): string {
  return `${new Date(value).toISOString().slice(0, 16).replace('T', ' ')} UTC`
}

function freed(bytes: number): string {
  return bytes < 1024 ** 3 ? `${Math.round(bytes / 1024 ** 2)} MB` : formatGiB(bytes)
}

function errorText(error: unknown, fallback: string): string {
  if (isAxiosError(error)) {
    const message = (error.response?.data as { error?: string } | undefined)?.error
    if (message) return message
  }
  return fallback
}

async function load() {
  try {
    const next = await serverCleanupService.get()
    cleanup.value = next
    form.value = { ...next.settings }
    loadError.value = null
  } catch {
    loadError.value = 'The cleanup could not be loaded.'
  }
}

async function act(action: () => Promise<unknown>, fallback: string) {
  busy.value = true
  actionError.value = null
  try {
    await action()
    await load()
  } catch (error) {
    actionError.value = errorText(error, fallback)
  } finally {
    busy.value = false
  }
}

const save = () => act(() => serverCleanupService.saveSettings(form.value), 'The settings could not be saved.')
const runNow = () => act(() => serverCleanupService.run(), 'The cleanup could not be started.')

onMounted(load)
</script>

<template>
  <section class="mb-4" data-testid="cleanup-section">
    <div class="flex align-items-center justify-content-between flex-wrap gap-2">
      <h2 class="studio-section">Cleanup</h2>
      <Button label="Run now" outlined class="btn-small" :disabled="busy" data-testid="cleanup-run" @click="runNow" />
    </div>
    <Message v-if="loadError" severity="warn" :closable="false">{{ loadError }}</Message>
    <div v-else-if="cleanup" class="studio-card">
      <p class="mt-0" data-testid="cleanup-last">
        <template v-if="lastRun">
          Last run {{ utc(lastRun.startedAtUtc) }}: {{ lastRun.result }}, freed {{ freed(lastRun.bytesFreed) }}.
          <span v-if="lastRun.message" class="studio-muted" data-testid="cleanup-message">{{ lastRun.message }}</span>
        </template>
        <template v-else>No run yet.</template>
      </p>
      <p data-testid="cleanup-next">Next run {{ utc(cleanup.nextRunAtUtc) }}.</p>
      <form class="flex align-items-end flex-wrap gap-3" @submit.prevent="save">
        <label class="flex flex-column gap-1">Time (UTC)
          <InputText v-model="form.timeUtc" placeholder="05:00" data-testid="cleanup-time" />
        </label>
        <label class="flex flex-column gap-1">Keep unused images and cache (days)
          <InputNumber v-model="form.keepDays" :min="1" :max="365" :use-grouping="false" input-id="cleanup-days" />
        </label>
        <label class="flex flex-column gap-1">Journal limit (MB)
          <InputNumber v-model="form.journalLimitMb" :min="50" :max="100000" :use-grouping="false" input-id="cleanup-journal" />
        </label>
        <Button type="submit" label="Save" :disabled="busy" data-testid="cleanup-save" />
      </form>
    </div>
    <Message v-if="actionError" severity="error" :closable="false" class="mt-2" data-testid="cleanup-error">{{ actionError }}</Message>
  </section>
</template>
