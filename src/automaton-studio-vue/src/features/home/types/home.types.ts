import type { CardBlock, CardTone } from './home-card.types'
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
    detail?: string | null
    tone?: CardTone | null
    blocks?: CardBlock[]
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

/** One entry of the notification feed Studio, the CLI and the MCP tool share. */
export interface NotificationItem {
  key: string
  kind: string
  severity: 'info' | 'warning' | 'critical'
  title: string
  detail: string
  actionLabel: string
  /** A Studio route. */
  actionPath: string
  count: number
  subjects: string[]
  /** False for critical notices: they stay until the cause is fixed. */
  acknowledgeable: boolean
}
