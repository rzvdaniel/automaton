import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'

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
      path: '/docs',
      name: 'docs',
      component: () => import('@/views/DocsView.vue'),
    },
    {
      path: '/docs/:slug',
      name: 'docs-section',
      component: () => import('@/views/DocsView.vue'),
    },
    {
      path: '/cli',
      name: 'cli',
      component: () => import('@/views/CliView.vue'),
    },
    {
      path: '/:publicUsername/apps/:appId',
      name: 'creator-app-detail',
      component: () => import('@/views/CreatorAppDetailView.vue'),
    },
    {
      path: '/:publicUsername',
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

export default router
