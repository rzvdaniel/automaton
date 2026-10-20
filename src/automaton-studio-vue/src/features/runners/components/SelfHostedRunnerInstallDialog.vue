<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import SelectButton from 'primevue/selectbutton'
import { useToast } from 'primevue/usetoast'
import { runnersService } from '../services/runners.service'
import {
  RunnerApplicationProfiles,
  SelfHostedRunnerInstallationStatuses,
  type RunnerApplicationProfile,
  type SelfHostedRunnerInstallation,
  type SelfHostedRunnerInstallationStatus,
} from '../types/runners.types'
import {
  canCreateSelfHostedInstallation,
  createSelfHostedInstallationRequest,
  getSelfHostedInstallationStatusLabel,
  isSelfHostedInstallationPollingComplete,
  isValidSelfHostedRunnerName,
} from '../utils/self-hosted-installation'

const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ created: [] }>()
const toast = useToast()

const runnerName = ref('')
const profile = ref<RunnerApplicationProfile>(RunnerApplicationProfiles.Standard)
const acknowledgedHostAccess = ref(false)
const submitting = ref(false)
const errorMessage = ref<string | null>(null)
const installation = ref<SelfHostedRunnerInstallation | null>(null)
const installationStatus = ref<SelfHostedRunnerInstallationStatus | null>(null)
const refreshingStatus = ref(false)
let statusPollTimer: ReturnType<typeof setTimeout> | null = null

const profileOptions = [
  {
    label: 'Standard',
    value: RunnerApplicationProfiles.Standard,
    description:
      'Runs Automaton apps with normal container isolation. Recommended for ordinary application workloads.',
  },
  {
    label: 'Machine Management',
    value: RunnerApplicationProfiles.MachineManagement,
    description:
      'Runs one trusted management Runner on this machine. Requires Ubuntu and Docker Engine; ext4 is the tested storage baseline, and other working Docker filesystems are accepted. Actions run in isolated containers; raw workflows and management-process custom steps remain disabled.',
  },
  {
    label: 'Privileged Host',
    value: RunnerApplicationProfiles.PrivilegedHost,
    description:
      'Runs trusted infrastructure-aware apps with administrator-level access to this machine.',
  },
]

const isPrivileged = computed(() => profile.value === RunnerApplicationProfiles.PrivilegedHost)
const requiresHostAccess = computed(() =>
  profile.value === RunnerApplicationProfiles.PrivilegedHost ||
  profile.value === RunnerApplicationProfiles.MachineManagement
)
const isValidName = computed(() => isValidSelfHostedRunnerName(runnerName.value))
const canCreate = computed(
  () =>
    canCreateSelfHostedInstallation(
      runnerName.value,
      profile.value,
      acknowledgedHostAccess.value
    ) && !submitting.value
)
const warning = computed(
  () =>
    installation.value?.hostAccessWarning ??
    (isPrivileged.value
      ? 'Apps deployed to this Runner may read and modify host files, inspect host processes, control Docker containers, affect services and system state, and access secrets stored on this machine.'
      : 'Automaton exposes typed app-container operations and disables Actions and workflows, but the Runner itself is trusted infrastructure: a compromise could control host Docker. Ordinary app and build code must remain isolated in containers.')
)

watch(profile, () => {
  acknowledgedHostAccess.value = false
})
watch(visible, (isVisible) => {
  if (!isVisible) {
    stopStatusPolling()
    reset()
  }
})

function reset() {
  runnerName.value = ''
  profile.value = RunnerApplicationProfiles.Standard
  acknowledgedHostAccess.value = false
  errorMessage.value = null
  installation.value = null
  installationStatus.value = null
  refreshingStatus.value = false
}

async function createInstallation() {
  if (!canCreate.value) return
  submitting.value = true
  errorMessage.value = null
  try {
    installation.value = await runnersService.createSelfHostedInstallation(
      createSelfHostedInstallationRequest(
        runnerName.value,
        profile.value,
        acknowledgedHostAccess.value
      )
    )
    installationStatus.value = installation.value.status
    scheduleStatusRefresh()
    emit('created')
  } catch {
    errorMessage.value =
      'Unable to create the installation. Check your Runner name and permissions, then try again.'
  } finally {
    submitting.value = false
  }
}

function stopStatusPolling() {
  if (statusPollTimer !== null) {
    clearTimeout(statusPollTimer)
    statusPollTimer = null
  }
}

function scheduleStatusRefresh() {
  stopStatusPolling()
  if (
    !visible.value ||
    !installation.value ||
    (installationStatus.value && isSelfHostedInstallationPollingComplete(installationStatus.value))
  ) {
    return
  }
  statusPollTimer = setTimeout(() => void refreshStatus(), 2000)
}

async function refreshStatus() {
  if (!visible.value || !installation.value || refreshingStatus.value) return
  refreshingStatus.value = true
  try {
    const response = await runnersService.getSelfHostedInstallationStatus(
      installation.value.installationId
    )
    installationStatus.value = response.status
  } catch {
    toast.add({
      severity: 'warn',
      summary: 'Status unavailable',
      detail: 'Could not refresh enrollment status. This does not mean the container failed.',
      life: 3000,
    })
  } finally {
    refreshingStatus.value = false
    scheduleStatusRefresh()
  }
}

async function copyCommand() {
  if (!installation.value) return
  try {
    await navigator.clipboard.writeText(installation.value.installationCommand)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'Docker command copied to clipboard.',
      life: 2000,
    })
  } catch {
    toast.add({
      severity: 'warn',
      summary: 'Copy failed',
      detail: 'Could not copy the Docker command.',
      life: 3000,
    })
  }
}

function close() {
  visible.value = false
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    header="Install Self-Hosted Runner"
    modal
    :style="{ width: 'min(680px, calc(100vw - 2rem))' }"
  >
    <template v-if="!installation">
      <p class="text-color-secondary mt-0">
        Create a one-time Docker installation command for a machine you own.
      </p>
      <Message
        v-if="profile === RunnerApplicationProfiles.MachineManagement"
        severity="info"
        :closable="false"
        class="mb-4"
      >
        Prepare an Ubuntu host with Docker Engine installed and running. ext4 is the tested storage baseline; working Docker storage on XFS or another filesystem does not need to be moved. The command checks Ubuntu and Docker availability, but does not install Docker or move or reconfigure its data.
      </Message>
      <div class="field">
        <label for="self-hosted-runner-name" class="font-semibold block mb-2">Runner name</label>
        <InputText
          id="self-hosted-runner-name"
          v-model="runnerName"
          class="w-full"
          maxlength="120"
          autocomplete="off"
        />
        <small v-if="runnerName && !isValidName" class="p-error"
          >Enter a Runner name between 1 and 120 characters.</small
        >
      </div>
      <div class="field mt-4">
        <label class="font-semibold block mb-2">Installation profile</label>
        <SelectButton
          v-model="profile"
          :options="profileOptions"
          option-label="label"
          option-value="value"
          :allow-empty="false"
        />
        <p class="text-color-secondary text-sm mb-0 mt-2">
          {{ profileOptions.find((option) => option.value === profile)?.description }}
        </p>
      </div>
      <Message v-if="requiresHostAccess" severity="warn" :closable="false" class="mt-4">
        <strong>{{ isPrivileged ? 'Privileged Host access' : 'Machine management access' }}</strong><br />{{ warning }}
        <div class="flex align-items-start gap-2 mt-3">
          <Checkbox
            v-model="acknowledgedHostAccess"
            input-id="host-access-acknowledgement"
            binary
          />
          <label for="host-access-acknowledgement"
            >{{
              isPrivileged
                ? 'I understand that apps on this Runner will have administrator-level access to this machine.'
                : 'I understand that this trusted Runner can manage Docker containers on this machine, and user app code must remain isolated in containers.'
            }}</label
          >
        </div>
      </Message>
      <Message v-if="errorMessage" severity="error" :closable="false" class="mt-4">{{
        errorMessage
      }}</Message>
    </template>
    <template v-else>
      <Message severity="warn" :closable="false" class="mb-4"
        >This command contains a one-time enrollment credential and is shown only for this
        installation. Do not share it.</Message
      >
      <Message
        v-if="installation.requiresHostAccessAcknowledgement"
        severity="warn"
        :closable="false"
        class="mb-4"
      >
        <strong>{{ installation.applicationProfile === RunnerApplicationProfiles.MachineManagement ? 'Machine management access' : 'Privileged Host access' }}</strong><br />{{ installation.hostAccessWarning ?? warning }}
      </Message>
      <div class="installation-summary">
        <span><strong>Runner:</strong> {{ installation.runnerName }}</span>
        <span
          ><strong>Profile:</strong>
          {{
            installation.applicationProfile === RunnerApplicationProfiles.PrivilegedHost
              ? 'Privileged Host'
              : installation.applicationProfile === RunnerApplicationProfiles.MachineManagement
                ? 'Machine Management'
                : 'Standard'
          }}</span
        >
        <span
          ><strong>Expires:</strong>
          {{ new Date(installation.expiresAtUtc).toLocaleString() }}</span
        >
      </div>
      <Message
        v-if="installationStatus"
        :severity="
          installationStatus === SelfHostedRunnerInstallationStatuses.RunnerConnected
            ? 'success'
            : installationStatus === SelfHostedRunnerInstallationStatuses.Expired ||
                installationStatus === SelfHostedRunnerInstallationStatuses.OfflineAfterEnrollment
              ? 'warn'
              : 'info'
        "
        :closable="false"
        class="mb-3"
      >
        {{ getSelfHostedInstallationStatusLabel(installationStatus) }}
      </Message>
      <pre class="installation-command"><code>{{ installation.installationCommand }}</code></pre>
      <ol class="pl-4 text-color-secondary">
        <li>Run the command on your Docker host.</li>
        <li>Wait for the Runner container to start.</li>
        <li>Return to Runners and refresh to confirm it is connected.</li>
      </ol>
    </template>
    <template #footer>
      <template v-if="!installation">
        <Button label="Cancel" text @click="close" />
        <Button
          label="Create installation"
          icon="pi pi-server"
          :disabled="!canCreate"
          :loading="submitting"
          @click="createInstallation"
        />
      </template>
      <template v-else>
        <Button
          label="Refresh status"
          icon="pi pi-refresh"
          text
          :loading="refreshingStatus"
          @click="refreshStatus"
        />
        <Button label="Copy command" icon="pi pi-copy" @click="copyCommand" />
        <Button label="Close" text @click="close" />
      </template>
    </template>
  </Dialog>
</template>

<style scoped>
.installation-summary {
  display: grid;
  gap: 0.4rem;
  margin-bottom: 1rem;
}
.installation-command {
  max-height: 260px;
  overflow: auto;
  white-space: pre;
  background: #1e1e1e;
  color: #d4d4d4;
  border-radius: 8px;
  padding: 1rem;
  font-size: 0.8rem;
}
</style>
