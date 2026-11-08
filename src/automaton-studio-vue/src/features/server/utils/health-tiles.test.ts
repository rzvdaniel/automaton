import { describe, expect, it } from 'vitest'
import { healthTiles } from './health-tiles'
import type { ServerHealth } from '../types/server.types'

const GIB = 1024 ** 3
const health = (overrides: Partial<ServerHealth> = {}): ServerHealth => ({
  generatedAtUtc: '2026-11-08T10:00:00Z',
  server: { version: 'server-v1', latestVersion: 'server-v1', updateAvailable: false },
  host: {
    measuredAtUtc: '2026-11-08T10:00:00Z', osName: 'Ubuntu', osVersion: '24.04',
    memoryTotalBytes: 8 * GIB, memoryAvailableBytes: 6 * GIB,
    diskTotalBytes: 100 * GIB, diskAvailableBytes: 60 * GIB,
    updatesPending: 0, securityUpdates: 0, rebootRequired: false,
  },
  hostAgeSeconds: 30,
  certificates: [{ host: 'studio.example.test', notAfterUtc: null, daysRemaining: 60, error: null }],
  backup: { available: true, lastSuccessAt: '2026-11-08T03:00:00Z', lastSuccessAgeHours: 7, lastError: null },
  ...overrides,
})
const tile = (value: ServerHealth, key: string) => healthTiles(value).find((candidate) => candidate.key === key)!

describe('healthTiles', () => {
  it('turns the host figures into the five tiles', () => {
    expect(healthTiles(health()).map((candidate) => candidate.key)).toEqual(['disk', 'memory', 'updates', 'certificates', 'backup'])
    expect(tile(health(), 'disk')).toMatchObject({ value: '40% used', detail: '40 GB of 100 GB', level: 'ok' })
    expect(tile(health(), 'memory')).toMatchObject({ value: '25% used', detail: '2.0 GB of 8.0 GB' })
    expect(tile(health(), 'updates')).toMatchObject({ value: 'Up to date', level: 'ok' })
    expect(tile(health(), 'certificates')).toMatchObject({ value: '60 days left', detail: 'studio.example.test' })
    expect(tile(health(), 'backup')).toMatchObject({ value: '7 hours ago', level: 'ok' })
  })

  it('warns on a full disk, security updates and a restart', () => {
    const base = health()
    const host = { ...base.host!, diskAvailableBytes: 5 * GIB, updatesPending: 4, securityUpdates: 2, rebootRequired: true }
    expect(tile({ ...base, host }, 'disk').level).toBe('danger')
    expect(tile({ ...base, host }, 'updates')).toMatchObject({ value: '4 pending', detail: '2 security, restart needed', level: 'warn' })
  })

  it('uses the soonest certificate and flags one close to expiry', () => {
    const certificates = [
      { host: 'a.example.test', notAfterUtc: null, daysRemaining: 40, error: null },
      { host: 'b.example.test', notAfterUtc: null, daysRemaining: 5, error: null },
    ]
    expect(tile(health({ certificates }), 'certificates')).toMatchObject({ value: '5 days left', detail: 'b.example.test', level: 'danger' })
  })

  it('says so when the Runner has not reported yet or no backup ran', () => {
    const waiting = health({ host: null, backup: { available: true, lastSuccessAt: null, lastSuccessAgeHours: null, lastError: null } })
    expect(tile(waiting, 'disk')).toMatchObject({ value: 'Not reported', level: 'unknown' })
    expect(tile(waiting, 'updates').value).toBe('Not reported')
    expect(tile(waiting, 'backup')).toMatchObject({ value: 'Never', level: 'warn' })
  })
})
