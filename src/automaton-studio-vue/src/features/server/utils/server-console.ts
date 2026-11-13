import type { App } from '@/features/apps/types/apps.types'

/** The Server Console app from the official catalog among the apps the viewer can see, if installed. */
export function serverConsoleApp(apps: App[]): App | null {
  return apps.find((app) =>
    app.installedFromRepo?.toLowerCase() === 'rzvdaniel/automaton-apps'
    && app.installedFromFolder?.split('/').pop() === 'server-console') ?? null
}
