export const APP_VIEW_KEYS = [
  'overview',
  'deployments',
  'runtime',
  'logs',
  'actions',
  'snapshots',
  'publishing',
  'settings',
] as const

export type AppViewKey = (typeof APP_VIEW_KEYS)[number]

export interface AppNavigationItem {
  key: AppViewKey
  label: string
  icon: string
}

export interface AppNavigationGroup {
  label?: string
  description?: string
  items: ReadonlyArray<AppNavigationItem>
}

export const DEFAULT_APP_VIEW: AppViewKey = 'overview'

export const APP_NAVIGATION_GROUPS: ReadonlyArray<AppNavigationGroup> = [
  {
    label: 'Operate',
    items: [
      { key: 'overview', label: 'Overview', icon: 'pi pi-home' },
      { key: 'deployments', label: 'Deployments', icon: 'pi pi-cloud-upload' },
      { key: 'runtime', label: 'Runtime', icon: 'pi pi-desktop' },
      { key: 'logs', label: 'Logs', icon: 'pi pi-list' },
      { key: 'actions', label: 'Actions', icon: 'pi pi-bolt' },
    ],
  },
  {
    label: 'Manage',
    items: [
      { key: 'snapshots', label: 'Source snapshots', icon: 'pi pi-history' },
      { key: 'publishing', label: 'Publishing', icon: 'pi pi-globe' },
      { key: 'settings', label: 'Settings', icon: 'pi pi-cog' },
    ],
  },
]

export function resolveAppView(value: unknown): AppViewKey {
  switch (value) {
    case 'overview':
    case 'deployments':
    case 'runtime':
    case 'logs':
    case 'actions':
    case 'snapshots':
    case 'publishing':
    case 'settings':
      return value
    default:
      return DEFAULT_APP_VIEW
  }
}

export function withoutAuthoringQuery(query: LocationQuery): LocationQueryRaw {
  const result: LocationQueryRaw = { ...query }
  delete result.prompt
  delete result.mode
  delete result.model
  delete result.tab
  if (resolveAppView(query.view) === DEFAULT_APP_VIEW) delete result.view
  return result
}
import type { LocationQuery, LocationQueryRaw } from 'vue-router'
