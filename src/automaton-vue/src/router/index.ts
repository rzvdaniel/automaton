import { createRouter, createWebHistory, type RouteLocationNormalized } from 'vue-router'
import HomeView from '@/views/HomeView.vue'
import { applySeo, type SeoMeta } from '@/seo/useSeo'
import { publicAppConfig } from '@/config/publicAppConfig'

// The public app store moved to its own site with the same paths (it maps /apps to /).
function redirectToStore(to: RouteLocationNormalized) {
  window.location.replace(new URL(to.fullPath, publicAppConfig.storeUrl).toString())
  return false
}

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      component: HomeView,
    },
    { path: '/apps/:rest(.*)*', name: 'apps', component: HomeView, beforeEnter: redirectToStore },
    {
      path: '/cli',
      name: 'cli',
      component: () => import('@/views/CliView.vue'),
    },
    {
      path: '/pricing',
      name: 'pricing',
      component: () => import('@/views/PricingView.vue'),
    },
    { path: '/creators/:rest(.*)*', name: 'creators', component: HomeView, beforeEnter: redirectToStore },
  ],
  scrollBehavior(to) {
    if (to.hash) {
      return { el: to.hash, behavior: 'smooth' }
    }
    return { top: 0 }
  },
})

router.afterEach((to) => {
  const seo = to.meta?.seo as Partial<SeoMeta> | undefined

  applySeo({
    title: seo?.title ?? 'Open-source App Deployment on Your Server',
    description:
      seo?.description ??
      'Automaton is an open-source platform for deploying web apps, APIs and Actions on your own server. Install your Studio, connect a Runner, and deploy with your AI tools.',
    path: to.path,
    noindex: seo?.noindex,
  })
})

export default router
