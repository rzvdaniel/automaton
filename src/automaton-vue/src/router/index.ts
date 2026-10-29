import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'
import { applySeo, type SeoMeta } from '@/seo/useSeo'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      component: HomeView,
    },
    {
      path: '/apps',
      name: 'apps',
      component: () => import('@/views/AppsView.vue'),
    },
    {
      path: '/apps/:appId',
      name: 'app-marketplace-detail',
      component: () => import('@/views/AppMarketplaceDetailView.vue'),
    },
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
    {
      path: '/creators/:publicUsername/apps/:appId',
      name: 'creator-app-detail',
      component: () => import('@/views/CreatorAppDetailView.vue'),
    },
    {
      path: '/creators/:publicUsername',
      name: 'creator-store',
      component: () => import('@/views/CreatorStoreView.vue'),
    },
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
