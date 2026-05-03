import type { RouteRecordRaw } from 'vue-router'

export const settingsRoutes: RouteRecordRaw[] = [
  {
    path: '/settings',
    name: 'settings',
    component: () => import('./views/SettingsView.vue'),
  },
  {
    path: '/ai-keys',
    name: 'ai-keys',
    component: () => import('./views/AiKeysView.vue'),
  },
  {
    path: '/ai-keys/add',
    name: 'ai-keys-add',
    component: () => import('./views/AiKeyFormView.vue'),
  },
  {
    path: '/ai-keys/:id/edit',
    name: 'ai-keys-edit',
    component: () => import('./views/AiKeyFormView.vue'),
  },
  {
    path: '/pricing',
    name: 'pricing',
    component: () => import('./views/PricingView.vue'),
  },
  {
    path: '/billing',
    name: 'billing',
    component: () => import('./views/BillingStatusView.vue'),
  },
  {
    path: '/billing/success',
    name: 'billing-success',
    component: () => import('./views/BillingSuccessView.vue'),
  },
  {
    path: '/billing/cancel',
    name: 'billing-cancel',
    component: () => import('./views/BillingCancelView.vue'),
  },
]
