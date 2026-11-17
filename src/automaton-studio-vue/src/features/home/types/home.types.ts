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

export interface HomePinnedApp {
  app: AppListItem
  pinnedAtUtc: string
}

export interface HomeData {
  needsYou: HomeCard[]
  pinned: HomePinnedApp[]
}
