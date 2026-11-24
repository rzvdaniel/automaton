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
  triggerType?: string
  triggerName?: string
}

/** Runner connect and disconnect events belong on the Server page. */
const hiddenEventTypes = new Set(['RunnerConnected', 'RunnerDisconnected'])

export function buildActivity(
  runs: ReadonlyArray<ActivityRun>,
  events: ReadonlyArray<ApplicationEvent>,
  secrets: ReadonlyArray<SecretAuditEntry>,
  deploys: ReadonlyArray<ActivityItem> = [],
  settingNames: ReadonlyArray<string> = [],
): ActivityItem[] {
  const items: ActivityItem[] = []
  for (const run of runs) {
    const failed = isFailedRun(run.status)
    items.push({
      id: `run-${run.id}`,
      kind: 'run',
      tone: failed ? 'failed' : isActiveRun(run.status) ? 'running' : run.status === 'Succeeded' ? 'ok' : 'none',
      subject: run.moduleKey,
      text: describeRun(run, failed),
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
      text: describeSecretChange(entry, settingNames),
      at: entry.createdAtUtc,
    })
  })
  const near = (a: string, b: string) => Math.abs(new Date(a).getTime() - new Date(b).getTime()) < 5 * 60_000
  const logged = events.filter((event) => event.eventType.includes('Deploy'))
  return [...items, ...deploys.filter((deploy) => !logged.some((event) => near(event.createdAtUtc, deploy.at)))].sort((a, b) => b.at.localeCompare(a.at))
}

/** A run says what started it when that is not a person or a schedule: a webhook names itself. */
function describeRun(run: ActivityRun, failed: boolean): string {
  const base = failed ? (run.failureSummary || run.status.toLowerCase()) : run.status === 'Succeeded' ? 'ran' : run.status.toLowerCase()
  return run.triggerType === 'Webhook' ? `${base} (started by webhook ${run.triggerName ?? ''})`.replace(' )', ')') : base
}

/** Settings are stored like secrets, but people see them as settings. */
function describeSecretChange(entry: SecretAuditEntry, settingNames: ReadonlyArray<string>): string {
  const by = entry.userName ? ` by ${entry.userName}` : ''
  if (!entry.name) return `${entry.message}${by}`
  const noun = settingNames.includes(entry.name) ? 'Setting' : 'Secret'
  if (entry.eventType === 'SecretDeleted') return `${noun} ${entry.name} removed${by}`
  return `${noun} ${entry.name} ${(entry.version ?? 1) > 1 ? 'replaced' : 'set'}${by}`
}

/** Deploys the event log did not record: the target remembers its last attempt and last success. */
export function deployActivity(
  target: { lastDeploymentAt?: string | null; lastSuccessfulDeploymentAt?: string | null; lastDeploymentError?: string | null } | null,
): ActivityItem[] {
  if (!target) return []
  const items: ActivityItem[] = []
  if (target.lastSuccessfulDeploymentAt) {
    items.push({ id: 'deploy-ok', kind: 'change', tone: 'none', subject: '', text: 'Deployed', at: target.lastSuccessfulDeploymentAt })
  }
  if (target.lastDeploymentError && target.lastDeploymentAt && target.lastDeploymentAt !== target.lastSuccessfulDeploymentAt) {
    items.push({ id: 'deploy-failed', kind: 'change', tone: 'failed', subject: '', text: `Deploy failed: ${target.lastDeploymentError}`, at: target.lastDeploymentAt })
  }
  return items
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

/** One line of the grouped list: a single item, or a run of the same successful action folded into one line. */
export type ActivityEntry =
  | { kind: 'item'; item: ActivityItem }
  | { kind: 'folded'; id: string; subject: string; count: number; everyMinutes: number | null; at: string; items: ActivityItem[] }

export interface ActivityDay { key: string; label: string; entries: ActivityEntry[] }

/** At least this many successful runs of one action in a row fold into one line. */
export const FOLD_AFTER = 3

const dayKey = (iso: string) => {
  const date = new Date(iso)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

/** Today, Yesterday, or "Mon 17 Nov". */
export function formatDayHeading(iso: string, now = new Date()): string {
  const date = new Date(iso)
  const startOfDay = (value: Date) => new Date(value.getFullYear(), value.getMonth(), value.getDate()).getTime()
  const days = Math.round((startOfDay(now) - startOfDay(date)) / 86400000)
  if (days === 0) return 'Today'
  if (days === 1) return 'Yesterday'
  return date.toLocaleDateString([], { weekday: 'short', day: 'numeric', month: 'short' })
}

/** Groups newest-first items by day and folds repeated successful runs ("home-card ran 55 times, every 15 min"). Failures never fold. */
export function groupActivity(items: ReadonlyArray<ActivityItem>, now = new Date()): ActivityDay[] {
  const days: ActivityDay[] = []
  for (const item of items) {
    if (Number.isNaN(new Date(item.at).getTime())) continue
    const key = dayKey(item.at)
    let day = days[days.length - 1]
    if (!day || day.key !== key) {
      day = { key, label: formatDayHeading(item.at, now), entries: [] }
      days.push(day)
    }
    day.entries.push({ kind: 'item', item })
  }
  for (const day of days) day.entries = foldRuns(day.entries)
  return days
}

const foldable = (item: ActivityItem) => item.kind === 'run' && item.tone === 'ok'

function foldRuns(entries: ActivityEntry[]): ActivityEntry[] {
  const out: ActivityEntry[] = []
  let index = 0
  while (index < entries.length) {
    const first = entries[index]!
    if (first.kind !== 'item' || !foldable(first.item)) { out.push(first); index++; continue }
    const run: ActivityItem[] = [first.item]
    let next = index + 1
    while (next < entries.length) {
      const candidate = entries[next]!
      if (candidate.kind !== 'item' || !foldable(candidate.item) || candidate.item.subject !== first.item.subject) break
      run.push(candidate.item)
      next++
    }
    if (run.length >= FOLD_AFTER) {
      const span = new Date(run[0]!.at).getTime() - new Date(run[run.length - 1]!.at).getTime()
      const every = Math.round(span / (run.length - 1) / 60_000)
      out.push({ kind: 'folded', id: `fold-${run[0]!.id}`, subject: first.item.subject, count: run.length, everyMinutes: every > 0 ? every : null, at: run[0]!.at, items: run })
    } else {
      run.forEach((item) => out.push({ kind: 'item', item }))
    }
    index = next
  }
  return out
}

/** "every 15 min", "every 2 h" (rounded; blank when the runs have no steady gap). */
export function describeEvery(minutes: number | null): string {
  if (!minutes) return ''
  if (minutes >= 120) return `every ${Math.round(minutes / 60)} h`
  return `every ${minutes} min`
}

/** The clock time inside a day group (the day is the heading). */
export function formatClock(iso: string): string {
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })
}
