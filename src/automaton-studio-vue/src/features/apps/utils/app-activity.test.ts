import { describe, expect, it } from 'vitest'
import { buildActivity, filterActivity, formatActivityTime } from './app-activity'
import type { ApplicationEvent } from '@/shared/types/ai.types'
import type { SecretAuditEntry } from '../types/apps.types'

const event = (over: Partial<ApplicationEvent>): ApplicationEvent => ({
  id: 'e1', appId: 'a', level: 'info', category: 'deploy', eventType: 'AppDeployCompleted', message: 'Deployed', createdAtUtc: '2026-11-06T10:00:00Z', ...over,
})
const secret = (over: Partial<SecretAuditEntry>): SecretAuditEntry => ({
  createdAtUtc: '2026-11-07T09:12:00Z', eventType: 'SecretSet', message: 'Secret S3_KEY was set', delivered: [], ...over,
})

const items = buildActivity(
  [
    { id: 'r1', moduleKey: 'upload', status: 'Succeeded', createdAtUtc: '2026-11-08T03:30:00Z' },
    { id: 'r2', moduleKey: 'upload', status: 'Failed', failureSummary: 'S3 refused the access key', createdAtUtc: '2026-11-07T03:30:00Z' },
  ],
  [
    event({}),
    event({ id: 'e2', eventType: 'RunnerConnected', message: 'Runner connected', createdAtUtc: '2026-11-08T01:00:00Z' }),
    event({ id: 'e3', level: 'error', eventType: 'AppDeployFailed', message: 'Deploy failed', createdAtUtc: '2026-11-05T10:00:00Z' }),
  ],
  [secret({ userName: 'Razvan' }), secret({ eventType: 'SecretsDelivered', message: 'delivered' })],
)

describe('app activity', () => {
  it('merges runs, deploys and secret changes, newest first, without Runner connections or deliveries', () => {
    expect(items.map((item) => item.id)).toEqual(['run-r1', 'secret-0-2026-11-07T09:12:00Z', 'run-r2', 'event-e1', 'event-e3'])
    expect(items.find((item) => item.id.startsWith('secret'))?.text).toBe('Secret S3_KEY was set by Razvan')
  })

  it('filters runs, changes and problems', () => {
    expect(filterActivity(items, 'all')).toHaveLength(5)
    expect(filterActivity(items, 'runs').map((item) => item.id)).toEqual(['run-r1', 'run-r2'])
    expect(filterActivity(items, 'changes')).toHaveLength(3)
    expect(filterActivity(items, 'problems').map((item) => item.id)).toEqual(['run-r2', 'event-e3'])
  })

  it('words the time as today, yesterday or a date', () => {
    const now = new Date(2026, 10, 8, 12, 0)
    expect(formatActivityTime(new Date(2026, 10, 8, 3, 30).toISOString(), now)).toMatch(/^today /)
    expect(formatActivityTime(new Date(2026, 10, 7, 9, 12).toISOString(), now)).toMatch(/^yesterday /)
    expect(formatActivityTime(new Date(2026, 10, 1, 9, 12).toISOString(), now)).not.toMatch(/today|yesterday/)
  })
})
