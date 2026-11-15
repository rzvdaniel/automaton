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
