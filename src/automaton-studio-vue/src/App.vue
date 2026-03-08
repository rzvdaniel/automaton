<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { useProjectsStore } from '@/features/projects/stores/projectsStore'
import { usePlanLimit } from '@/shared/composables/usePlanLimit'
import { usePlanFeatures } from '@/shared/composables/usePlanFeatures'
import { useSubscriptionsEnabled } from '@/shared/composables/useSubscriptionsEnabled'
import AppToolbar from '@/shared/components/layout/AppToolbar.vue'
import AppSidebar from '@/shared/components/layout/AppSidebar.vue'
import Toast from 'primevue/toast'
import ConfirmDialog from 'primevue/confirmdialog'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'

// Initialize settings on app mount
useUserSettings()
const projectsStore = useProjectsStore()
const route = useRoute()
const router = useRouter()
const { visible: planLimitVisible, featureName, requiredPlan, install: installPlanLimit, dismiss } = usePlanLimit()
const { load: loadPlanFeatures } = usePlanFeatures()
const subscriptionsEnabled = useSubscriptionsEnabled()

// Check if current route uses auth layout (login, register, install, etc.)
const isAuthLayout = computed(() => route.meta.layout === 'auth')

// Check if current route uses no layout (full screen apps, etc.)
const isNoLayout = computed(() => route.meta.layout === 'none')

onMounted(async () => {
  installPlanLimit()
  // Only initialize projects store on authenticated pages (not auth/install pages)
  if (!isAuthLayout.value) {
    await projectsStore.initialize()
    await loadPlanFeatures()
  }
})

function goToPricing() {
  dismiss()
  router.push('/pricing')
}
</script>

<template>
  <!-- Global PrimeVue components -->
  <Toast />
  <ConfirmDialog />

  <!-- Plan limit upgrade dialog -->
  <Dialog
    v-model:visible="planLimitVisible"
    header="Upgrade Required"
    modal
    style="width: 400px"
    :closable="true"
  >
    <div class="flex flex-column gap-3">
      <div class="flex align-items-center gap-3">
        <i class="pi pi-lock text-4xl text-primary" />
        <div>
          <div class="font-semibold text-lg">{{ featureName }} requires a higher plan</div>
          <div class="text-color-secondary text-sm mt-1">
            This feature is available on the <strong>{{ requiredPlan }}</strong> plan and above.
          </div>
        </div>
      </div>
    </div>
    <template #footer>
      <Button label="Maybe later" text @click="dismiss" />
      <Button v-if="subscriptionsEnabled" label="See plans" icon="pi pi-arrow-right" icon-pos="right" @click="goToPricing" />
    </template>
  </Dialog>

  <!-- No layout: full-screen for published apps -->
  <template v-if="isNoLayout">
    <RouterView />
  </template>

  <!-- Auth layout: full-screen without sidebar/toolbar -->
  <div v-else-if="isAuthLayout" class="auth-layout">
    <RouterView />
  </div>

  <!-- Default layout: with sidebar and toolbar -->
  <div v-else class="app-shell">
    <!-- Sidebar / Navigation Rail -->
    <AppSidebar />

    <!-- Main Content Area -->
    <div class="app-content">
      <!-- Top Toolbar -->
      <AppToolbar />

      <!-- Page Content -->
      <main class="app-main">
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
/* Layout styles are in main.css for proper dark mode support */

/* Auth layout takes full screen */
.auth-layout {
  min-height: 100vh;
  width: 100%;
}
</style>
