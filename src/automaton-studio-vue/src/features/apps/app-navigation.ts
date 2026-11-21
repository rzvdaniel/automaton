import type { LocationQuery, LocationQueryRaw } from 'vue-router'

export const APP_VIEW_KEYS = ['overview', 'activity', 'actions', 'source', 'secrets', 'domain', 'store', 'settings', 'admin'] as const

export type AppViewKey = (typeof APP_VIEW_KEYS)[number]

export interface AppTab {
  key: AppViewKey
  label: string
  /** Count shown beside the label; hidden when 0. */
  badge?: number
}

export const DEFAULT_APP_VIEW: AppViewKey = 'overview'

const LABELS: Record<AppViewKey, string> = {
  overview: 'Overview',
  activity: 'Activity',
  actions: 'Actions',
  source: 'Source',
  secrets: 'Secrets',
  domain: 'Domain',
  store: 'Store listing',
  settings: 'Settings',
  admin: 'Admin',
}

export interface AppTabOptions {
  hasActions?: boolean
  hasSecrets?: boolean
  /** Can change the app (Secrets, Settings). */
  canManage?: boolean
  /** Has an address people open (not a tool): Domain and Store listing. */
  isPublishable?: boolean
  isAdmin?: boolean
  /** Permission requests waiting on this app; badge on the Admin tab. */
  waitingPermissions?: number
}

/** The tabs of the app page, in order; each shows only when it applies to this app and person. */
export function appTabs({ hasActions = false, hasSecrets = false, canManage = false, isPublishable = false, isAdmin = false, waitingPermissions = 0 }: AppTabOptions = {}): ReadonlyArray<AppTab> {
  const keys: AppViewKey[] = ['overview', 'activity']
  if (hasActions) keys.push('actions')
  if (canManage || isAdmin) keys.push('source')
  if (canManage && hasSecrets) keys.push('secrets')
  if (canManage && isPublishable) keys.push('domain', 'store')
  if (canManage) keys.push('settings')
  if (isAdmin) keys.push('admin')
  return keys.map((key) => key === 'admin' && waitingPermissions > 0 ? { key, label: LABELS[key], badge: waitingPermissions } : { key, label: LABELS[key] })
}

/** Earlier `?view=` values and the tab each one now lives in. */
const LEGACY_VIEWS: Record<string, AppViewKey> = {
  logs: 'activity',
  deployments: 'activity',
  runtime: 'overview',
  targets: 'overview',
  contract: 'overview',
  more: 'settings',
}

export function resolveAppView(value: unknown): AppViewKey {
  if (typeof value !== 'string') return DEFAULT_APP_VIEW
  if ((APP_VIEW_KEYS as ReadonlyArray<string>).includes(value)) return value as AppViewKey
  return LEGACY_VIEWS[value] ?? DEFAULT_APP_VIEW
}

export function resolveAppViewFromQuery(query: LocationQuery): AppViewKey {
  return resolveAppView(query.view)
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
