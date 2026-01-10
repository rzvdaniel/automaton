<script setup lang="ts">
/**
 * StepsPanel.vue
 * Backend steps management panel for the App Editor
 * Displays linked steps and allows creating/managing them
 */
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Tag from 'primevue/tag'
import Message from 'primevue/message'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { LinkedStepInfo } from '../types/apps.types'

const emit = defineEmits<{
  createStep: []
  viewCode: [step: LinkedStepInfo]
}>()

const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const editorStore = useAppEditorStore()

// Search filter
const searchText = ref('')

// Filtered steps
const filteredSteps = computed(() => {
  if (!searchText.value) return editorStore.linkedSteps

  const search = searchText.value.toLowerCase()
  return editorStore.linkedSteps.filter(
    (step) =>
      step.name.toLowerCase().includes(search) ||
      step.displayName?.toLowerCase().includes(search) ||
      step.description?.toLowerCase().includes(search),
  )
})

// Get language icon
function getLanguageIcon(language: string): string {
  switch (language?.toLowerCase()) {
    case 'python':
      return 'pi pi-code'
    case 'powershell':
      return 'pi pi-terminal'
    case 'batch':
      return 'pi pi-desktop'
    default:
      return 'pi pi-file'
  }
}

// Get language color
function getLanguageColor(language: string): 'info' | 'success' | 'warn' | 'danger' | undefined {
  switch (language?.toLowerCase()) {
    case 'python':
      return 'info'
    case 'powershell':
      return 'warn'
    case 'batch':
      return 'success'
    default:
      return undefined
  }
}

// Handle edit step - navigate to Step Designer
function handleEditStep(stepId: string) {
  router.push(`/steps/${stepId}/edit`)
}

// Handle view code
function handleViewCode(step: LinkedStepInfo) {
  emit('viewCode', step)
}

// Handle unlink step
function handleUnlinkStep(step: LinkedStepInfo) {
  confirm.require({
    message: `Unlink step "${step.displayName || step.name}" from this app?`,
    header: 'Confirm Unlink',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Unlink',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await editorStore.unlinkStep(step.id)
        toast.add({
          severity: 'success',
          summary: 'Unlinked',
          detail: 'Step unlinked from app',
          life: 3000,
        })
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to unlink step',
          life: 5000,
        })
      }
    },
  })
}

// Generate usage code for a step
function generateUsageCode(step: LinkedStepInfo): string {
  return `const result = await automatonApi.executeStep('${step.name}', { inputs: {} });
console.log(result.outputVariables);`
}

// Copy code to clipboard
async function copyCode(step: LinkedStepInfo) {
  try {
    await navigator.clipboard.writeText(generateUsageCode(step))
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'Code copied to clipboard',
      life: 2000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to copy code',
      life: 3000,
    })
  }
}
</script>

<template>
  <div class="steps-panel">
    <!-- Header -->
    <div class="steps-header">
      <span class="font-semibold text-sm">Backend Steps</span>
      <Button
        icon="pi pi-plus"
        label="New"
        size="small"
        @click="emit('createStep')"
      />
    </div>

    <!-- Content -->
    <div class="steps-content">
      <!-- Empty state -->
      <div v-if="editorStore.linkedSteps.length === 0" class="steps-empty">
        <i class="pi pi-puzzle-piece text-4xl text-color-secondary"></i>
        <p class="mt-2 text-color-secondary">No backend steps linked</p>
        <p class="text-sm text-color-secondary mb-3">
          Create steps to add backend functionality
        </p>
        <Button
          label="Create Step"
          icon="pi pi-plus"
          size="small"
          @click="emit('createStep')"
        />
      </div>

      <!-- Steps list -->
      <div v-else class="steps-list">
        <Card
          v-for="step in filteredSteps"
          :key="step.id"
          class="step-card mb-2"
        >
          <template #content>
            <div class="step-card-content">
              <!-- Step info -->
              <div class="flex align-items-start gap-2 mb-2">
                <i :class="getLanguageIcon(step.language)" class="text-xl text-color-secondary"></i>
                <div class="flex-1 min-w-0">
                  <div class="font-semibold text-overflow-ellipsis overflow-hidden">
                    {{ step.displayName || step.name }}
                  </div>
                  <div
                    v-if="step.description"
                    class="text-sm text-color-secondary text-overflow-ellipsis overflow-hidden"
                  >
                    {{ step.description }}
                  </div>
                </div>
              </div>

              <!-- Step metadata -->
              <div class="flex align-items-center gap-2 mb-2">
                <Tag :value="step.language" :severity="getLanguageColor(step.language)" size="small" />
              </div>

              <!-- Actions -->
              <div class="flex gap-1 flex-wrap">
                <Button
                  label="Edit"
                  icon="pi pi-pencil"
                  size="small"
                  outlined
                  @click="handleEditStep(step.id)"
                />
                <Button
                  label="Copy"
                  icon="pi pi-copy"
                  size="small"
                  text
                  severity="secondary"
                  @click="copyCode(step)"
                  v-tooltip.bottom="'Copy usage code'"
                />
                <Button
                  icon="pi pi-times"
                  size="small"
                  text
                  severity="danger"
                  @click="handleUnlinkStep(step)"
                  v-tooltip.bottom="'Unlink'"
                />
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Usage hint -->
      <Message
        v-if="editorStore.linkedSteps.length > 0"
        severity="info"
        :closable="false"
        class="usage-hint"
      >
        <template #messageicon>
          <i class="pi pi-info-circle"></i>
        </template>
        <div class="text-sm">
          <strong>Usage:</strong>
          <pre class="usage-code">const result = await automatonApi.executeStep('StepName', { inputs: {} });</pre>
        </div>
      </Message>
    </div>
  </div>
</template>

<style scoped>
.steps-panel {
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: var(--p-surface-0);
}

.steps-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.steps-content {
  flex: 1;
  overflow-y: auto;
  padding: 1rem;
}

.steps-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  text-align: center;
  padding: 2rem;
}

.steps-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.step-card {
  transition: box-shadow 0.2s;
}

.step-card:hover {
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
}

.step-card :deep(.p-card-content) {
  padding: 0.75rem;
}

.step-card-content {
  font-size: 0.875rem;
}

.usage-hint {
  margin-top: 1rem;
}

.usage-hint :deep(.p-message-wrapper) {
  padding: 0.5rem;
}

.usage-code {
  background-color: var(--p-surface-100);
  padding: 0.5rem;
  border-radius: var(--p-border-radius);
  font-family: 'JetBrains Mono', 'Consolas', monospace;
  font-size: 0.75rem;
  margin-top: 0.5rem;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-all;
}

/* Dark mode */
:global(.app-dark) .steps-panel {
  background-color: var(--p-surface-800);
}

:global(.app-dark) .steps-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .usage-code {
  background-color: var(--p-surface-700);
}
</style>
