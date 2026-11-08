<script setup lang="ts">
import { ref } from 'vue'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import { isAxiosError } from 'axios'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import RunnerRemovalDialog from '@/features/runners/components/RunnerRemovalDialog.vue'
import RunnerUpgradeDialog from '@/features/runners/components/RunnerUpgradeDialog.vue'
import { runnersService } from '@/features/runners/services/runners.service'
import {
  RunnerStatus,
  getStatusDisplayName,
  getStatusSeverity,
  isRunnerLinkedToProject,
  type Runner,
  type RunnerRemovalInstructions,
} from '@/features/runners/types/runners.types'

defineProps<{ runners: Runner[] }>()
const emit = defineEmits<{ changed: [] }>()

const toast = useToast()
const confirm = useConfirm()
const removal = ref<RunnerRemovalInstructions | null>(null)
const removalName = ref('')
const showRemoval = ref(false)
const upgrading = ref<Runner | null>(null)
const showUpgrade = ref(false)

// Only a Runner installed with Studio's command can be upgraded or removed from here.
const canUpgrade = (runner: Runner) => !runner.isHostedRunner && runner.applicationProfile === 'MachineManagement'

function openUpgrade(runner: Runner) {
  upgrading.value = runner
  showUpgrade.value = true
}

function confirmRemoval(runner: Runner) {
  confirm.require({
    message: `Prepare removal of "${runner.name}"? All app environments must be moved or deleted first. New assignments will be blocked, and you will receive a command to uninstall it on its server.`,
    header: 'Delete Runner',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Prepare removal',
    acceptClass: 'p-button-danger',
    accept: () => void prepareRemoval(runner),
  })
}

async function prepareRemoval(runner: Runner) {
  try {
    removal.value = await runnersService.prepareRemoval(runner.id)
    removalName.value = runner.name
    showRemoval.value = true
    emit('changed')
  } catch (error) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: isAxiosError(error) ? error.response?.data?.message ?? 'Failed to prepare Runner removal' : 'Failed to prepare Runner removal',
      life: 10000,
    })
  }
}
</script>

<template>
  <div class="runner-rows" data-testid="runners-list">
    <div v-for="runner in runners" :key="runner.id" class="runner-row" data-testid="runner-row">
      <div class="runner-row__name">
        <span class="runner-row__dot" :class="runner.status === RunnerStatus.Online ? 'is-online' : 'is-offline'" />
        <span class="font-semibold">{{ runner.name }}</span>
        <Tag :value="getStatusDisplayName(runner.status)" :severity="getStatusSeverity(runner.status)" />
        <Tag
          v-if="isRunnerLinkedToProject(runner)" :value="`Project: ${runner.projectName}`" severity="info"
        />
      </div>
      <dl class="runner-row__facts">
        <div><dt>OS</dt><dd data-testid="runner-os">{{ runner.operatingSystem || 'Unknown' }}</dd></div>
        <div><dt>Apps</dt><dd data-testid="runner-apps">{{ runner.appCount ?? 0 }}</dd></div>
        <div><dt>Version</dt><dd data-testid="runner-version">{{ runner.version || 'Unknown' }}</dd></div>
      </dl>
      <div class="runner-row__actions">
        <Button
          v-if="canUpgrade(runner) && runner.upgradeAvailable" label="Upgrade" icon="pi pi-refresh" size="small"
          data-testid="runner-upgrade-button" @click="openUpgrade(runner)"
        />
        <Button
          v-if="!runner.isHostedRunner && !isRunnerLinkedToProject(runner)" v-tooltip.top="'Delete Runner'"
          icon="pi pi-trash" text rounded size="small" severity="danger" aria-label="Delete Runner" @click="confirmRemoval(runner)"
        />
      </div>
    </div>

    <RunnerRemovalDialog
      v-if="removal" :key="removal.runnerId" v-model:visible="showRemoval" :instructions="removal"
      :runner-name="removalName" @removed="emit('changed')"
    />
    <RunnerUpgradeDialog
      v-if="upgrading" :key="upgrading.id" v-model:visible="showUpgrade" :runner-id="upgrading.id"
      :runner-name="upgrading.name" :online="upgrading.status === RunnerStatus.Online"
      :upgrade-available="!!upgrading.upgradeAvailable" @finished="emit('changed')"
    />
  </div>
</template>

<style scoped>
.runner-rows {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--p-content-border-color);
  border-radius: 8px;
  background: var(--p-content-background);
}

.runner-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.75rem 1.5rem;
  padding: 0.85rem 1rem;
}

.runner-row + .runner-row {
  border-top: 1px solid var(--p-content-border-color);
}

.runner-row__name {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.5rem;
  flex: 1 1 14rem;
  min-width: 0;
}

.runner-row__dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}

.is-online { background: var(--p-green-500); }
.is-offline { background: var(--p-red-500); }

.runner-row__facts {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 2rem;
  margin: 0;
}

.runner-row__facts dt {
  color: var(--p-text-muted-color);
  font-size: 0.8rem;
}

.runner-row__facts dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.runner-row__actions {
  display: flex;
  gap: 0.25rem;
  margin-left: auto;
}
</style>
