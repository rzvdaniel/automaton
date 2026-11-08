import type { LocationQuery, LocationQueryRaw } from 'vue-router'

export const APP_VIEW_KEYS = ['overview', 'activity', 'settings'] as const

export type AppViewKey = (typeof APP_VIEW_KEYS)[number]

export interface AppTab {
  key: AppViewKey
  label: string
}

export const DEFAULT_APP_VIEW: AppViewKey = 'overview'

/** The three tabs of the app page. */
export const APP_TABS: ReadonlyArray<AppTab> = [
  { key: 'overview', label: 'Overview' },
  { key: 'activity', label: 'Activity' },
  { key: 'settings', label: 'Settings' },
]

/** Earlier `?view=` values and the tab each one now lives in. */
const LEGACY_VIEWS: Record<string, AppViewKey> = {
  logs: 'activity',
  deployments: 'activity',
  actions: 'activity',
  runtime: 'settings',
  targets: 'settings',
  contract: 'settings',
  more: 'settings',
}

/** Earlier views whose content now sits in Settings, under the closed Advanced section. */
const ADVANCED_VIEWS = new Set(['runtime', 'targets', 'contract', 'more'])

export function resolveAppView(value: unknown): AppViewKey {
  if (typeof value !== 'string') return DEFAULT_APP_VIEW
  if ((APP_VIEW_KEYS as ReadonlyArray<string>).includes(value)) return value as AppViewKey
  return LEGACY_VIEWS[value] ?? DEFAULT_APP_VIEW
}

export function resolveAppViewFromQuery(query: LocationQuery): AppViewKey {
  return resolveAppView(query.view)
}

/** True when an earlier link pointed at something that is now inside Settings > Advanced. */
export function opensAdvanced(query: LocationQuery): boolean {
  return (typeof query.view === 'string' && ADVANCED_VIEWS.has(query.view))
    || (query.view === 'settings' && query.settings === 'targets')
}

export function withoutAuthoringQuery(query: LocationQuery): LocationQueryRaw {
  const result: LocationQueryRaw = { ...query }
  delete result.prompt
  delete result.mode
  delete result.model
  delete result.tab
  delete result.share
  if (resolveAppView(query.view) === DEFAULT_APP_VIEW) delete result.view
  return result
}
