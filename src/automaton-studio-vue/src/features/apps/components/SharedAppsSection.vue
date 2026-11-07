<script setup lang="ts">
import { ref } from 'vue'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Message from 'primevue/message'
import type { OrganizationAppSummary } from '@/features/organizations/services/organizationsService'
import { appsService } from '../services/apps.service'

defineProps<{ apps: OrganizationAppSummary[] }>()

const openingId = ref<string | null>(null)
const error = ref('')

async function open(app: OrganizationAppSummary) {
  openingId.value = app.id
  error.value = ''
  try {
    const { runtimeUrl } = await appsService.createRuntimeLaunch(app.id)
    window.location.assign(runtimeUrl)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : 'The app could not be opened.'
  } finally {
    openingId.value = null
  }
}
</script>

<template>
  <section v-if="apps.length" class="mb-4" aria-label="Shared with you">
    <h2 class="text-xl font-semibold mt-0 mb-3">Shared with you</h2>
    <Message v-if="error" severity="warn" :closable="false" class="mb-3">{{ error }}</Message>
    <div class="grid">
      <div v-for="app in apps" :key="app.id" class="col-12 md:col-6 lg:col-4">
        <Card class="h-full">
          <template #title>{{ app.name }}</template>
          <template #content>
            <p v-if="app.summary || app.description" class="text-color-secondary mt-0">{{ app.summary || app.description }}</p>
            <Button label="Open app" icon="pi pi-external-link" :loading="openingId === app.id" @click="open(app)" />
          </template>
        </Card>
      </div>
    </div>
  </section>
</template>
