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
const sourceId = String(route.params.sourceAppId ?? '')
// A catalog entry or a pasted Git address arrives as repo, folder and commit in the address.
const gitSource = route.query.repo
  ? { repo: String(route.query.repo), folder: String(route.query.folder ?? ''), commit: String(route.query.commit ?? '') }
  : null

const source = ref<InstallSource | null>(null)
const loadError = ref<string | null>(null)
const name = ref('')
const settings = reactive<Record<string, string>>({})
const secrets = reactive<Record<string, string>>({})
const schedules = reactive<Record<string, boolean>>({})
const approvePermissions = ref(true)
const runnerOptions = ref<Array<{ label: string; value: string; disabled: boolean }>>([])
const runnerId = ref<string | null>(null)
type StepState = 'pending' | 'running' | 'done' | 'failed' | 'skipped'
const steps = ref<Array<{ key: string; label: string; state: StepState; detail?: string }>>([])
const installing = ref(false)
const installedAppId = ref<string | null>(null)
const error = ref<string | null>(null)

const manifest = computed<InstallManifest>(() => source.value?.application ?? {})
const isAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
const permissions = computed(() => manifest.value.permissions ?? [])
const missingRequired = computed(() => [
  ...(manifest.value.settings ?? []).filter((item) => item.required && !item.default && !settings[item.name]?.trim()).map((item) => item.name),
  ...(manifest.value.secrets ?? []).filter((item) => !item.from && !item.optional && !secrets[item.name]).map((item) => item.name),
])

onMounted(async () => {
  try {
    source.value = gitSource ? await appsService.getGitInstallSource(gitSource) : await appsService.getInstallSource(sourceId)
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
    loadError.value = message(failure, gitSource ? 'This app cannot be installed from that source.' : 'This app cannot be installed: it is not published, not forkable, or has no source.')
  }
})

function message(failure: unknown, fallback: string) {
  return isAxiosError(failure) ? failure.response?.data?.detail ?? failure.response?.data?.message ?? failure.response?.data?.error ?? fallback : fallback
}

async function step<T>(key: string, work: () => Promise<T>): Promise<T> {
  const entry = steps.value.find((item) => item.key === key)!
  entry.state = 'running'
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

/** Every step the install will run, so the progress panel shows what is coming from the start. */
function planSteps(hasValues: boolean) {
  const plan: typeof steps.value = [{ key: 'create', label: 'Create your copy of the app', state: 'pending' }]
  if (hasValues) plan.push({ key: 'values', label: 'Save settings and secrets', state: 'pending' })
  if (permissions.value.length > 0) {
    const approve = isAdministrator.value && approvePermissions.value
    plan.push({
      key: 'permissions',
      label: approve ? 'Approve the requested permissions' : 'Permissions',
      state: approve ? 'pending' : 'skipped',
      detail: approve ? undefined : 'A system administrator still has to approve them on the app page.',
    })
  }
  plan.push({ key: 'deploy', label: 'Deploy and start the app', state: 'pending' })
  if (Object.values(schedules).some(Boolean)) plan.push({ key: 'schedules', label: 'Create the schedules', state: 'pending' })
  return plan
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
  const values = [
    // A setting left at its default is not saved, so the app keeps following the default.
    ...Object.entries(settings).filter(([key, value]) => value.trim() && value !== defaultOf(key)),
    ...Object.entries(secrets).filter(([, value]) => value),
  ]
  steps.value = planSteps(values.length > 0)
  installing.value = true
  try {
    const app = await step('create', () => gitSource ? appsService.installFromGit(gitSource, name.value.trim(), runnerId.value ?? undefined) : appsService.install(sourceId, name.value.trim(), runnerId.value ?? undefined))
    installedAppId.value = app.id
    if (values.length > 0)
      await step('values', async () => {
        for (const [key, value] of values) await appsService.setSecret(app.id, key, value)
      })
    if (steps.value.some((item) => item.key === 'permissions' && item.state === 'pending'))
      await step('permissions', async () => {
        for (const permission of permissions.value) await adminAppsService.setPermission(app.id, permission, true)
      })
    await step('deploy', () => deployAndWait(app.id))
    if (steps.value.some((item) => item.key === 'schedules')) await step('schedules', () => createSchedules(app.id))
    succeeded.value = true
  } catch {
    if (installedAppId.value) {
      error.value = 'The install stopped. The app was created; open it to fix the failed step and deploy again.'
    } else {
      // Nothing was created, so show the form again with what the user typed.
      error.value = steps.value.find((item) => item.state === 'failed')?.detail ?? 'The install failed. Nothing was created.'
      steps.value = []
    }
  } finally {
    installing.value = false
  }
}

const started = computed(() => steps.value.length > 0)
const succeeded = ref(false)
const stepIcon: Record<StepState, string> = {
  pending: 'pi pi-circle',
  running: 'pi pi-spin pi-spinner',
  done: 'pi pi-check-circle',
  failed: 'pi pi-times-circle',
  skipped: 'pi pi-minus-circle',
}
const scheduleSummary = computed(() =>
  (manifest.value.schedules ?? [])
    .filter((item) => schedules[item.name])
    .map((item) => `${item.name} runs ${item.cron ? `on cron ${item.cron}` : `every ${item.intervalSeconds} seconds`}`),
)

function openInstalledApp() {
  router.push({ name: 'app-editor', params: { id: installedAppId.value } })
}
</script>

<template>
  <div class="install-app flex flex-column gap-4">
    <header class="flex flex-column gap-1">
      <h1 class="m-0">Install app</h1>
      <p v-if="source" class="m-0 text-color-secondary">
        <strong>{{ source.name }}</strong><template v-if="source.creatorDisplayName"> by {{ source.creatorDisplayName }}</template>
      </p>
    </header>
    <Message v-if="loadError" severity="error" :closable="false">{{ loadError }}</Message>
    <div v-else-if="!source" class="text-color-secondary">Loading…</div>

    <template v-else>
      <section v-if="started" class="install-progress" :class="{ 'install-progress--done': succeeded, 'install-progress--failed': !!error }" aria-labelledby="install-progress-title" aria-live="polite">
        <h2 id="install-progress-title" class="m-0">
          <template v-if="succeeded">Installed</template>
          <template v-else-if="error">Install stopped</template>
          <template v-else>Installing {{ name.trim() }}…</template>
        </h2>
        <ol class="install-steps m-0">
          <li v-for="entry in steps" :key="entry.key" :class="`install-step install-step--${entry.state}`">
            <i :class="stepIcon[entry.state]" aria-hidden="true"></i>
            <span>
              {{ entry.label }}
              <small v-if="entry.detail" class="block text-color-secondary">{{ entry.detail }}</small>
            </span>
          </li>
        </ol>
        <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
        <div v-if="succeeded" class="flex flex-column gap-2">
          <p class="m-0">{{ name.trim() }} is installed and running on your server.</p>
          <p v-if="manifest.publicAccess" class="m-0" data-testid="public-access-next">
            {{ manifest.publicAccess.level === 'required' ? 'To work for outside callers, this app must be public.' : 'To work for outside callers, make this app public.' }}
            Open the app, choose Share and set it to anyone with the link.
          </p>
          <ul v-if="scheduleSummary.length" class="m-0">
            <li v-for="line in scheduleSummary" :key="line">{{ line }}.</li>
          </ul>
        </div>
        <div v-if="installedAppId && !installing">
          <Button :label="error ? 'Open the app to fix it' : 'Open the installed app'" icon="pi pi-arrow-right" icon-pos="right" @click="openInstalledApp" />
        </div>
      </section>

      <div v-show="!started" class="install-layout">
        <div class="install-main flex flex-column gap-4">
          <div class="flex flex-column gap-1">
            <label for="install-name">Name of your install</label>
            <InputText id="install-name" v-model="name" maxlength="200" />
            <small class="text-color-secondary">Your copy gets this name.</small>
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
            />
            <small v-if="!runnerOptions.length" class="text-color-secondary">No Runner is available to you yet; connect one first.</small>
          </div>

          <Message v-if="manifest.publicAccess || manifest.openPaths?.length" severity="info" :closable="false" data-testid="public-access-notice">
            <p v-if="manifest.publicAccess" class="m-0">
              <strong>{{ manifest.publicAccess.level === 'required' ? 'Needs public access.' : 'Works best with public access.' }}</strong>
              {{ manifest.publicAccess.reason }}
              New apps are private: only you and the people you share them with can open them. After installing, open the app's Sharing and choose anyone with the link, or outside calls get 401.
            </p>
            <p v-if="manifest.openPaths?.length" class="m-0">
              These paths answer without sign-in, for outside calls: <code>{{ manifest.openPaths.join(', ') }}</code>. The rest of the app stays private.
            </p>
          </Message>

          <div class="install-fields">
            <section v-if="manifest.settings?.length" class="flex flex-column gap-2" aria-labelledby="install-settings">
              <h3 id="install-settings" class="m-0">Settings</h3>
              <div v-for="item in manifest.settings" :key="item.name" class="flex flex-column gap-1">
                <label :for="`setting-${item.name}`">{{ item.name }}<span v-if="item.required"> (required)</span></label>
                <InputText :id="`setting-${item.name}`" v-model="settings[item.name]" />
                <small v-if="item.description" class="text-color-secondary">{{ item.description }}</small>
              </div>
            </section>

            <section v-if="manifest.secrets?.length" class="flex flex-column gap-2" aria-labelledby="install-secrets">
              <h3 id="install-secrets" class="m-0">Secrets</h3>
              <small class="text-color-secondary">Stored encrypted and never shown again.</small>
              <div v-for="item in manifest.secrets" :key="item.name" class="flex flex-column gap-1">
                <label :for="`secret-${item.name}`">{{ item.name }}<span v-if="item.optional"> (optional)</span><span v-if="item.from"> (can come from your {{ item.from.split(':')[0] }} vault)</span></label>
                <Password
                  v-model="secrets[item.name]"
                  :input-id="`secret-${item.name}`"
                  :feedback="false"
                  toggle-mask
                  autocomplete="off"
                />
                <small v-if="item.description" class="text-color-secondary">{{ item.description }}</small>
              </div>
            </section>
          </div>
        </div>

        <aside class="install-side flex flex-column gap-4">
          <section class="install-summary flex flex-column gap-2" aria-labelledby="install-about">
            <h3 id="install-about" class="m-0">About this app</h3>
            <p v-if="source.summary" class="m-0">{{ source.summary }}</p>
            <dl v-if="source.git" class="install-source m-0">
              <dt>Source</dt>
              <dd><a :href="`https://github.com/${source.git.repo}`" target="_blank" rel="noopener">{{ source.git.repo }}</a><template v-if="source.git.folder"> / {{ source.git.folder }}</template></dd>
              <dt>Pinned commit</dt>
              <dd><a :href="`https://github.com/${source.git.repo}/tree/${source.git.commit}/${source.git.folder}`" target="_blank" rel="noopener"><code>{{ source.git.commit.slice(0, 12) }}</code></a></dd>
            </dl>
            <small class="text-color-secondary">You get your own copy; you can install the same app more than once.</small>
          </section>

          <section v-if="permissions.length" class="install-summary flex flex-column gap-2" aria-labelledby="install-permissions">
            <h3 id="install-permissions" class="m-0">Permissions</h3>
            <ul class="m-0"><li v-for="permission in permissions" :key="permission"><code>{{ permission }}</code></li></ul>
            <div v-if="isAdministrator" class="flex align-items-center gap-2">
              <Checkbox v-model="approvePermissions" input-id="approve-permissions" binary />
              <label for="approve-permissions">Approve these permissions for this install</label>
            </div>
            <Message v-else severity="info" :closable="false">A system administrator must approve these before the app can use them.</Message>
          </section>

          <section v-if="manifest.schedules?.length" class="install-summary flex flex-column gap-2" aria-labelledby="install-schedules">
            <h3 id="install-schedules" class="m-0">Schedules</h3>
            <div v-for="item in manifest.schedules" :key="item.name" class="flex align-items-start gap-2">
              <Checkbox v-model="schedules[item.name]" :input-id="`schedule-${item.name}`" binary />
              <label :for="`schedule-${item.name}`">
                {{ item.name }}
                <small class="text-color-secondary block">
                  Runs <code>{{ item.action }}</code> {{ item.cron ? `on cron ${item.cron}` : `every ${item.intervalSeconds} seconds` }}<template v-if="item.description"> · {{ item.description }}</template>
                </small>
              </label>
            </div>
          </section>
        </aside>
      </div>

      <template v-if="!started">
        <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
        <Message v-if="missingRequired.length" severity="warn" :closable="false">Still needed: {{ missingRequired.join(', ') }}</Message>
        <div>
          <Button
            label="Install"
            icon="pi pi-download"
            :disabled="!name.trim() || !runnerId || missingRequired.length > 0"
            @click="install"
          />
        </div>
      </template>
    </template>
  </div>
</template>

<style scoped>
.install-app {
  max-width: 72rem;
}

.install-layout {
  display: grid;
  grid-template-columns: minmax(0, 3fr) minmax(16rem, 2fr);
  gap: 1.5rem;
  align-items: start;
}

.install-fields {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr));
  gap: 1.5rem;
  align-items: start;
}

.install-summary,
.install-progress {
  padding: 1rem 1.25rem;
  border: 1px solid var(--p-content-border-color);
  border-radius: var(--p-content-border-radius);
  background: var(--p-content-background);
}

.install-source a {
  color: var(--p-primary-color);
}

.install-source dt {
  color: var(--p-text-muted-color);
  font-size: 0.85rem;
}

.install-source dd {
  margin: 0 0 0.5rem;
  overflow-wrap: anywhere;
}

.install-progress {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.install-progress--done {
  border-color: var(--p-green-500);
}

.install-progress--failed {
  border-color: var(--p-red-500);
}

.install-steps {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 0;
  list-style: none;
}

.install-step {
  display: flex;
  gap: 0.6rem;
  align-items: flex-start;
}

.install-step--pending,
.install-step--skipped {
  color: var(--p-text-muted-color);
}

.install-step--running i {
  color: var(--p-primary-color);
}

.install-step--done i {
  color: var(--p-green-500);
}

.install-step--failed i {
  color: var(--p-red-500);
}

@media (max-width: 900px) {
  .install-layout {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
