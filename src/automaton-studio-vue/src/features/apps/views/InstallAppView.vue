<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isAxiosError } from 'axios'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Password from 'primevue/password'
import Select from 'primevue/select'
import { appsService } from '../services/apps.service'
import { actionSchedulesService } from '../services/action-schedules.service'
import { adminAppsService } from '@/features/admin/services/adminAppsService'
import { runnersService } from '@/features/runners/services/runners.service'
import { getDeploymentTargetRunnerOptions } from '../utils/deployment-target-runner'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type { InstallManifest, InstallSource } from '../types/apps.types'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const sourceId = String(route.params.sourceAppId)

const source = ref<InstallSource | null>(null)
const loadError = ref<string | null>(null)
const name = ref('')
const settings = reactive<Record<string, string>>({})
const secrets = reactive<Record<string, string>>({})
const schedules = reactive<Record<string, boolean>>({})
const approvePermissions = ref(true)
const runnerOptions = ref<Array<{ label: string; value: string; disabled: boolean }>>([])
const runnerId = ref<string | null>(null)
const steps = ref<Array<{ label: string; state: 'running' | 'done' | 'failed' | 'skipped'; detail?: string }>>([])
const installing = ref(false)
const installedAppId = ref<string | null>(null)
const error = ref<string | null>(null)

const manifest = computed<InstallManifest>(() => source.value?.application ?? {})
const isAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
const permissions = computed(() => manifest.value.permissions ?? [])
const missingRequired = computed(() => [
  ...(manifest.value.settings ?? []).filter((item) => item.required && !item.default && !settings[item.name]?.trim()).map((item) => item.name),
  ...(manifest.value.secrets ?? []).filter((item) => !item.from && !secrets[item.name]).map((item) => item.name),
])

onMounted(async () => {
  try {
    source.value = await appsService.getInstallSource(sourceId)
    try {
      runnerOptions.value = getDeploymentTargetRunnerOptions(await runnersService.list())
      runnerId.value = runnerOptions.value.find((option) => !option.disabled)?.value ?? null
    } catch {
      runnerOptions.value = []
    }
    name.value = source.value.name
    for (const item of manifest.value.settings ?? []) settings[item.name] = item.default ?? ''
    for (const item of manifest.value.schedules ?? []) schedules[item.name] = true
  } catch (failure) {
    loadError.value = message(failure, 'This app cannot be installed: it is not published, not forkable, or has no source.')
  }
})

function message(failure: unknown, fallback: string) {
  return isAxiosError(failure) ? failure.response?.data?.detail ?? failure.response?.data?.message ?? failure.response?.data?.error ?? fallback : fallback
}

async function step<T>(label: string, work: () => Promise<T>): Promise<T> {
  const entry = reactive({ label, state: 'running' as 'running' | 'done' | 'failed' | 'skipped', detail: undefined as string | undefined })
  steps.value.push(entry)
  try {
    const result = await work()
    entry.state = 'done'
    return result
  } catch (failure) {
    entry.state = 'failed'
    entry.detail = failure instanceof Error && !isAxiosError(failure) ? failure.message : message(failure, 'Failed.')
    throw failure
  }
}

function defaultOf(key: string) {
  return manifest.value.settings?.find((item) => item.name === key)?.default ?? ''
}

async function deployAndWait(appId: string) {
  const operation = await appsService.deployStoredSource(appId)
  for (let attempt = 0; attempt < 300; attempt++) {
    const current = await appsService.getDeploymentOperation(appId, operation.operationId)
    if (current.status === 'Succeeded') return
    if (current.status === 'Failed') throw new Error(current.error ?? 'The deployment failed.')
    await new Promise((resolve) => setTimeout(resolve, 2000))
  }
  throw new Error('The deployment did not finish in time. Check the app page.')
}

async function createSchedules(appId: string) {
  for (const item of manifest.value.schedules ?? []) {
    if (!schedules[item.name]) continue
    await actionSchedulesService.create({
      applicationId: appId,
      actionKey: item.action,
      name: `${name.value.trim()}: ${item.name}`,
      description: item.description ?? undefined,
      executionScope: 'AppRunner',
      scheduleType: item.cron ? 'Cron' : 'Interval',
      cronExpression: item.cron ?? undefined,
      intervalSeconds: item.cron ? undefined : item.intervalSeconds ?? undefined,
      timeZone: item.timeZone ?? 'Etc/UTC',
      enabled: true,
      concurrencyPolicy: 'SkipIfRunning',
      misfirePolicy: 'Skip',
      input: item.input ?? {},
    })
  }
}

async function install() {
  error.value = null
  steps.value = []
  installing.value = true
  try {
    const app = await step('Create your copy of the app', () => appsService.install(sourceId, name.value.trim(), runnerId.value ?? undefined))
    installedAppId.value = app.id
    const values = [
      // A setting left at its default is not saved, so the app keeps following the default.
      ...Object.entries(settings).filter(([key, value]) => value.trim() && value !== defaultOf(key)),
      ...Object.entries(secrets).filter(([, value]) => value),
    ]
    if (values.length > 0)
      await step('Save settings and secrets', async () => {
        for (const [key, value] of values) await appsService.setSecret(app.id, key, value)
      })
    if (permissions.value.length > 0) {
      if (isAdministrator.value && approvePermissions.value)
        await step('Approve the requested permissions', async () => {
          for (const permission of permissions.value) await adminAppsService.setPermission(app.id, permission, true)
        })
      else
        steps.value.push({ label: 'Permissions', state: 'skipped', detail: 'A system administrator still has to approve them on the app page.' })
    }
    await step('Deploy and start the app', () => deployAndWait(app.id))
    if (Object.values(schedules).some(Boolean)) await step('Create the schedules', () => createSchedules(app.id))
  } catch {
    error.value = 'The install stopped. The app was created; open it to fix the failed step and deploy again.'
  } finally {
    installing.value = false
  }
}
</script>

<template>
  <div class="install-app flex flex-column gap-4" style="max-width: 44rem">
    <h1 class="m-0">Install app</h1>
    <Message v-if="loadError" severity="error" :closable="false">{{ loadError }}</Message>
    <div v-else-if="!source" class="text-color-secondary">Loading…</div>

    <template v-else>
      <section class="flex flex-column gap-1">
        <h2 class="m-0">{{ source.name }}</h2>
        <p v-if="source.summary" class="m-0 text-color-secondary">{{ source.summary }}</p>
        <small class="text-color-secondary">By {{ source.creatorDisplayName }}. You get your own copy; you can install the same app more than once.</small>
      </section>

      <div class="flex flex-column gap-1">
        <label for="install-name">Name of your install</label>
        <InputText id="install-name" v-model="name" :disabled="installing || !!installedAppId" maxlength="200" />
      </div>

      <div class="flex flex-column gap-1">
        <label for="install-runner">Runner</label>
        <Select
          v-model="runnerId"
          input-id="install-runner"
          :options="runnerOptions"
          option-label="label"
          option-value="value"
          option-disabled="disabled"
          placeholder="Choose where the app runs"
          :disabled="installing || !!installedAppId"
        />
        <small v-if="!runnerOptions.length" class="text-color-secondary">No Runner is available to you yet; connect one first.</small>
      </div>

      <section v-if="manifest.settings?.length" class="flex flex-column gap-2" aria-labelledby="install-settings">
        <h3 id="install-settings" class="m-0">Settings</h3>
        <div v-for="item in manifest.settings" :key="item.name" class="flex flex-column gap-1">
          <label :for="`setting-${item.name}`">{{ item.name }}<span v-if="item.required"> (required)</span></label>
          <InputText :id="`setting-${item.name}`" v-model="settings[item.name]" :disabled="installing || !!installedAppId" />
          <small v-if="item.description" class="text-color-secondary">{{ item.description }}</small>
        </div>
      </section>

      <section v-if="manifest.secrets?.length" class="flex flex-column gap-2" aria-labelledby="install-secrets">
        <h3 id="install-secrets" class="m-0">Secrets</h3>
        <small class="text-color-secondary">Stored encrypted and never shown again.</small>
        <div v-for="item in manifest.secrets" :key="item.name" class="flex flex-column gap-1">
          <label :for="`secret-${item.name}`">{{ item.name }}<span v-if="item.from"> (can come from your {{ item.from.split(':')[0] }} vault)</span></label>
          <Password
            v-model="secrets[item.name]"
            :input-id="`secret-${item.name}`"
            :feedback="false"
            toggle-mask
            :disabled="installing || !!installedAppId"
            autocomplete="off"
          />
          <small v-if="item.description" class="text-color-secondary">{{ item.description }}</small>
        </div>
      </section>

      <section v-if="permissions.length" class="flex flex-column gap-2" aria-labelledby="install-permissions">
        <h3 id="install-permissions" class="m-0">Permissions</h3>
        <ul class="m-0"><li v-for="permission in permissions" :key="permission"><code>{{ permission }}</code></li></ul>
        <div v-if="isAdministrator" class="flex align-items-center gap-2">
          <Checkbox v-model="approvePermissions" input-id="approve-permissions" binary :disabled="installing || !!installedAppId" />
          <label for="approve-permissions">Approve these permissions for this install</label>
        </div>
        <Message v-else severity="info" :closable="false">A system administrator must approve these before the app can use them.</Message>
      </section>

      <section v-if="manifest.schedules?.length" class="flex flex-column gap-2" aria-labelledby="install-schedules">
        <h3 id="install-schedules" class="m-0">Schedules</h3>
        <div v-for="item in manifest.schedules" :key="item.name" class="flex align-items-start gap-2">
          <Checkbox v-model="schedules[item.name]" :input-id="`schedule-${item.name}`" binary :disabled="installing || !!installedAppId" />
          <label :for="`schedule-${item.name}`">
            {{ item.name }}
            <small class="text-color-secondary block">
              Runs <code>{{ item.action }}</code> {{ item.cron ? `on cron ${item.cron}` : `every ${item.intervalSeconds} seconds` }}<template v-if="item.description"> · {{ item.description }}</template>
            </small>
          </label>
        </div>
      </section>

      <ol v-if="steps.length" class="m-0" aria-live="polite">
        <li v-for="entry in steps" :key="entry.label">
          {{ entry.label }}: {{ entry.state === 'running' ? 'working…' : entry.state }}
          <small v-if="entry.detail" class="block text-color-secondary">{{ entry.detail }}</small>
        </li>
      </ol>
      <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
      <Message v-if="missingRequired.length && !installedAppId" severity="warn" :closable="false">Still needed: {{ missingRequired.join(', ') }}</Message>

      <div class="flex gap-2">
        <Button
          v-if="!installedAppId"
          label="Install"
          icon="pi pi-download"
          :loading="installing"
          :disabled="installing || !name.trim() || !runnerId || missingRequired.length > 0"
          @click="install"
        />
        <Button v-if="installedAppId && !installing" :label="error ? 'Open the app to fix it' : 'Open the installed app'" @click="router.push({ name: 'app-editor', params: { id: installedAppId } })" />
      </div>
    </template>
  </div>
</template>
