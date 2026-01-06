<script setup lang="ts">
import { ref } from 'vue'

const description = ref('')
const isGenerating = ref(false)
const generatedFlow = ref<any>(null)

async function generateFlow() {
  if (!description.value.trim()) return

  isGenerating.value = true
  // TODO: Call API to generate flow
  // Simulating API call
  await new Promise(resolve => setTimeout(resolve, 2000))

  generatedFlow.value = {
    name: 'Generated Flow',
    description: description.value,
    steps: [
      { id: '1', name: 'Webhook Trigger', type: 'trigger', status: 'ready' },
      { id: '2', name: 'Validate Payment', type: 'action', status: 'ready' },
      { id: '3', name: 'Send Confirmation', type: 'action', status: 'ready' },
    ],
  }
  isGenerating.value = false
}
</script>

<template>
  <div>
    <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/flows" class="mb-4">
      Back to Flows
    </v-btn>

    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h5 font-weight-bold">Generate Flow with AI</h1>
        <p class="text-body-2 text-medium-emphasis">Describe your automation in natural language</p>
      </div>
    </div>

    <!-- Flow Description Input -->
    <v-card v-if="!generatedFlow" class="mb-6">
      <v-card-text class="py-8">
        <div class="text-center mb-6">
          <v-avatar color="secondary" size="64" class="mb-4">
            <v-icon icon="mdi-robot" size="32" />
          </v-avatar>
          <h2 class="text-h6 font-weight-medium">What would you like to automate?</h2>
        </div>

        <v-textarea
          v-model="description"
          variant="outlined"
          placeholder="Describe your workflow in detail...

Example: When a new order comes in via webhook, validate the payment with Stripe API, check inventory levels in our PostgreSQL database, send a confirmation email to the customer via SendGrid, and create a shipping label with ShipStation."
          rows="6"
          class="mb-4"
          :disabled="isGenerating"
        />

        <div class="text-center">
          <v-btn
            color="secondary"
            size="large"
            prepend-icon="mdi-sparkles"
            :loading="isGenerating"
            :disabled="!description.trim()"
            @click="generateFlow"
          >
            Generate Flow
          </v-btn>
        </div>

        <v-divider class="my-6" />

        <div class="text-center">
          <p class="text-caption text-medium-emphasis mb-2">Example prompts:</p>
          <v-chip-group>
            <v-chip
              size="small"
              variant="outlined"
              @click="description = 'Send Slack notification when GitHub PR is merged'"
            >
              Slack + GitHub
            </v-chip>
            <v-chip
              size="small"
              variant="outlined"
              @click="description = 'Backup database daily and upload to S3'"
            >
              Database Backup
            </v-chip>
            <v-chip
              size="small"
              variant="outlined"
              @click="description = 'Process new orders from webhook, validate payment, send email confirmation'"
            >
              Order Processing
            </v-chip>
          </v-chip-group>
        </div>
      </v-card-text>
    </v-card>

    <!-- Generated Flow Preview -->
    <template v-if="generatedFlow">
      <v-row>
        <v-col cols="12" md="7">
          <v-card>
            <v-card-title class="d-flex align-center">
              <v-icon icon="mdi-sitemap" class="mr-2" />
              Flow Timeline
              <v-spacer />
              <v-btn variant="text" size="small" prepend-icon="mdi-plus">
                Add Step
              </v-btn>
            </v-card-title>
            <v-divider />
            <v-card-text>
              <v-timeline side="end" density="compact">
                <v-timeline-item
                  v-for="(step, index) in generatedFlow.steps"
                  :key="step.id"
                  :dot-color="index === 0 ? 'primary' : 'success'"
                  size="small"
                >
                  <v-card variant="outlined">
                    <v-card-text class="py-2">
                      <div class="d-flex align-center justify-space-between">
                        <div>
                          <span class="text-caption text-medium-emphasis">Step {{ index + 1 }}</span>
                          <h4 class="text-body-1 font-weight-medium">{{ step.name }}</h4>
                        </div>
                        <v-chip size="x-small" color="success" variant="tonal">
                          {{ step.status }}
                        </v-chip>
                      </div>
                    </v-card-text>
                  </v-card>
                </v-timeline-item>
              </v-timeline>
            </v-card-text>
          </v-card>
        </v-col>

        <v-col cols="12" md="5">
          <!-- Step Details Panel -->
          <v-card class="mb-4">
            <v-card-title>Step Details</v-card-title>
            <v-divider />
            <v-card-text>
              <p class="text-body-2 text-medium-emphasis">
                Click on a step in the timeline to view and edit its details.
              </p>
            </v-card-text>
          </v-card>

          <!-- AI Assistant Panel -->
          <v-card>
            <v-card-title class="d-flex align-center">
              <v-icon icon="mdi-robot" color="secondary" class="mr-2" />
              AI Assistant
            </v-card-title>
            <v-divider />
            <v-card-text>
              <v-text-field
                variant="outlined"
                density="compact"
                placeholder="Refine your flow... e.g., 'Add error handling for failed payments'"
                append-inner-icon="mdi-send"
                hide-details
              />
            </v-card-text>
          </v-card>

          <!-- Action Buttons -->
          <div class="d-flex gap-2 mt-4">
            <v-btn color="primary" block prepend-icon="mdi-content-save">
              Save Flow
            </v-btn>
            <v-btn variant="outlined" block prepend-icon="mdi-play">
              Test
            </v-btn>
          </div>
        </v-col>
      </v-row>
    </template>
  </div>
</template>
