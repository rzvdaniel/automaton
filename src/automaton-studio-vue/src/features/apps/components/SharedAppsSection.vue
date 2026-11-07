<script setup lang="ts">
import { ref } from 'vue'
import Message from 'primevue/message'
import type { OrganizationAppSummary } from '@/features/organizations/services/organizationsService'
import { appsService } from '../services/apps.service'
import AppRow from './AppRow.vue'

defineProps<{ apps: OrganizationAppSummary[] }>()

const openingId = ref<string | null>(null)
const error = ref('')

async function open(app: OrganizationAppSummary) {
  openingId.value = app.id
  error.value = ''
  try {
    await appsService.createRuntimeLaunch(app.id)
    window.location.assign(appsService.getRestrictedRuntimeLaunchUrl(app.id))
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : 'The app could not be opened.'
  } finally {
    openingId.value = null
  }
}
</script>

<template>
  <section v-if="apps.length" class="shared" aria-label="Shared with you">
    <h2 class="shared__title">Shared with you</h2>
    <Message v-if="error" severity="warn" :closable="false" class="mb-3">{{ error }}</Message>
    <div class="shared__rows">
      <AppRow
        v-for="app in apps"
        :id="app.id"
        :key="app.id"
        :name="app.name"
        :line="app.summary || app.description || 'Open app'"
        :image-url="app.thumbnailUrl"
        :busy="openingId === app.id"
        @click="open(app)"
      />
    </div>
  </section>
</template>

<style scoped>
.shared { margin-top: 24px; }
.shared__title { margin: 0 0 12px; font-family: var(--display); font-size: 20px; font-weight: 600; }
.shared__rows { display: flex; flex-direction: column; gap: 8px; }
</style>
