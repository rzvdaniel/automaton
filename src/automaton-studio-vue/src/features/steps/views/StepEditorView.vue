<script setup lang="ts">
import { ref } from 'vue'

const stepName = ref('ValidateEmail')
const displayName = ref('Validate Email')
const language = ref('Python')
const aiPrompt = ref('')

const inputs = ref([
  { name: 'email', type: 'string', required: true }
])

const outputs = ref([
  { name: 'isValid', type: 'bool' },
  { name: 'errorMessage', type: 'string' }
])

const usedInFlows = ref([
  'Order Processing',
  'User Registration'
])

const code = ref(`import re
import dns.resolver

def validate(email):
    pattern = r'^[\\w\\.-]+@[\\w\\.-]+\\.\\w+$'
    if not re.match(pattern, email):
        return False
    
    # Check MX record
    domain = email.split('@')[1]
    try:
        dns.resolver.resolve(domain, 'MX')
    except:
        return False
    
    return True`)

</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-6">
      <div class="d-flex align-center">
        <v-btn icon="mdi-arrow-left" variant="text" class="mr-2" />
        <div>
          <h1 class="text-h5 font-weight-bold">{{ displayName }}</h1>
          <p class="text-body-2 text-medium-emphasis">{{ stepName }}</p>
        </div>
      </div>
      <div class="d-flex gap-2">
        <v-btn variant="outlined">Test</v-btn>
        <v-btn color="primary">Save</v-btn>
      </div>
    </div>

    <!-- Two-Panel Layout: Code | Configuration -->
    <v-row>
      <!-- Code Editor (Left) -->
      <v-col cols="12" md="8">
        <v-card>
          <v-card-title>Code</v-card-title>
          <v-divider />
          <v-card-text>
            <v-sheet color="surface-variant" class="pa-4">
              <pre class="text-body-2" style="font-family: 'JetBrains Mono', monospace"><code>{{ code }}</code></pre>
            </v-sheet>
          </v-card-text>
        </v-card>

        <!-- AI Assistant -->
        <v-card class="mt-4">
          <v-card-text class="d-flex align-center gap-2">
            <v-icon>mdi-robot</v-icon>
            <v-text-field
              v-model="aiPrompt"
              variant="outlined"
              placeholder="Add timeout handling"
              density="compact"
              append-inner-icon="mdi-send"
              hide-details
            />
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Configuration (Right) -->
      <v-col cols="12" md="4">
        <v-card>
          <v-card-title>Configuration</v-card-title>
          <v-divider />
          <v-card-text>
            <v-text-field
              v-model="stepName"
              label="Name"
              variant="outlined"
              class="mb-4"
            />
            <v-text-field
              v-model="displayName"
              label="Display Name"
              variant="outlined"
              class="mb-4"
            />
            <v-select
              v-model="language"
              :items="['Python', 'PowerShell', 'Batch']"
              label="Language"
              variant="outlined"
              class="mb-6"
            />

            <h3 class="text-subtitle-1 font-weight-bold mb-2">Inputs</h3>
            <v-list density="compact" class="mb-2">
              <v-list-item v-for="input in inputs" :key="input.name">
                <v-list-item-title>{{ input.name }} ({{ input.type }}) {{ input.required ? '*' : '' }}</v-list-item-title>
              </v-list-item>
            </v-list>
            <v-btn variant="outlined" size="small" block class="mb-6">+ Add Input</v-btn>

            <h3 class="text-subtitle-1 font-weight-bold mb-2">Outputs</h3>
            <v-list density="compact" class="mb-2">
              <v-list-item v-for="output in outputs" :key="output.name">
                <v-list-item-title>{{ output.name }} ({{ output.type }})</v-list-item-title>
              </v-list-item>
            </v-list>
            <v-btn variant="outlined" size="small" block class="mb-6">+ Add Output</v-btn>

            <h3 class="text-subtitle-1 font-weight-bold mb-2">Used in Flows</h3>
            <v-list density="compact">
              <v-list-item v-for="flow in usedInFlows" :key="flow" prepend-icon="mdi-flash">
                <v-list-item-title>{{ flow }}</v-list-item-title>
              </v-list-item>
            </v-list>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
