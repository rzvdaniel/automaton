import { createApp } from 'vue'
import { createPinia } from 'pinia'

// PrimeVue imports - MUST be in this order
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import Tooltip from 'primevue/tooltip'
import 'primeicons/primeicons.css'
import 'primeflex/primeflex.css'

// Application styles - AFTER PrimeVue imports
import './assets/styles/tokens.css'
import './assets/styles/main.css'
import { StudioPreset } from './assets/styles/studio-preset'

import App from './App.vue'
import router from './router'
import { setUnauthorizedCallback } from '@/shared/services/api/axios-instance'
import { analytics } from '@/shared/services/analytics/analytics'
import { useAuthStore } from '@/features/auth/stores/auth.store'

// Create app
const app = createApp(App)

analytics.init()

// Pinia store
const pinia = createPinia()
app.use(pinia)

// Setup unauthorized callback for axios interceptor
// This prevents full page reloads when tokens expire
setUnauthorizedCallback(() => {
  const authStore = useAuthStore()
  void authStore.logout().finally(() => {
    void router.push({ path: '/login', query: { session: 'expired' } })
  })
})

// PrimeVue with the Aura preset mapped to the Studio design tokens
app.use(PrimeVue, {
  theme: {
    preset: StudioPreset,
    options: {
      darkModeSelector: '.app-dark',
    },
  },
})

// PrimeVue services
app.use(ToastService)
app.use(ConfirmationService)

// PrimeVue directives
app.directive('tooltip', Tooltip)

// Router
app.use(router)

// Wait for router to be ready before mounting to prevent race conditions
// This ensures route.meta is available when App.vue renders
router.isReady().then(() => {
  app.mount('#app')
})
