<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Message from 'primevue/message'
import { organizationsService, type OrganizationAppSummary } from '../services/organizationsService'
import { actionSchedulesService, type ActionModule } from '@/features/apps/services/action-schedules.service'
import { appsService } from '@/features/apps/services/apps.service'
import AppActionContextBar from '@/features/apps/components/AppActionContextBar.vue'

const route = useRoute()
const router = useRouter()
const app = ref<OrganizationAppSummary | null>(null)
const actions = ref<ActionModule[]>([])
const loading = ref(true)
const error = ref('')
const actionsError = ref('')
const opening = ref(false)
const appId = String(route.params.appId)

onMounted(async () => {
  try {
    const assignedApps = await organizationsService.getMyAssignedApps()
    app.value = assignedApps.find(item => item.id === appId) ?? null
    if (!app.value) {
      error.value = 'This app is no longer assigned to your account.'
      return
    }
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : 'App access could not be verified.'
  } finally {
    loading.value = false
  }

  if (app.value) {
    try {
      actions.value = await actionSchedulesService.listActions(appId)
    } catch (reason) {
      actionsError.value = reason instanceof Error ? reason.message : 'Actions could not be loaded.'
    }
  }
})

async function openRuntime() {
  opening.value = true
  try {
    await appsService.createRuntimeLaunch(appId)
    window.location.assign(appsService.getRestrictedRuntimeLaunchUrl(appId))
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : 'The app could not be opened.'
  } finally {
    opening.value = false
  }
}
</script>

<template>
  <main class="assigned-app-page">
    <div class="page-header">
      <Button label="My Apps" icon="pi pi-arrow-left" text @click="router.push('/my-apps')" />
      <h1>{{ app?.name || 'Assigned app' }}</h1>
      <p v-if="app?.description || app?.summary" class="page-description">{{ app.description || app.summary }}</p>
    </div>

    <Message v-if="error" severity="warn" :closable="false">{{ error }}</Message>
    <div v-else-if="loading" class="loading-state"><i class="pi pi-spin pi-spinner" /> Checking app access…</div>
    <template v-else-if="app">
      <section class="use-actions">
        <div class="section-heading">
          <div><h2>Use this app</h2><p>Run its available Actions or open the deployed app.</p></div>
          <Button label="Open app" icon="pi pi-external-link" :loading="opening" @click="openRuntime" />
        </div>
        <Card v-if="actions.length" class="actions-card">
          <template #title>Actions</template>
          <template #content>
            <div v-for="action in actions" :key="action.key" class="action-row">
              <div class="action-copy"><strong>{{ action.name || action.key }}</strong><p v-if="action.description">{{ action.description }}</p></div>
              <AppActionContextBar :app-id="app.id" :action-key="action.key" :allow-version-management="false" />
            </div>
          </template>
        </Card>
        <Message v-else-if="actionsError" severity="warn" :closable="false">Actions could not be loaded. {{ actionsError }}</Message>
        <Message v-else severity="secondary" :closable="false">No Actions are available for this app.</Message>
      </section>
    </template>
  </main>
</template>

<style scoped>
.assigned-app-page { max-width: 72rem; margin: 0 auto; padding: 2rem; }
.page-header { margin-bottom: 1.5rem; }
.page-header h1 { margin: .75rem 0 .25rem; }
.page-description, .section-heading p, .action-copy p { color: var(--p-text-muted-color); margin: .25rem 0 0; }
.loading-state { display: flex; gap: .75rem; align-items: center; padding: 2rem 0; color: var(--p-text-muted-color); }
.section-heading { display: flex; align-items: center; justify-content: space-between; gap: 1rem; margin: 1.5rem 0 1rem; }
.section-heading h2 { margin: 0; }
.actions-card { background: var(--p-content-background); }
.action-row { padding: .75rem 0; border-bottom: 1px solid var(--p-content-border-color); }
.action-row:last-child { border-bottom: 0; }
.action-copy { margin-bottom: .5rem; }
@media (max-width: 640px) { .assigned-app-page { padding: 1rem; } .section-heading { align-items: flex-start; flex-direction: column; } }
</style>
