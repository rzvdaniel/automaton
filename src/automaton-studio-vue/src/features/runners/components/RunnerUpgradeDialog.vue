<script setup lang="ts">
import { ref } from 'vue'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import Textarea from 'primevue/textarea'
import Button from 'primevue/button'
import type { RunnerUpgradeInstructions } from '../types/runners.types'

const props = defineProps<{ instructions: RunnerUpgradeInstructions; runnerName: string }>()
const visible = defineModel<boolean>('visible', { default: false })
const error = ref<string | null>(null)

async function copyCommand() {
  try { await navigator.clipboard.writeText(props.instructions.upgradeCommand) }
  catch { error.value = 'Select and copy the upgrade command manually.' }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Upgrade Runner" :style="{ width: '48rem', maxWidth: '95vw' }">
    <Message v-if="instructions.upToDate" severity="success" :closable="false">
      {{ runnerName }} already reports the server's current Runner image.
    </Message>
    <p>
      Upgrade {{ runnerName }} by running this command on its server with Docker access. It recreates the
      Runner container on <code>{{ instructions.image }}</code> and keeps its identity, configuration and workspace.
    </p>
    <Message severity="info" :closable="false">
      Deployed apps keep running. The Runner reconnects within a minute; if the new container does not start,
      the command restores the previous one.
    </Message>
    <Textarea :model-value="instructions.upgradeCommand" readonly rows="12" class="w-full mt-4"
      aria-label="Runner upgrade command" />
    <Button label="Copy command" icon="pi pi-copy" text @click="copyCommand" />
    <Message v-if="error" severity="error" :closable="false" class="mt-4">{{ error }}</Message>
    <template #footer>
      <Button label="Close" text @click="visible = false" />
    </template>
  </Dialog>
</template>
