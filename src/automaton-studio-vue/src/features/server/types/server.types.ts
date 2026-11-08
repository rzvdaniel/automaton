/** GET /api/server/health (ServerHealthResponse). Only the figures Studio shows. */
export interface ServerHealthHost {
  measuredAtUtc: string
  osName: string | null
  osVersion: string | null
  memoryTotalBytes: number | null
  memoryAvailableBytes: number | null
  diskTotalBytes: number | null
  diskAvailableBytes: number | null
  updatesPending: number | null
  securityUpdates: number | null
  rebootRequired: boolean | null
}

export interface ServerHealthCertificate {
  host: string
  notAfterUtc: string | null
  daysRemaining: number | null
  error: string | null
}

export interface ServerHealth {
  generatedAtUtc: string
  server: { version: string | null; latestVersion: string | null; updateAvailable: boolean | null }
  host: ServerHealthHost | null
  hostAgeSeconds: number | null
  certificates: ServerHealthCertificate[]
  backup: { available: boolean; lastSuccessAt: string | null; lastSuccessAgeHours: number | null; lastError: string | null }
}

export type TileLevel = 'ok' | 'warn' | 'danger' | 'unknown'

export interface HealthTile {
  key: 'disk' | 'memory' | 'updates' | 'certificates' | 'backup'
  label: string
  value: string
  detail: string
  level: TileLevel
}
