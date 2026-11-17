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
}

export interface HomePinnedApp {
  app: AppListItem
  pinnedAtUtc: string
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
