import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'

// PrimeVue imports - MUST be in this order
import PrimeVue from 'primevue/config'
import Aura from '@primevue/themes/aura'
import 'primeicons/primeicons.css'
import 'primeflex/primeflex.css'

// Application styles - AFTER PrimeVue imports
import './assets/styles/main.css'

import App from './App.vue'

// Router setup
const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('./views/HomeView.vue'),
    },
  ],
})

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

// Router
app.use(router)

// Mount
app.mount('#app')
