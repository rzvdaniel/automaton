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
  'server:restart': { wants: 'restart the server', allows: 'Lets the app restart this server, now or at a time you choose. Apps stop for a moment and come back by themselves.' },
  'server:update-os': { wants: 'install OS updates', allows: 'Lets the app install this server\'s operating system updates (security only, or all). It never reboots the server; restarting needs its own approval.' },
  'backups:run': { wants: 'start backups', allows: 'Lets the app start a backup of this server.' },
  'server:read': { wants: 'read server status', allows: 'Lets the app read this server\'s health and settings status.' },
  'security:read': { wants: 'read fail2ban bans and host security findings', allows: 'Lets the app see which addresses fail2ban banned on this server and whether SSH allows passwords, extra ports are open or no firewall is found. It cannot change any of it.' },
  'security:manage': { wants: 'unban and allowlist addresses', allows: 'Lets the app lift a fail2ban ban or allowlist an address on this server when a system administrator asks it to. It gets no firewall or shell access.' },
  'server:cleanup': { wants: 'clean up the server', allows: 'Lets the app free disk space on this server.' },
  'runner:host-exec': { wants: 'run any command as root on a machine', allows: 'Lets an app you own run any command as root on the machines you approve. Off until you turn host access on.' },
  'runner:maintain': { wants: 'maintain Runners', allows: 'Lets the app run maintenance on the Runners you approve.' },
  'logs:receive': { wants: 'receive the server logs', allows: 'Lets the Automaton API, Worker and Runners send their logs to the app. Logs can contain addresses and user names; they go to the Runner that hosts the app, over the private network of the server.' },
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
