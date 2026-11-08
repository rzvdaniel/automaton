import type { HealthTile, ServerHealth, TileLevel } from '../types/server.types'

const GIB = 1024 ** 3
const WAITING = 'Waiting for the Runner on this server'

export function formatGiB(bytes: number): string {
  const value = bytes / GIB
  return `${value.toFixed(value >= 10 ? 0 : 1)} GB`
}

function usageTile(key: 'disk' | 'memory', label: string, total?: number | null, available?: number | null): HealthTile {
  if (!total || available == null) return { key, label, value: 'Not reported', detail: WAITING, level: 'unknown' }
  const used = total - available
  const percent = Math.round((used / total) * 100)
  const level: TileLevel = percent >= 90 ? 'danger' : percent >= 80 ? 'warn' : 'ok'
  return { key, label, value: `${percent}% used`, detail: `${formatGiB(used)} of ${formatGiB(total)}`, level }
}

function updatesTile(host: ServerHealth['host']): HealthTile {
  if (!host || host.updatesPending == null) {
    return { key: 'updates', label: 'System updates', value: 'Not reported', detail: WAITING, level: 'unknown' }
  }
  const security = host.securityUpdates ?? 0
  const level: TileLevel = security > 0 || host.rebootRequired ? 'warn' : 'ok'
  const value = host.updatesPending === 0 ? 'Up to date' : `${host.updatesPending} pending`
  const detail = `${security} security${host.rebootRequired ? ', restart needed' : ''}`
  return { key: 'updates', label: 'System updates', value, detail, level }
}

function certificatesTile(certificates: ServerHealth['certificates']): HealthTile {
  const dated = certificates.filter((certificate) => certificate.daysRemaining != null)
  if (dated.length === 0) {
    const error = certificates.find((certificate) => certificate.error)?.error
    return error
      ? { key: 'certificates', label: 'Certificates', value: 'Check failed', detail: error, level: 'warn' }
      : { key: 'certificates', label: 'Certificates', value: 'None checked', detail: 'No certificate found', level: 'unknown' }
  }
  const soonest = dated.reduce((first, next) => (next.daysRemaining! < first.daysRemaining! ? next : first))
  const days = soonest.daysRemaining!
  const level: TileLevel = days < 7 ? 'danger' : days < 21 ? 'warn' : 'ok'
  const value = days < 0 ? 'Expired' : days === 1 ? '1 day left' : `${days} days left`
  return { key: 'certificates', label: 'Certificates', value, detail: soonest.host, level }
}

function backupTile(backup: ServerHealth['backup']): HealthTile {
  if (!backup.lastSuccessAt) {
    return { key: 'backup', label: 'Last backup', value: 'Never', detail: backup.lastError ?? 'No backup has finished yet', level: backup.available ? 'warn' : 'unknown' }
  }
  const hours = backup.lastSuccessAgeHours ?? 0
  const value = hours < 1 ? 'Under an hour ago' : hours < 48 ? `${Math.round(hours)} hours ago` : `${Math.round(hours / 24)} days ago`
  return { key: 'backup', label: 'Last backup', value, detail: backup.lastError ?? 'Succeeded', level: backup.lastError || hours > 48 ? 'warn' : 'ok' }
}

/** The five figures an administrator checks first, each with a traffic-light level. */
export function healthTiles(health: ServerHealth): HealthTile[] {
  return [
    usageTile('disk', 'Disk', health.host?.diskTotalBytes, health.host?.diskAvailableBytes),
    usageTile('memory', 'Memory', health.host?.memoryTotalBytes, health.host?.memoryAvailableBytes),
    updatesTile(health.host),
    certificatesTile(health.certificates),
    backupTile(health.backup),
  ]
}
