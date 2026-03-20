<script setup lang="ts">
/**
 * RunnerSelector Component
 * Dropdown to select a runner for app execution
 */
import { ref, onMounted, computed } from 'vue'
import Select from 'primevue/select'
import { useToast } from 'primevue/usetoast'
import axios from '@/shared/services/api/axios-instance'

// Props
const props = defineProps<{
  modelValue?: string | null
  disabled?: boolean
}>()

// Emits
const emit = defineEmits<{
  (e: 'update:modelValue', value: string | null): void
}>()

// State
interface Runner {
  id: string
  name: string
  status: 'Online' | 'Offline'
  lastSeenAt?: string
}

const runners = ref<Runner[]>([])
const isLoading = ref(false)
const toast = useToast()

// Computed
const selectedRunnerId = computed({
  get: () => props.modelValue,
  set: (value) => emit('update:modelValue', value || null),
})

const runnerOptions = computed(() => {
  return runners.value.map((runner) => ({
    label: `${runner.name} (${runner.status})`,
    value: runner.id,
    disabled: runner.status === 'Offline',
  }))
})

// Methods
async function loadRunners() {
  isLoading.value = true
  try {
    const { data } = await axios.get<Runner[]>('/api/runners')
    runners.value = data
    // Auto-select when no runner is chosen and exactly one online runner exists
    if (!props.modelValue) {
      const onlineRunners = data.filter((r) => r.status === 'Online')
      if (onlineRunners.length === 1) {
        emit('update:modelValue', onlineRunners[0].id)
      }
    }
  } catch (error) {
    console.error('Failed to load runners:', error)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load runners',
      life: 3000,
    })
  } finally {
    isLoading.value = false
  }
}

// Load runners on mount
onMounted(() => {
  loadRunners()
})
</script>

<template>
  <Select
    v-model="selectedRunnerId"
    :options="runnerOptions"
    option-label="label"
    option-value="value"
    placeholder="Select runner"
    :loading="isLoading"
    :disabled="disabled || isLoading"
    class="runner-selector"
  >
    <template #value="slotProps">
      <div v-if="slotProps.value" class="flex align-items-center gap-2">
        <i class="pi pi-server" style="font-size: 0.875rem"></i>
        <span>{{ runners.find(r => r.id === slotProps.value)?.name || 'Unknown runner' }}</span>
      </div>
      <span v-else class="text-color-secondary">{{ slotProps.placeholder }}</span>
    </template>
    
    <template #option="slotProps">
      <div class="flex align-items-center gap-2">
        <i class="pi pi-server" style="font-size: 0.875rem"></i>
        <span>{{ slotProps.option.label }}</span>
      </div>
    </template>
  </Select>
</template>

<style scoped>
.runner-selector {
  min-width: 200px;
}
</style>
