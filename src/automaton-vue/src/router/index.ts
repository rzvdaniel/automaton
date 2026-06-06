import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'
import { applySeo } from '@/seo/useSeo'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      component: HomeView,
      meta: {
        seo: {
          title: 'Build and Deploy AI-Generated Apps',
          description:
            'Build frontend apps, backend apps and automation workflows with AI. Deploy frontend apps to Automaton and run backend apps on runners you control.',
        },
      },
    },
    {
      path: '/apps',
      name: 'apps',
      component: () => import('@/views/AppsView.vue'),
      meta: {
        seo: {
          title: 'Apps Gallery',
          description:
            'Explore AI-generated apps built and deployed with Automaton Monster. Browse demos and get inspiration for your next app.',
        },
      },
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
      meta: {
        seo: {
          title: 'Docs',
          description:
            'Automaton Monster documentation. Learn how to build, deploy and publish frontend and backend apps using the Automaton CLI.',
        },
      },
    },
    {
      path: '/docs/getting-started',
      name: 'docs-getting-started',
      component: () => import('@/views/DocsView.vue'),
      meta: {
        seo: {
          title: 'Getting Started',
          description:
            'Get started with Automaton Monster in 5 minutes. Install the CLI, create your first app and deploy it.',
        },
      },
    },
    {
      path: '/docs/cli-ai-tools',
      name: 'docs-cli-ai-tools',
      component: () => import('@/views/DocsView.vue'),
      meta: {
        seo: {
          title: 'Build with AI Tools',
          description:
            'Use Automaton CLI with AI coding tools like Copilot, Claude or Cursor to build and deploy apps faster.',
        },
      },
    },
    {
      path: '/docs/build-frontend-app',
      name: 'docs-build-frontend-app',
      component: () => import('@/views/DocsView.vue'),
      meta: {
        seo: {
          title: 'Build a Frontend App',
          description:
            'Build and host a static frontend app with AI using Automaton. Supports Vite, React, Vue, and vanilla JavaScript.',
        },
      },
    },
    {
      path: '/docs/build-backend-app',
      name: 'docs-build-backend-app',
      component: () => import('@/views/DocsView.vue'),
      meta: {
        seo: {
          title: 'Build a Backend App',
          description:
            'Build a Python backend app with Automaton. Deploy controllers to a self-hosted Runner and call them from your frontend.',
        },
      },
    },
    {
      path: '/docs/deploy-runner',
      name: 'docs-deploy-runner',
      component: () => import('@/views/DocsView.vue'),
      meta: {
        seo: {
          title: 'Deploy a Runner',
          description:
            'Set up a self-hosted Automaton Runner to execute backend apps locally or on your own infrastructure using Docker.',
        },
      },
    },
    {
      path: '/docs/deploy',
      name: 'docs-deploy',
      component: () => import('@/views/DocsView.vue'),
      meta: {
        seo: {
          title: 'Deploy an App',
          description:
            'Deploy your Automaton app with the CLI. Ship frontend and backend apps in seconds using automaton deploy.',
        },
      },
    },
    {
      path: '/docs/publish',
      name: 'docs-publish',
      component: () => import('@/views/DocsView.vue'),
      meta: {
        seo: {
          title: 'Publish an App',
          description:
            'Make your Automaton app public. Publish a deployed app to share it with anyone using automaton publish.',
        },
      },
    },
    {
      path: '/docs/automaton-cli',
      name: 'docs-automaton-cli',
      component: () => import('@/views/DocsView.vue'),
      meta: {
        seo: {
          title: 'Automaton CLI Reference',
          description:
            'Full CLI reference for the Automaton CLI. All commands, flags and options for building, deploying and publishing apps.',
        },
      },
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
      meta: {
        seo: {
          title: 'Automaton CLI',
          description:
            'Install the Automaton CLI to create, deploy and publish frontend and backend apps from your terminal.',
        },
      },
    },
    {
      path: '/pricing',
      name: 'pricing',
      component: () => import('@/views/PricingView.vue'),
      meta: {
        seo: {
          title: 'Pricing',
          description:
            'Simple Automaton Monster pricing for building, deploying and publishing AI-generated apps.',
        },
      },
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

router.afterEach((to) => {
  const seo = to.meta?.seo as
    | { title?: string; description?: string; noindex?: boolean }
    | undefined

  applySeo({
    title: seo?.title ?? 'Build and Deploy AI-Generated Apps',
    description:
      seo?.description ??
      'Automaton Monster lets you build frontend apps, backend apps and automation workflows with AI, then deploy them using runners you control.',
    path: to.path,
    noindex: seo?.noindex,
  })
})

export default router
