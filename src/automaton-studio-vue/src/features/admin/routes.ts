/**
 * Admin feature routes
 * Routes for system administrator management features.
 * Uses AdminLayout as a wrapper with sidebar sub-navigation.
 * To add a new admin section, add a child route here and an entry in AdminLayout's navItems.
 */
import AdminLayout from './views/AdminLayout.vue'
import AdminProvidersView from './views/AdminProvidersView.vue'
import AdminProviderDetailView from './views/AdminProviderDetailView.vue'
import AdminModelsView from './views/AdminModelsView.vue'
import AdminModelDetailView from './views/AdminModelDetailView.vue'
import AdminAiSettingsView from './views/AdminAiSettingsView.vue'
import AdminLogsView from './views/AdminLogsView.vue'
import AdminSubscriptionPlansView from './views/AdminSubscriptionPlansView.vue'
import AdminAppActivityView from './views/AdminAppActivityView.vue'
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
        meta: { title: 'AI Providers' },
      },
      {
        path: 'ai-providers/:providerId',
        name: 'admin-provider-detail',
        component: AdminProviderDetailView,
        meta: { title: 'Provider Detail' },
      },
      {
        path: 'ai-providers/:providerId/models',
        name: 'admin-models',
        component: AdminModelsView,
        meta: { title: 'AI Models' },
      },
      {
        path: 'ai-providers/:providerId/models/:modelId',
        name: 'admin-model-detail',
        component: AdminModelDetailView,
        meta: { title: 'Model Detail' },
      },
      {
        path: 'ai-settings',
        name: 'admin-ai-settings',
        component: AdminAiSettingsView,
        meta: {
          title: 'AI Settings',
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
      {
        path: 'app-activity',
        name: 'admin-app-activity',
        component: AdminAppActivityView,
        meta: {
          title: 'App Activity',
        },
      },
    ],
  },
]
