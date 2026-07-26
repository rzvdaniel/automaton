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
import AdminAiGuidanceView from './views/AdminAiGuidanceView.vue'
import AdminDashboardView from './views/AdminDashboardView.vue'
import AdminLogsView from './views/AdminLogsView.vue'
import AdminSubscriptionPlansView from './views/AdminSubscriptionPlansView.vue'
import AdminAppActivityView from './views/AdminAppActivityView.vue'
import AdminHostedRunnersView from './views/AdminHostedRunnersView.vue'
import AdminHostedRunnerSpecificationsView from './views/AdminHostedRunnerSpecificationsView.vue'
import AdminMarketplaceAppsView from './views/AdminMarketplaceAppsView.vue'
import AdminAppDeployRulesView from './views/AdminAppDeployRulesView.vue'
import AdminUsersView from './views/AdminUsersView.vue'
import AdminUserDetailView from './views/AdminUserDetailView.vue'
import AdminUserAppsView from './views/AdminUserAppsView.vue'
import AdminUserRunnersView from './views/AdminUserRunnersView.vue'
import AdminAppDetailView from './views/AdminAppDetailView.vue'
import AdminSystemApplicationView from './views/AdminSystemApplicationView.vue'
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
        redirect: '/admin/dashboard',
      },
      {
        path: 'dashboard',
        name: 'admin-dashboard',
        component: AdminDashboardView,
        meta: {
          title: 'Dashboard',
        },
      },
      {
        path: 'system',
        name: 'admin-system-application',
        component: AdminSystemApplicationView,
        meta: { title: 'System Application' },
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
        path: 'ai-guidance',
        name: 'admin-ai-guidance',
        component: AdminAiGuidanceView,
        meta: {
          title: 'AI Guidance',
        },
      },
      {
        path: 'users',
        name: 'admin-users',
        component: AdminUsersView,
        meta: {
          title: 'Users',
        },
      },
      {
        path: 'users/:userId',
        name: 'admin-user-detail',
        component: AdminUserDetailView,
        meta: {
          title: 'User Details',
        },
      },
      {
        path: 'users/:userId/apps',
        name: 'admin-user-apps',
        component: AdminUserAppsView,
        meta: {
          title: 'User Apps',
        },
      },
      {
        path: 'apps/:appId',
        name: 'admin-app-detail',
        component: AdminAppDetailView,
        meta: {
          title: 'App Details',
        },
      },
      {
        path: 'users/:userId/runners',
        name: 'admin-user-runners',
        component: AdminUserRunnersView,
        meta: {
          title: 'User Runners',
        },
      },
      {
        path: 'logs',
        name: 'admin-logs',
        component: AdminLogsView,
        meta: {
          title: 'Audit Events',
        },
      },
      {
        path: 'app-deploy-rules',
        name: 'admin-app-deploy-rules',
        component: AdminAppDeployRulesView,
        meta: {
          title: 'App File Exclusions',
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
        path: 'hosted-runners',
        name: 'admin-hosted-runners',
        component: AdminHostedRunnersView,
        meta: {
          title: 'Hosted Runners',
        },
      },
      {
        path: 'hosted-runners/containers',
        name: 'admin-hosted-runner-containers',
        component: AdminHostedRunnersView,
        meta: { title: 'Runner Containers' },
      },
      {
        path: 'hosted-runners/specifications',
        name: 'admin-hosted-runner-specifications',
        component: AdminHostedRunnerSpecificationsView,
        meta: { title: 'Hosted Runner Specifications' },
      },

      {
        path: 'marketplace-apps',
        name: 'admin-marketplace-apps',
        component: AdminMarketplaceAppsView,
        meta: {
          title: 'Marketplace Apps',
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
