/**
 * Admin feature routes
 * Routes for system administrator management features.
 * Uses AdminLayout as a wrapper with sidebar sub-navigation.
 * To add a new admin section, add a child route here and an entry in AdminLayout's navItems.
 */
import AdminLayout from './views/AdminLayout.vue'
import AdminProvidersView from './views/AdminProvidersView.vue'
import AdminModelsView from './views/AdminModelsView.vue'
import AdminAiKeysView from './views/AdminAiKeysView.vue'
import AdminLogsView from './views/AdminLogsView.vue'
import AdminSubscriptionPlansView from './views/AdminSubscriptionPlansView.vue'
import type { RouteRecordRaw } from 'vue-router'

export const adminRoutes: RouteRecordRaw[] = [
  {
    path: '/admin',
    component: AdminLayout,
    meta: {
      requiresRole: ['Admin', 'SysAdmin'],
    },
    children: [
      {
        path: '',
        redirect: '/admin/ai-providers',
      },
      {
        path: 'ai-providers',
        name: 'admin-providers',
        component: AdminProvidersView,
        meta: {
          title: 'AI Providers',
        },
      },
      {
        path: 'ai-providers/:providerId/models',
        name: 'admin-models',
        component: AdminModelsView,
        meta: {
          title: 'AI Models',
        },
      },
      {
        path: 'ai-keys',
        name: 'admin-ai-keys',
        component: AdminAiKeysView,
        meta: {
          title: 'API Keys',
        },
      },
      {
        path: 'logs',
        name: 'admin-logs',
        component: AdminLogsView,
        meta: {
          title: 'Server Logs',
        },
      },
      {
        path: 'subscription-plans',
        name: 'admin-subscription-plans',
        component: AdminSubscriptionPlansView,
        meta: {
          title: 'Subscription Plans',
        },
      },
    ],
  },
]
