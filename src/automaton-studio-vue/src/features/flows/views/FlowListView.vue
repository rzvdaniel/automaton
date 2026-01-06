<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import SelectButton from 'primevue/selectbutton'

const aiDescription = ref('')
const viewMode = ref('grid')
const viewOptions = ref([
  { icon: 'pi pi-th-large', value: 'grid' },
  { icon: 'pi pi-list', value: 'list' },
])

// Mock flows data
const flows = ref([
  {
    id: 1,
    name: 'Order Processing',
    steps: 5,
    lastStatus: 'success',
    lastRun: '2 min ago',
  },
  {
    id: 2,
    name: 'Email Campaign',
    steps: 3,
    lastStatus: 'success',
    lastRun: '5 min ago',
  },
  {
    id: 3,
    name: 'Data Sync',
    steps: 4,
    lastStatus: 'error',
    lastRun: '12 min ago',
  },
  {
    id: 4,
    name: 'Report Generation',
    steps: 6,
    lastStatus: 'success',
    lastRun: '1 hour ago',
  },
  {
    id: 5,
    name: 'Customer Onboarding',
    steps: 8,
    lastStatus: 'success',
    lastRun: '2 hours ago',
  },
  {
    id: 6,
    name: 'Inventory Update',
    steps: 3,
    lastStatus: 'success',
    lastRun: '3 hours ago',
  },
])

const getStatusIcon = (status: string) => {
  return status === 'success' ? 'pi pi-check-circle' : 'pi pi-times-circle'
}
</script>

<template>
  <div class="flows-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Flows</h1>
        <p class="text-color-secondary mt-1 mb-0">Create and manage your automation workflows</p>
      </div>
      <Button label="New Flow" icon="pi pi-plus" @click="$router.push('/flows/generate')" />
    </div>

    <!-- AI Generation Input -->
    <Card class="mb-5">
      <template #content>
        <div class="flex align-items-start gap-3">
          <div
            class="flex align-items-center justify-content-center border-round flex-shrink-0"
            style="
              width: 48px;
              height: 48px;
              background-color: var(--p-primary-100);
              color: var(--p-primary-600);
            "
          >
            <i class="pi pi-sparkles text-xl"></i>
          </div>
          <div class="flex-1">
            <div class="font-semibold mb-2">What would you like to automate?</div>
            <Textarea
              v-model="aiDescription"
              rows="3"
              placeholder="Describe your workflow in plain English. For example: 'When a new order comes in via webhook, validate the payment with Stripe, send confirmation email, and update inventory...'"
              class="w-full"
            />
            <div class="flex align-items-center justify-content-between mt-3">
              <div class="text-color-secondary text-sm">
                <span class="font-medium">Examples:</span>
                "Send Slack notification when GitHub PR merged" •
                "Backup database daily and upload to S3"
              </div>
              <Button label="Generate Flow" icon="pi pi-sparkles" :disabled="!aiDescription" />
            </div>
          </div>
        </div>
      </template>
    </Card>

    <!-- Flows List Header -->
    <div class="flex align-items-center justify-content-between mb-4">
      <div class="flex align-items-center gap-3">
        <span class="font-semibold">Your Flows</span>
        <Tag :value="`${flows.length} flows`" severity="secondary" />
      </div>
      <div class="flex align-items-center gap-3">
        <span class="p-input-icon-left">
          <i class="pi pi-search" />
          <InputText placeholder="Search flows..." />
        </span>
        <SelectButton v-model="viewMode" :options="viewOptions" option-value="value">
          <template #option="{ option }">
            <i :class="option.icon"></i>
          </template>
        </SelectButton>
      </div>
    </div>

    <!-- Flows Grid -->
    <div class="grid">
      <div v-for="flow in flows" :key="flow.id" class="col-12 md:col-6 lg:col-4">
        <Card class="h-full flow-card">
          <template #content>
            <div class="flex flex-column h-full">
              <div class="flex align-items-center justify-content-between mb-3">
                <div class="flex align-items-center gap-2">
                  <i class="pi pi-bolt" style="color: var(--p-primary-500)"></i>
                  <span class="font-semibold">{{ flow.name }}</span>
                </div>
                <Button icon="pi pi-ellipsis-v" text rounded severity="secondary" size="small" />
              </div>

              <div class="text-color-secondary text-sm mb-3">{{ flow.steps }} steps</div>

              <div class="flex align-items-center gap-2 mb-4">
                <i :class="getStatusIcon(flow.lastStatus)" :style="{ color: flow.lastStatus === 'success' ? 'var(--p-green-500)' : 'var(--p-red-500)' }"></i>
                <span class="text-sm">Last: {{ flow.lastRun }}</span>
              </div>

              <div class="flex gap-2 mt-auto">
                <Button label="Run" icon="pi pi-play" size="small" outlined class="flex-1" />
                <Button
                  label="Edit"
                  icon="pi pi-pencil"
                  size="small"
                  outlined
                  severity="secondary"
                  class="flex-1"
                />
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.flows-view {
  max-width: 1400px;
  margin: 0 auto;
}

.flow-card {
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.flow-card:hover {
  transform: translateY(-2px);
}
</style>
