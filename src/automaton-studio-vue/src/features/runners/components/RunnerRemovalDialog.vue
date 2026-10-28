<script setup lang="ts">
import { ref } from 'vue'
import { isAxiosError } from 'axios'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import Button from 'primevue/button'
import { runnersService } from '../services/runners.service'
import type { RunnerRemovalInstructions } from '../types/runners.types'

const props = defineProps<{ instructions: RunnerRemovalInstructions; runnerName: string }>()
const visible = defineModel<boolean>('visible', { default: false })
const emit = defineEmits<{ removed: [] }>()
const confirmed = ref(false)
const submitting = ref(false)
const error = ref<string | null>(null)

async function remove() {
  if (!confirmed.value) return
  submitting.value = true
  error.value = null
  try {
    await runnersService.delete(props.instructions.runnerId)
    visible.value = false
    emit('removed')
  } catch (failure) {
    error.value = isAxiosError(failure)
      ? failure.response?.data?.message ?? 'Could not remove the Runner. Retry after completing host cleanup.'
      : 'Could not remove the Runner. Retry after completing host cleanup.'
  } finally {
    submitting.value = false
  }
}

async function copyCommand() {
  try { await navigator.clipboard.writeText(props.instructions.uninstallCommand) }
  catch { error.value = 'Select and copy the uninstall command manually.' }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Remove Runner" :style="{ width: '48rem', maxWidth: '95vw' }">
    <p>Remove {{ runnerName }} by running this command on its server with Docker access.</p>
    <Message severity="warn" :closable="false">
      New app assignments are blocked. The command removes the management Runner, its shared static
      server, and its workspace volume. It refuses to proceed if app/Action containers or retained
      Compose app data volumes remain.
      Closing this dialog keeps removal pending; use Delete Runner again to resume.
    </Message>
    <Textarea :model-value="instructions.uninstallCommand" readonly rows="12" class="w-full mt-4"
      aria-label="Runner uninstall command" />
    <Button label="Copy command" icon="pi pi-copy" text @click="copyCommand" />
    <div class="flex gap-2 align-items-center mt-4">
      <Checkbox v-model="confirmed" binary input-id="runner-host-cleanup-confirmed" />
      <label for="runner-host-cleanup-confirmed">I ran the command successfully on the Runner server.</label>
    </div>
    <Message v-if="error" severity="error" :closable="false" class="mt-4">{{ error }}</Message>
    <template #footer>
      <Button label="Close" text :disabled="submitting" @click="visible = false" />
      <Button label="Confirm removal" severity="danger" :disabled="!confirmed" :loading="submitting" @click="remove" />
    </template>
  </Dialog>
</template>
