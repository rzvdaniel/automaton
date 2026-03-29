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
    disabled?: boolean
    loading?: boolean
    /**
     * compact = text button + short labels ("Auto" / "HITM") — used inside chat toolbars
     * default = outlined button + full labels ("Autonomous" / "Human-in-the-middle") — used on landing cards
     */
    compact?: boolean
  }>(),
  {
    disabled: false,
    loading: false,
    compact: false,
  },
)

const emit = defineEmits<{
  'update:mode': [value: string]
  'update:model': [value: string]
}>()

const isAutonomous = computed(() => props.mode === ConversationMode.Autonomous)

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
      :options="availableModels"
      :placeholder="compact ? 'Model' : 'Select model'"
      size="small"
      :class="compact ? 'model-select-inline' : 'w-14rem'"
      :disabled="disabled || availableModels.length === 0"
      :loading="loading"
      @update:model-value="(val: string) => emit('update:model', val)"
    />
  </div>
</template>
