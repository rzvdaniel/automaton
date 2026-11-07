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
      path: '/product',
      name: 'product',
      component: () => import('@/views/ProductView.vue'),
      meta: { seo: { title: 'Product', description: 'The parts of Automaton: the CLI your AI agent drives, Studio, the app store and catalogs, Runners, and the security and recovery built in.' } },
    },
    {
      path: '/use-cases',
      name: 'use-cases',
      component: () => import('@/views/UseCasesView.vue'),
      meta: { seo: { title: 'Use cases', description: 'How people use one Automaton server: personal tools, team tools, a company app store, in-house data, client sharing and scheduled jobs.' } },
    },
    {
      path: '/catalog',
      name: 'catalog',
      component: () => import('@/views/CatalogView.vue'),
      meta: { seo: { title: 'Catalog', description: 'Apps as source code in GitHub repositories, installed on your own server when you need them. Anyone can publish a catalog: an app needs only an automaton.json.' } },
    },
    {
      path: '/how-setup-works',
      name: 'how-setup-works',
      component: () => import('@/views/SetupView.vue'),
      meta: { seo: { title: 'How setup works', description: 'From a rented server to your first app, mostly done by your AI agent, with measured times for each step.' } },
    },
    {
      path: '/get-started',
      name: 'get-started',
      component: () => import('@/views/GetStartedView.vue'),
      meta: { seo: { title: 'Get started', description: 'Let your AI agent install Automaton, follow the guide yourself, or join a server someone already runs.' } },
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
    { path: '/creators/:rest(.*)*', name: 'creators', component: HomeView, beforeEnter: redirectToStore },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/views/NotFoundView.vue'),
      meta: { seo: { title: 'Page not found', noindex: true } },
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
    title: seo?.title ?? 'The home for every app your AI builds',
    description:
      seo?.description ??
      'Automaton is an open-source, self-hosted platform for all the apps your AI builds: sign-in, sharing, an app store, catalogs, secrets, backups and safe updates on servers you own.',
    path: to.path,
    noindex: seo?.noindex,
  })
})

export default router
