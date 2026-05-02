<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'
import Select from 'primevue/select'
import { ConversationMode } from '@/shared/types/ai.types'

const props = withDefaults(
  defineProps<{
    mode: string
    model: string
    availableModels: string[]
    modelDisplayNames?: Record<string, string>
    disabled?: boolean
    loading?: boolean
    /**
     * compact = text button + short labels ("Auto" / "HITM") — used inside chat toolbars
     * default = outlined button + full labels ("Autonomous" / "Human-in-the-middle") — used on landing cards
     */
    compact?: boolean
    /**
     * When true, a "Demo" badge is shown next to the model selector to indicate the shared
     * admin key is being used rather than the user's own key.
     */
    sharedKey?: boolean
  }>(),
  {
    disabled: false,
    loading: false,
    compact: false,
    sharedKey: false,
  },
)

const emit = defineEmits<{
  'update:mode': [value: string]
  'update:model': [value: string]
}>()

const isAutonomous = computed(() => props.mode === ConversationMode.Autonomous)
const modelOptions = computed(() =>
  props.availableModels.map((value) => ({
    value,
    label: props.modelDisplayNames?.[value] || value,
  })),
)

function toggleMode() {
  emit('update:mode', isAutonomous.value ? ConversationMode.HumanInTheMiddle : ConversationMode.Autonomous)
}
</script>

<template>
  <div class="ai-mode-model-selector">
    <Button
      v-tooltip.top="
        isAutonomous
          ? 'Autonomous: AI builds everything without pausing. Click to switch.'
          : 'Human-in-the-middle: AI proposes plans and waits for approval. Click to switch.'
      "
      :label="compact ? (isAutonomous ? 'Auto' : 'HITM') : isAutonomous ? 'Autonomous' : 'Human-in-the-middle'"
      :icon="isAutonomous ? 'pi pi-bolt' : 'pi pi-users'"
      :severity="isAutonomous ? 'contrast' : 'secondary'"
      size="small"
      :text="compact"
      :outlined="!compact"
      :disabled="disabled"
      @click="toggleMode"
    />
    <Select
      v-if="availableModels.length > 0 || loading"
      :model-value="model"
      :options="modelOptions"
      option-label="label"
      option-value="value"
      :placeholder="compact ? 'Model' : 'Select model'"
      size="small"
      :class="compact ? 'model-select-inline' : 'w-14rem'"
      :disabled="disabled || availableModels.length === 0"
      :loading="loading"
      @update:model-value="(val: string) => emit('update:model', val)"
    />
    <span
      v-if="sharedKey"
      v-tooltip.top="'Using shared demo key. Add your own API key for unlimited use.'"
      class="demo-badge"
    >Demo</span>
  </div>
</template>

<style scoped>
.ai-mode-model-selector {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.demo-badge {
  font-size: 0.65rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  padding: 0.15rem 0.45rem;
  border-radius: 0.75rem;
  background-color: var(--p-yellow-100);
  color: var(--p-yellow-800);
  cursor: default;
  white-space: nowrap;
}

:global(.app-dark) .demo-badge {
  background-color: var(--p-yellow-900);
  color: var(--p-yellow-200);
}
</style>
