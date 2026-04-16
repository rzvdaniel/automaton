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
