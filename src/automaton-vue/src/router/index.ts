import type { RouteLocationNormalized, RouteRecordRaw } from 'vue-router'
import HomeView from '@/views/HomeView.vue'
import { seoTags, type SeoMeta } from '@/seo/useSeo'
import { publicAppConfig } from '@/config/publicAppConfig'

// The public app store moved to its own site with the same paths (it maps /apps to /).
function redirectToStore(to: RouteLocationNormalized) {
  if (typeof window === 'undefined') return false
  window.location.replace(new URL(to.fullPath, publicAppConfig.storeUrl).toString())
  return false
}

export const routes: RouteRecordRaw[] = [
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
      meta: { seo: { title: 'Catalog', description: 'Browse and search the apps you can install on your own server, each with screenshots, the permissions it asks for and its licence.' } },
    },
    { path: '/catalog/:id', name: 'catalog-app', component: () => import('@/views/CatalogAppView.vue') },
    {
      path: '/spec',
      name: 'spec',
      component: () => import('@/views/SpecView.vue'),
      meta: { seo: { title: 'App and catalog spec', description: 'An open, versioned format for self-hosted apps and catalogs: automaton.json and catalog.json, with JSON Schemas under Apache-2.0 and the text under CC BY 4.0.' } },
    },
    {
      path: '/how-catalogs-work',
      name: 'how-catalogs-work',
      component: () => import('@/views/HowCatalogsWorkView.vue'),
      meta: { seo: { title: 'How catalogs work', description: 'Apps as source code in GitHub repositories, installed on your own server when you need them. Anyone can publish a catalog: an app needs only an automaton.json.' } },
    },
    {
      path: '/how-setup-works',
      name: 'how-setup-works',
      component: () => import('@/views/SetupView.vue'),
      meta: { seo: { title: 'How setup works', description: 'From a rented server to your first app, mostly done by your AI agent, with measured times for each step.' } },
    },
    {
      path: '/roadmap',
      name: 'roadmap',
      component: () => import('@/views/RoadmapView.vue'),
      meta: { seo: { title: 'Roadmap', description: 'What Automaton does now, next and later, in plain words, with a link to each GitHub issue.' } },
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
]

export function scrollBehavior(to: RouteLocationNormalized) {
  if (to.hash) {
    return { el: to.hash, behavior: 'smooth' as const }
  }
  return { top: 0 }
}

export function seoFor(to: RouteLocationNormalized): SeoMeta {
  const seo = to.meta?.seo as Partial<SeoMeta> | undefined
  return {
    title: seo?.title ?? 'A small cloud of your own',
    description:
      seo?.description ??
      'Deploy apps, APIs and jobs on servers you own, with backups, monitoring and logs built in. No autoscaling or multi-region failover: built for small and medium workloads.',
    path: to.path,
    noindex: seo?.noindex,
  }
}

export { seoTags }
