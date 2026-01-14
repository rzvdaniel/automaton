import { createApp } from 'vue'
import { createPinia } from 'pinia'

// PrimeVue imports - MUST be in this order
import PrimeVue from 'primevue/config'
import Aura from '@primevue/themes/aura'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import Tooltip from 'primevue/tooltip'
import 'primeicons/primeicons.css'
import 'primeflex/primeflex.css'

// Application styles - AFTER PrimeVue imports
import './assets/styles/main.css'

import App from './App.vue'
import router from './router'

// Create app
const app = createApp(App)

// Pinia store
const pinia = createPinia()
app.use(pinia)

// PrimeVue with Aura theme (default)
app.use(PrimeVue, {
  theme: {
    preset: Aura,
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

// Mount
app.mount('#app')
