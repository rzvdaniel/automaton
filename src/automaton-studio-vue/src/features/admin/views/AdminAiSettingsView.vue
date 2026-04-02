<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import InputNumber from 'primevue/inputnumber'
import Button from 'primevue/button'
import Divider from 'primevue/divider'
import { adminAiProvidersService } from '../services/adminAiProvidersService'
import type { AiProvidersOperationalConfig } from '../types/admin.types'

const toast = useToast()

const loading = ref(false)
const saving = ref(false)

const settings = ref<AiProvidersOperationalConfig>({
  maxConversationHistoryMessages: 30,
  maxIterations: 0,
  maxConsecutiveFailures: 3,
  defaultMaxOutputTokens: 65536,
  iterationLimits: {
    simple: 15,
    medium: 25,
    large: 40,
    enterprise: 60,
    autonomous: 0,
    planning: 0,
  },
})

onMounted(async () => {
  await loadSettings()
})

async function loadSettings() {
  loading.value = true
  try {
    settings.value = await adminAiProvidersService.getAiSettings()
  } catch (err) {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load AI settings', life: 3000 })
  } finally {
    loading.value = false
  }
}

async function saveSettings() {
  saving.value = true
  try {
    settings.value = await adminAiProvidersService.saveAiSettings(settings.value)
    toast.add({ severity: 'success', summary: 'Saved', detail: 'AI settings updated. Changes are effective immediately.', life: 3000 })
  } catch (err) {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to save AI settings', life: 3000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="ai-settings-view">
    <div class="flex align-items-center justify-content-between mb-4">
      <div>
        <h2 class="text-2xl font-bold m-0">AI Engine Settings</h2>
        <p class="text-color-secondary mt-1 mb-0">
          Operational settings that control AI behaviour. Changes are persisted in the database
          and take effect immediately — no server restart required.
        </p>
      </div>
      <Button
        label="Save Changes"
        icon="pi pi-save"
        :loading="saving"
        :disabled="loading"
        @click="saveSettings"
      />
    </div>

    <div class="grid">
      <!-- Conversation settings -->
      <div class="col-12 lg:col-6">
        <Card>
          <template #title>Conversation Limits</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div class="field">
                <label class="font-medium block mb-1">Max Conversation History Messages</label>
                <p class="text-color-secondary text-sm mb-2">
                  How many previous messages to include per request. Higher values improve coherence
                  but increase token usage.
                </p>
                <InputNumber
                  v-model="settings.maxConversationHistoryMessages"
                  :min="1"
                  :max="200"
                  show-buttons
                  class="w-full"
                />
              </div>

              <Divider />

              <div class="field">
                <label class="font-medium block mb-1">Max Consecutive Failures</label>
                <p class="text-color-secondary text-sm mb-2">
                  Number of consecutive tool failures allowed before the AI stops retrying.
                </p>
                <InputNumber
                  v-model="settings.maxConsecutiveFailures"
                  :min="1"
                  :max="20"
                  show-buttons
                  class="w-full"
                />
              </div>

              <Divider />

              <div class="field">
                <label class="font-medium block mb-1">Default Max Output Tokens</label>
                <p class="text-color-secondary text-sm mb-2">
                  Token limit used when creating new AI models and as a fallback when a model's
                  configured limit is not found.
                </p>
                <InputNumber
                  v-model="settings.defaultMaxOutputTokens"
                  :min="1024"
                  :max="200000"
                  :step="1024"
                  show-buttons
                  class="w-full"
                />
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Iteration limits -->
      <div class="col-12 lg:col-6">
        <Card>
          <template #title>Iteration Limits</template>
          <template #content>
            <p class="text-color-secondary text-sm mb-3">
              Maximum AI round-trips (tool calls) per conversation, by complexity tier.
              <strong>0</strong> = use built-in tier defaults (Simple 15 / Medium 25 / Large 40 / Enterprise 60).
            </p>

            <div class="flex flex-column gap-4">
              <div class="field">
                <label class="font-medium block mb-1">
                  Global Override
                  <span class="text-color-secondary font-normal">(all tiers)</span>
                </label>
                <p class="text-color-secondary text-sm mb-2">
                  Emergency cap for all requests regardless of tier. Set to 0 to disable.
                </p>
                <InputNumber
                  v-model="settings.maxIterations"
                  :min="0"
                  :max="1000"
                  show-buttons
                  class="w-full"
                />
              </div>

              <Divider />

              <div class="grid">
                <div class="col-6 field">
                  <label class="font-medium block mb-1">Simple</label>
                  <InputNumber
                    v-model="settings.iterationLimits.simple"
                    :min="0"
                    :max="500"
                    show-buttons
                    class="w-full"
                  />
                </div>
                <div class="col-6 field">
                  <label class="font-medium block mb-1">Medium</label>
                  <InputNumber
                    v-model="settings.iterationLimits.medium"
                    :min="0"
                    :max="500"
                    show-buttons
                    class="w-full"
                  />
                </div>
                <div class="col-6 field">
                  <label class="font-medium block mb-1">Large</label>
                  <InputNumber
                    v-model="settings.iterationLimits.large"
                    :min="0"
                    :max="500"
                    show-buttons
                    class="w-full"
                  />
                </div>
                <div class="col-6 field">
                  <label class="font-medium block mb-1">Enterprise</label>
                  <InputNumber
                    v-model="settings.iterationLimits.enterprise"
                    :min="0"
                    :max="500"
                    show-buttons
                    class="w-full"
                  />
                </div>
                <div class="col-12 field">
                  <label class="font-medium block mb-1">Autonomous</label>
                  <small class="text-color-secondary block mb-1">Default: 500. 0 = use built-in default. Applies when the chat is in Autonomous mode.</small>
                  <InputNumber
                    v-model="settings.iterationLimits.autonomous"
                    :min="0"
                    :max="10000"
                    show-buttons
                    class="w-full"
                  />
                </div>
                <div class="col-12 field">
                  <label class="font-medium block mb-1">Planning</label>
                  <small class="text-color-secondary block mb-1">Default: 5. 0 = use built-in default. Max iterations for the autonomous planning phase (Phase 1).</small>
                  <InputNumber
                    v-model="settings.iterationLimits.planning"
                    :min="0"
                    :max="50"
                    show-buttons
                    class="w-full"
                  />
                </div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>
