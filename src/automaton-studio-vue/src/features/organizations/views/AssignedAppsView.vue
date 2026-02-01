<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { organizationsService } from '../services/organizationsService'
import Card from 'primevue/card'
import Button from 'primevue/button'
import EmptyState from '@/shared/components/EmptyState.vue'

const router = useRouter()

// State
const assignedApps = ref<any[]>([])
const loading = ref(true)

// Load assigned apps on mount
onMounted(async () => {
  await loadAssignedApps()
})

// Load all apps assigned to current user across all organizations
async function loadAssignedApps() {
  try {
    loading.value = true
    // TODO: Implement endpoint to get all assigned apps for current user across orgs
    // For now, this is a placeholder
    assignedApps.value = []
  } catch (error) {
    // Error is handled by the service
  } finally {
    loading.value = false
  }
}

function openApp(app: any) {
  // Navigate to app or open in new tab
  window.open(app.publicUrl, '_blank')
}
</script>

<template>
  <div class="assigned-apps">
    <div class="page-header">
      <div>
        <h1>My Apps</h1>
        <p class="text-muted">Apps assigned to you across all organizations</p>
      </div>
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading your apps...</p>
    </div>

    <EmptyState
      v-else-if="assignedApps.length === 0"
      icon="pi-inbox"
      title="No apps assigned to you yet"
      description="Contact your organization admin to get access to apps"
    />

    <div v-else class="apps-grid">
      <Card
        v-for="app in assignedApps"
        :key="app.id"
        class="app-card"
      >
        <template #header>
          <div class="app-icon-container">
            <i class="pi pi-th-large app-icon"></i>
          </div>
        </template>
        <template #title>{{ app.name }}</template>
        <template #content>
          <p class="text-muted">{{ app.description || 'No description' }}</p>
        </template>
        <template #footer>
          <Button
            label="Open App"
            icon="pi pi-external-link"
            @click="openApp(app)"
          />
        </template>
      </Card>
    </div>
  </div>
</template>

<style scoped>
.assigned-apps {
  padding: 2rem;
}

.page-header {
  margin-bottom: 2rem;
}

.page-header h1 {
  margin: 0 0 0.5rem 0;
  font-size: 2rem;
  font-weight: 600;
}

.text-muted {
  color: var(--p-text-muted-color);
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 0;
  gap: 1rem;
}

.apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 1.5rem;
}

.app-card {
  cursor: pointer;
  transition: transform 0.2s;
}

.app-card:hover {
  transform: translateY(-4px);
}

.app-icon-container {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2rem;
  background: var(--p-surface-100);
}

.app-icon {
  font-size: 3rem;
  color: var(--p-primary-color);
}
</style>
