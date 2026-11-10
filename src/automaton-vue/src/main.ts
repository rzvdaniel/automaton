import { ViteSSG } from 'vite-ssg'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import Aura from '@primevue/themes/aura'
import 'primeicons/primeicons.css'
import App from './App.vue'
import { routes, scrollBehavior, seoFor, seoTags } from './router'
import { applySeo } from './seo/useSeo'
import { analytics } from './services/analytics'
import './assets/main.css'
import './assets/site.css'

// The marketing routes are prerendered at build time (vite-ssg) and hydrated in the browser.
export const createApp = ViteSSG(App, { routes, scrollBehavior }, ({ app, router, isClient, head }) => {
  if (isClient) analytics.init()

  app.use(createPinia())
  app.use(PrimeVue, {
    theme: {
      preset: Aura,
      options: {
        darkModeSelector: '.dark',
      },
    },
  })

  router.afterEach((to) => {
    const seo = seoFor(to)
    if (isClient) applySeo(seo)
    else head?.push(seoTags(seo))
  })
})
