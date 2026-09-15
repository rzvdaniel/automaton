export const APP_VIEW_KEYS = [
  'overview',
  'deployments',
  'runtime',
  'logs',
  'actions',
  'snapshots',
  'publishing',
  'settings',
  'build',
  'files',
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
  {
    label: 'Legacy authoring',
    description: 'Temporary access before Stage 2',
    items: [
      { key: 'build', label: 'AI workspace', icon: 'pi pi-sparkles' },
      { key: 'files', label: 'Source editor', icon: 'pi pi-file-edit' },
    ],
  },
]
