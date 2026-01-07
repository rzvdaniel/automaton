<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Textarea from 'primevue/textarea'
import Skeleton from 'primevue/skeleton'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'

const description = ref('')
const isGenerating = ref(false)

// Mock selected step for details panel
const selectedStep = ref({
  name: 'Validate Payment',
  description: 'Validates Stripe payment using the payment intent ID',
  language: 'python',
  inputs: [{ name: 'paymentIntentId', type: 'string' }],
  outputs: [
    { name: 'isValid', type: 'bool' },
    { name: 'errorMessage', type: 'string' },
  ],
})

const aiRefinement = ref('')
</script>

<template>
  <div class="flow-generation-view">
    <!-- Page Header -->
    <div class="flex align-items-center gap-3 mb-5">
      <Button icon="pi pi-arrow-left" text rounded @click="$router.back()" />
      <div>
        <h1 class="text-2xl font-bold m-0">Generate Flow</h1>
        <p class="text-color-secondary mt-1 mb-0">Describe your automation and let AI build it</p>
      </div>
    </div>

    <div class="grid">
      <!-- Left Column - Description & Timeline -->
      <div class="col-12 lg:col-5">
        <!-- Description Input -->
        <Card class="mb-4">
          <template #title>
            <div class="flex align-items-center gap-2">
              <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
              <span>Describe Your Workflow</span>
            </div>
          </template>
          <template #content>
            <Textarea
              v-model="description"
              rows="5"
              placeholder="When a webhook receives a new customer order, validate payment with Stripe, update inventory in PostgreSQL, send confirmation email via SendGrid, and create shipping label with ShipStation."
              class="w-full mb-3"
            />
            <Button
              :label="isGenerating ? 'Generating...' : 'Generate Flow'"
              icon="pi pi-sparkles"
              :loading="isGenerating"
              :disabled="!description"
              class="w-full"
            />
          </template>
        </Card>

        <!-- Flow Timeline (Placeholder) -->
        <Card>
          <template #title>Flow Timeline</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <!-- Placeholder steps -->
              <div v-for="i in 4" :key="i" class="flex align-items-start gap-3">
                <div class="flex flex-column align-items-center">
                  <div
                    class="flex align-items-center justify-content-center border-round-lg"
                    style="
                      width: 40px;
                      height: 40px;
                      background-color: var(--p-surface-100);
                    "
                  >
                    <Skeleton shape="circle" size="24px" />
                  </div>
                  <div
                    v-if="i < 4"
                    style="width: 2px; height: 40px; background-color: var(--p-surface-200)"
                  ></div>
                </div>
                <div class="flex-1 pt-2">
                  <Skeleton width="60%" height="1rem" class="mb-2" />
                  <Skeleton width="80%" height="0.875rem" />
                </div>
              </div>

              <Button label="+ Add Step" text class="align-self-start" />
            </div>
          </template>
        </Card>
      </div>

      <!-- Right Column - Step Details & AI Assistant -->
      <div class="col-12 lg:col-7">
        <!-- Step Details -->
        <Card class="mb-4">
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>Step Details</span>
              <Tag value="Selected" severity="info" />
            </div>
          </template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div>
                <label class="text-color-secondary text-sm block mb-2">Step Name</label>
                <InputText :model-value="selectedStep.name" class="w-full" readonly />
              </div>

              <div>
                <label class="text-color-secondary text-sm block mb-2">Description</label>
                <Textarea :model-value="selectedStep.description" rows="2" class="w-full" readonly />
              </div>

              <div class="grid">
                <div class="col-6">
                  <label class="text-color-secondary text-sm block mb-2">Inputs</label>
                  <div class="flex flex-column gap-2">
                    <div
                      v-for="input in selectedStep.inputs"
                      :key="input.name"
                      class="p-2 surface-ground border-round"
                    >
                      <span class="font-medium">{{ input.name }}</span>
                      <span class="text-color-secondary ml-2">({{ input.type }})</span>
                    </div>
                  </div>
                </div>
                <div class="col-6">
                  <label class="text-color-secondary text-sm block mb-2">Outputs</label>
                  <div class="flex flex-column gap-2">
                    <div
                      v-for="output in selectedStep.outputs"
                      :key="output.name"
                      class="p-2 surface-ground border-round"
                    >
                      <span class="font-medium">{{ output.name }}</span>
                      <span class="text-color-secondary ml-2">({{ output.type }})</span>
                    </div>
                  </div>
                </div>
              </div>

              <div class="flex gap-2">
                <Button label="View Code" icon="pi pi-code" outlined size="small" />
                <Button label="Edit" icon="pi pi-pencil" outlined severity="secondary" size="small" />
                <Button label="Refine with AI" icon="pi pi-sparkles" outlined severity="secondary" size="small" />
              </div>
            </div>
          </template>
        </Card>

        <!-- AI Assistant -->
        <Card>
          <template #title>
            <div class="flex align-items-center gap-2">
              <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
              <span>AI Assistant</span>
            </div>
          </template>
          <template #content>
            <div class="flex gap-2">
              <InputText
                v-model="aiRefinement"
                placeholder="Add error handling for failed payments..."
                class="flex-1"
              />
              <Button icon="pi pi-send" :disabled="!aiRefinement" />
            </div>
            <div class="text-color-secondary text-sm mt-3">
              <p class="m-0 mb-2">Suggestions:</p>
              <ul class="m-0 pl-4">
                <li>"Add retry logic for API failures"</li>
                <li>"Include logging for each step"</li>
                <li>"Add notification on completion"</li>
              </ul>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <!-- Bottom Actions -->
    <div class="flex justify-content-end gap-3 mt-5">
      <Button label="Cancel" severity="secondary" outlined @click="$router.back()" />
      <Button label="Test Flow" icon="pi pi-play" outlined />
      <Button label="Save Flow" icon="pi pi-save" />
    </div>
  </div>
</template>

<style scoped>
.flow-generation-view {
  max-width: 1400px;
  margin: 0 auto;
}
</style>
