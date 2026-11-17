import type { AppListItem } from '@/features/apps/types/apps.types'

export interface HomeCard {
  key: string
  kind: string
  severity: 'info' | 'warn' | 'error'
  title: string
  detail: string
  actionLabel: string
  /** A Studio route. */
  actionPath: string
  dismissible: boolean
  appId?: string | null
  /** Catalog id of the app a recommended-app card offers. */
  catalogAppId?: string | null
}

/** What an app's Action put on its Home card. Plain text only: always show it as text, never as HTML. */
export interface AppHomeCard {
  card: {
    status: string
    facts: { label: string; value: string }[]
    alerts: { severity: 'info' | 'warning' | 'critical'; text: string }[]
  }
  updatedAtUtc: string
  /** The last card run failed or the card was not refreshed in time. */
  stale: boolean
}

export interface HomePinnedApp {
  app: AppListItem
  pinnedAtUtc: string
  card?: AppHomeCard | null
}

export interface HomeData {
  needsYou: HomeCard[]
  pinned: HomePinnedApp[]
}
