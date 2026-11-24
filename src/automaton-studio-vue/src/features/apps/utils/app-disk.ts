import type { AppDiskUsage } from '../types/apps.types'

export function formatBytes(bytes: number): string {
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(1)} GB`
  if (bytes >= 1024 ** 2) return `${Math.round(bytes / 1024 ** 2)} MB`
  if (bytes >= 1024) return `${Math.round(bytes / 1024)} KB`
  return `${bytes} B`
}

/** "Disk: 1.2 GB — image 540 MB, data 600 MB, logs 60 MB, files 12 MB"; parts the Runner could not measure are left out. */
export function describeDisk(disk: AppDiskUsage): string {
  const parts: [string, number | null | undefined][] = [
    ['image', disk.imageBytes], ['container', disk.containerBytes], ['data', disk.dataBytes],
    ['logs', disk.logsBytes], ['files', disk.filesBytes],
  ]
  const shown = parts.filter(([, bytes]) => bytes != null && bytes > 0).map(([name, bytes]) => `${name} ${formatBytes(bytes as number)}`)
  return `${formatBytes(disk.totalBytes)}${shown.length ? ` — ${shown.join(', ')}` : ''}`
}

export const HOST_DISK_WARNING = 80

/** The parts of an app's disk use that were measured, largest first, for the split bar. */
export function diskParts(disk: AppDiskUsage): Array<{ name: string; bytes: number }> {
  const all: [string, number | null | undefined][] = [
    ['image', disk.imageBytes], ['files', disk.filesBytes], ['data', disk.dataBytes],
    ['container', disk.containerBytes], ['logs', disk.logsBytes],
  ]
  return all.filter(([, bytes]) => bytes != null && bytes > 0).map(([name, bytes]) => ({ name, bytes: bytes as number }))
    .sort((a, b) => b.bytes - a.bytes)
}

/** The machine disk the app lives on, when its Runner reported it. */
export function hostDiskUse(disk: AppDiskUsage): { used: number; total: number; free: number; percent: number } | null {
  const total = disk.hostDiskTotalBytes
  const free = disk.hostDiskAvailableBytes
  if (!total || total <= 0 || free == null || free > total) return null
  return { used: total - free, total, free, percent: Math.round(((total - free) / total) * 100) }
}
