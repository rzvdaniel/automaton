<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Dropdown from 'primevue/dropdown'
import Skeleton from 'primevue/skeleton'
import Tag from 'primevue/tag'

const stepName = ref('ValidateEmail')
const displayName = ref('Validate Email')
const description = ref('Validates email addresses using regex and DNS lookup')
const language = ref('python')

const languageOptions = ref([
  { label: 'Python', value: 'python' },
  { label: 'PowerShell', value: 'powershell' },
  { label: 'Batch', value: 'batch' },
])

const inputs = ref([{ name: 'email', type: 'string' }])
const outputs = ref([
  { name: 'isValid', type: 'bool' },
  { name: 'errorMessage', type: 'string' },
])

const usedInFlows = ref(['Order Processing', 'User Registration', 'Newsletter Signup'])

const aiPrompt = ref('')
</script>

<template>
  <div class="step-editor-view">
    <!-- Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div class="flex align-items-center gap-3">
        <Button icon="pi pi-arrow-left" text rounded @click="$router.push('/steps')" />
        <div>
          <h1 class="text-2xl font-bold m-0">{{ stepName }}</h1>
          <p class="text-color-secondary mt-1 mb-0">Edit custom step</p>
        </div>
      </div>
      <div class="flex align-items-center gap-2">
        <Button label="Test" icon="pi pi-play" outlined severity="secondary" />
        <Button label="Save" icon="pi pi-save" />
      </div>
    </div>

    <div class="grid">
      <!-- Code Editor Panel -->
      <div class="col-12 lg:col-7">
        <Card class="h-full">
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>Code</span>
              <Tag :value="language" severity="info" />
            </div>
          </template>
          <template #content>
            <div class="code-editor-placeholder">
              <Skeleton width="100%" height="18px" class="mb-2" />
              <Skeleton width="60%" height="18px" class="mb-2" />
              <Skeleton width="80%" height="18px" class="mb-2" />
              <Skeleton width="40%" height="18px" class="mb-2" />
              <Skeleton width="90%" height="18px" class="mb-2" />
              <Skeleton width="70%" height="18px" class="mb-2" />
              <Skeleton width="85%" height="18px" class="mb-2" />
              <Skeleton width="50%" height="18px" class="mb-2" />
              <Skeleton width="75%" height="18px" class="mb-2" />
              <Skeleton width="60%" height="18px" class="mb-2" />
              <Skeleton width="80%" height="18px" class="mb-2" />
              <Skeleton width="45%" height="18px" class="mb-2" />
              <div class="text-center text-color-secondary mt-4">
                <i class="pi pi-code text-4xl mb-2"></i>
                <p>Monaco Editor will be integrated here</p>
              </div>
            </div>

            <!-- AI Assistant inline -->
            <div class="flex align-items-center gap-2 mt-4 p-3 surface-ground border-round">
              <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
              <InputText
                v-model="aiPrompt"
                placeholder="Add timeout handling..."
                class="flex-1"
              />
              <Button label="Go" size="small" :disabled="!aiPrompt" />
            </div>
          </template>
        </Card>
      </div>

      <!-- Configuration Panel -->
      <div class="col-12 lg:col-5">
        <Card class="mb-4">
          <template #title>Configuration</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div class="flex flex-column gap-2">
                <label for="stepName" class="font-medium">Name</label>
                <InputText id="stepName" v-model="stepName" class="w-full" />
              </div>

              <div class="flex flex-column gap-2">
                <label for="displayName" class="font-medium">Display Name</label>
                <InputText id="displayName" v-model="displayName" class="w-full" />
              </div>

              <div class="flex flex-column gap-2">
                <label for="language" class="font-medium">Language</label>
                <Dropdown
                  id="language"
                  v-model="language"
                  :options="languageOptions"
                  option-label="label"
                  option-value="value"
                  class="w-full"
                />
              </div>

              <div class="flex flex-column gap-2">
                <label for="description" class="font-medium">Description</label>
                <Textarea id="description" v-model="description" rows="3" class="w-full" />
              </div>
            </div>
          </template>
        </Card>

        <Card class="mb-4">
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>Inputs</span>
              <Button icon="pi pi-plus" text rounded size="small" />
            </div>
          </template>
          <template #content>
            <div class="flex flex-column gap-2">
              <div
                v-for="input in inputs"
                :key="input.name"
                class="flex align-items-center justify-content-between p-2 surface-ground border-round"
              >
                <div>
                  <span class="font-medium">{{ input.name }}</span>
                  <span class="text-color-secondary ml-2">({{ input.type }})</span>
                </div>
                <div class="flex gap-1">
                  <Button icon="pi pi-pencil" text rounded size="small" severity="secondary" />
                  <Button icon="pi pi-trash" text rounded size="small" severity="danger" />
                </div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="mb-4">
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>Outputs</span>
              <Button icon="pi pi-plus" text rounded size="small" />
            </div>
          </template>
          <template #content>
            <div class="flex flex-column gap-2">
              <div
                v-for="output in outputs"
                :key="output.name"
                class="flex align-items-center justify-content-between p-2 surface-ground border-round"
              >
                <div>
                  <span class="font-medium">{{ output.name }}</span>
                  <span class="text-color-secondary ml-2">({{ output.type }})</span>
                </div>
                <div class="flex gap-1">
                  <Button icon="pi pi-pencil" text rounded size="small" severity="secondary" />
                  <Button icon="pi pi-trash" text rounded size="small" severity="danger" />
                </div>
              </div>
            </div>
          </template>
        </Card>

        <Card>
          <template #title>Used in Flows</template>
          <template #content>
            <div class="flex flex-column gap-2">
              <div
                v-for="flow in usedInFlows"
                :key="flow"
                class="flex align-items-center gap-2 p-2 surface-ground border-round cursor-pointer hover:surface-hover"
              >
                <i class="pi pi-bolt text-color-secondary"></i>
                <span>{{ flow }}</span>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.step-editor-view {
  max-width: 1400px;
  margin: 0 auto;
}

.code-editor-placeholder {
  background-color: var(--p-surface-50);
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  padding: 1rem;
  font-family: 'JetBrains Mono', monospace;
  min-height: 400px;
}

:global(.app-dark) .code-editor-placeholder {
  background-color: var(--p-surface-900);
  border-color: var(--p-surface-700);
}
</style>
