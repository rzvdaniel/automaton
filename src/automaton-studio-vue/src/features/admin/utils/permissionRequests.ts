import type { AdminAppListItem } from '../services/adminAppsService'

/** One permission an app waits to have approved. */
export interface PermissionRequest {
  appId: string
  appName: string
  permission: string
}

const words: Record<string, { wants: string; allows: string }> = {
  'notify:email': { wants: 'send email', allows: 'Lets the app email people through this server\'s email settings. It never sees the SMTP password.' },
  'backups:read': { wants: 'read backups', allows: 'Lets the app list this server\'s backups.' },
  'backups:run': { wants: 'start backups', allows: 'Lets the app start a backup of this server.' },
  'server:read': { wants: 'read server status', allows: 'Lets the app read this server\'s health and settings status.' },
  'server:cleanup': { wants: 'clean up the server', allows: 'Lets the app free disk space on this server.' },
  'runner:maintain': { wants: 'maintain Runners', allows: 'Lets the app run maintenance on the Runners you approve.' },
}

/** What the app wants in plain words; unknown permissions show their own name. */
export function wantsText(permission: string): string {
  return words[permission]?.wants ?? `use ${permission}`
}

export function allowsText(permission: string): string {
  return words[permission]?.allows ?? 'Lets the app use this platform permission.'
}

/** "e2e-heartbeat wants to send email (notify:email)" */
export function requestLine(request: PermissionRequest): string {
  return `${request.appName} wants to ${wantsText(request.permission)} (${request.permission})`
}

export function toRequests(apps: AdminAppListItem[]): PermissionRequest[] {
  return apps.flatMap((app) => app.waitingPermissions.map((permission) => ({ appId: app.id, appName: app.name, permission })))
}

export function groupByApp(requests: PermissionRequest[]): { appId: string; appName: string; items: PermissionRequest[] }[] {
  const groups = new Map<string, { appId: string; appName: string; items: PermissionRequest[] }>()
  for (const request of requests) {
    const group = groups.get(request.appId) ?? { appId: request.appId, appName: request.appName, items: [] }
    group.items.push(request)
    groups.set(request.appId, group)
  }
  return [...groups.values()]
}
