import { createRouter, createWebHistory } from 'vue-router'
import AppsView from '@/views/AppsView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'apps', component: AppsView },
    // Same paths as the former automaton.monster store, so old links can redirect host-only.
    { path: '/apps', redirect: '/' },
    {
      path: '/apps/:appId',
      name: 'app-marketplace-detail',
      component: () => import('@/views/AppMarketplaceDetailView.vue'),
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
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
  scrollBehavior(to) {
    if (to.hash) return { el: to.hash, behavior: 'smooth' }
    return { top: 0 }
  },
})

export default router
