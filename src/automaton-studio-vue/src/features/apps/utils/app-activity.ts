import type { ApplicationEvent } from '@/shared/types/ai.types'
import type { SecretAuditEntry } from '../types/apps.types'
import { isActiveRun, isFailedRun } from './app-overview'

export type ActivityFilter = 'all' | 'runs' | 'changes' | 'problems'
export type ActivityTone = 'ok' | 'failed' | 'running' | 'none'

export interface ActivityItem {
  id: string
  kind: 'run' | 'change'
  tone: ActivityTone
  /** The action name for a run; empty for a change. */
  subject: string
  text: string
  at: string
  /** Set for runs, whose log opens from the list. */
  executionId?: string
}

export interface ActivityRun {
  id: string
  moduleKey: string
  status: string
  failureSummary?: string
  createdAtUtc: string
}

/** Runner connect and disconnect events belong on the Server page. */
const hiddenEventTypes = new Set(['RunnerConnected', 'RunnerDisconnected'])

export function buildActivity(
  runs: ReadonlyArray<ActivityRun>,
  events: ReadonlyArray<ApplicationEvent>,
  secrets: ReadonlyArray<SecretAuditEntry>,
): ActivityItem[] {
  const items: ActivityItem[] = []
  for (const run of runs) {
    const failed = isFailedRun(run.status)
    items.push({
      id: `run-${run.id}`,
      kind: 'run',
      tone: failed ? 'failed' : isActiveRun(run.status) ? 'running' : run.status === 'Succeeded' ? 'ok' : 'none',
      subject: run.moduleKey,
      text: failed ? (run.failureSummary || run.status.toLowerCase()) : run.status === 'Succeeded' ? 'ran' : run.status.toLowerCase(),
      at: run.createdAtUtc,
      executionId: run.id,
    })
  }
  for (const event of events) {
    if (hiddenEventTypes.has(event.eventType)) continue
    const problem = event.level.toLowerCase() === 'error' || event.eventType.endsWith('Failed')
    items.push({ id: `event-${event.id}`, kind: 'change', tone: problem ? 'failed' : 'none', subject: '', text: event.message, at: event.createdAtUtc })
  }
  secrets.forEach((entry, index) => {
    if (entry.eventType === 'SecretsDelivered') return
    items.push({
      id: `secret-${index}-${entry.createdAtUtc}`,
      kind: 'change',
      tone: 'none',
      subject: '',
      text: entry.userName ? `${entry.message} by ${entry.userName}` : entry.message,
      at: entry.createdAtUtc,
    })
  })
  return items.sort((a, b) => b.at.localeCompare(a.at))
}

export function filterActivity(items: ReadonlyArray<ActivityItem>, filter: ActivityFilter): ActivityItem[] {
  switch (filter) {
    case 'runs': return items.filter((item) => item.kind === 'run')
    case 'changes': return items.filter((item) => item.kind === 'change')
    case 'problems': return items.filter((item) => item.tone === 'failed')
    default: return [...items]
  }
}

/** Today 03:30, yesterday 09:12, or Nov 6. */
export function formatActivityTime(iso: string, now = new Date()): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const time = date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })
  const startOfDay = (value: Date) => new Date(value.getFullYear(), value.getMonth(), value.getDate()).getTime()
  const days = Math.round((startOfDay(now) - startOfDay(date)) / 86400000)
  if (days === 0) return `today ${time}`
  if (days === 1) return `yesterday ${time}`
  return date.toLocaleDateString([], { month: 'short', day: 'numeric' })
}
