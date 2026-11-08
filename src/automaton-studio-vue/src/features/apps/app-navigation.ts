import type { LocationQuery, LocationQueryRaw } from 'vue-router'

export const APP_VIEW_KEYS = [
  'overview',
  'publishing',
  'settings',
  'logs',
  'deployments',
  'actions',
  'runtime',
  'targets',
  'contract',
  'more',
] as const

export type AppViewKey = (typeof APP_VIEW_KEYS)[number]

export type AppTabKey = 'overview' | 'settings' | 'activity' | 'advanced'

export interface AppTab {
  key: AppTabKey
  label: string
  /** The view a click on the tab opens. */
  view: AppViewKey
}

export interface AppSubView {
  key: AppViewKey
  label: string
}

export const DEFAULT_APP_VIEW: AppViewKey = 'overview'

/** The four tabs of the app page. */
export const APP_TABS: ReadonlyArray<AppTab> = [
  { key: 'overview', label: 'Overview', view: 'overview' },
  { key: 'settings', label: 'Settings & secrets', view: 'settings' },
  { key: 'activity', label: 'Activity', view: 'logs' },
  { key: 'advanced', label: 'Advanced', view: 'runtime' },
]

/** Views inside a tab that has more than one. */
export const APP_SUB_VIEWS: Partial<Record<AppTabKey, ReadonlyArray<AppSubView>>> = {
  activity: [
    { key: 'logs', label: 'Logs' },
    { key: 'deployments', label: 'Deploys' },
    { key: 'actions', label: 'Schedule runs' },
  ],
  advanced: [
    { key: 'runtime', label: 'Runner' },
    { key: 'targets', label: 'Deployment targets' },
    { key: 'contract', label: 'Raw contract' },
    { key: 'more', label: 'Appearance, restore and delete' },
  ],
}

const VIEW_TABS: Record<AppViewKey, AppTabKey> = {
  overview: 'overview',
  publishing: 'overview',
  settings: 'settings',
  logs: 'activity',
  deployments: 'activity',
  actions: 'activity',
  runtime: 'advanced',
  targets: 'advanced',
  contract: 'advanced',
  more: 'advanced',
}

export function tabForView(view: AppViewKey): AppTabKey {
  return VIEW_TABS[view]
}

export function resolveAppView(value: unknown): AppViewKey {
  return typeof value === 'string' && (APP_VIEW_KEYS as ReadonlyArray<string>).includes(value)
    ? (value as AppViewKey)
    : DEFAULT_APP_VIEW
}

/** Old links used `?view=settings&settings=targets`; that section now lives under Advanced. */
export function resolveAppViewFromQuery(query: LocationQuery): AppViewKey {
  const view = resolveAppView(query.view)
  return view === 'settings' && query.settings === 'targets' ? 'targets' : view
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
