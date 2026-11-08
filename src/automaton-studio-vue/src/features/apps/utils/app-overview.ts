import type { ActionSchedule } from '../services/action-schedules.service'
import type { App, SharingAudience } from '../types/apps.types'
import { isActionOnlyApp, type AppOperationalSummary } from './app-operational-summary'

/** What the app is, which decides its main button and what its Overview lists. */
export type AppKind = 'tool' | 'site' | 'web' | 'api'
export type AppHealth = 'healthy' | 'failing' | 'stopped'

export interface Segment {
  text: string
  strong?: boolean
  mono?: boolean
}

export interface RunSummary {
  status: string
  createdAtUtc: string
}

const failedRunStatuses = new Set(['Failed', 'TimedOut', 'Lost', 'RunnerUnavailable'])
const finishedRunStatuses = new Set(['Succeeded', 'Failed', 'TimedOut', 'Cancelled', 'RunnerUnavailable', 'Lost'])

export const isFailedRun = (status: string) => failedRunStatuses.has(status)
export const isActiveRun = (status: string) => !finishedRunStatuses.has(status)

export function appKindOf(app: Pick<App, 'application'>): AppKind {
  if (isActionOnlyApp(app)) return 'tool'
  if (app.application?.isStaticSite) return 'site'
  return app.application?.kind === 'api' ? 'api' : 'web'
}

export function getAppHealth(summary: AppOperationalSummary, lastRun?: RunSummary | null): AppHealth {
  switch (summary.status) {
    case 'not-deployed':
    case 'no-runtime-assigned':
      return 'stopped'
    case 'deployment-failed':
    case 'runtime-unavailable':
    case 'degraded':
      return 'failing'
    default:
      return lastRun && isFailedRun(lastRun.status) ? 'failing' : 'healthy'
  }
}

const plural = (count: number, unit: string) => `${count} ${unit}${count === 1 ? '' : 's'}`

/** "5 minutes ago" or "in 3 hours"; older or later than a week becomes a date. */
export function formatWhen(iso: string, now = new Date()): string {
  const time = new Date(iso).getTime()
  if (Number.isNaN(time)) return 'at an unknown time'
  const seconds = Math.round((time - now.getTime()) / 1000)
  const minutes = Math.floor(Math.abs(seconds) / 60)
  const label = minutes < 1 ? '' : minutes < 60 ? plural(minutes, 'minute') : minutes < 1440 ? plural(Math.floor(minutes / 60), 'hour') : plural(Math.floor(minutes / 1440), 'day')
  if (!label) return seconds > 30 ? 'in a moment' : 'just now'
  if (minutes >= 7 * 1440) return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  return seconds < 0 ? `${label} ago` : `in ${label}`
}

export function describeSchedule(schedule: Pick<ActionSchedule, 'scheduleType' | 'intervalSeconds' | 'cronExpression' | 'runAtUtc'>): string {
  if (schedule.scheduleType === 'Cron') return `on schedule ${schedule.cronExpression ?? ''}`.trim()
  if (schedule.scheduleType === 'OneTime') return 'once'
  const seconds = schedule.intervalSeconds ?? 0
  if (seconds > 0 && seconds % 86400 === 0) return seconds === 86400 ? 'every day' : `every ${seconds / 86400} days`
  if (seconds > 0 && seconds % 3600 === 0) return seconds === 3600 ? 'every hour' : `every ${seconds / 3600} hours`
  if (seconds > 0 && seconds % 60 === 0) return seconds === 60 ? 'every minute' : `every ${seconds / 60} minutes`
  return `every ${seconds} seconds`
}

export function audienceSentence(audience: SharingAudience | undefined, peopleCount: number): Segment[] {
  switch (audience) {
    case 'internet': return [{ text: 'Anyone', strong: true }, { text: ' can open it.' }]
    case 'server': return [{ text: 'Everyone signed in on this server', strong: true }, { text: ' can open it.' }]
    case 'people': return [{ text: peopleCount === 1 ? '1 person' : `${peopleCount} people`, strong: true }, { text: ' can open it.' }]
    default: return [{ text: 'Only you', strong: true }, { text: ' can open it.' }]
  }
}

export function audienceLabel(audience: SharingAudience | undefined, peopleCount: number): string {
  switch (audience) {
    case 'internet': return 'Anyone with the link'
    case 'server': return 'Everyone on this server'
    case 'people': return peopleCount === 1 ? '1 person' : `${peopleCount} people`
    default: return 'Only you'
  }
}

export interface StatusInput {
  kind: AppKind
  lastRun?: RunSummary | null
  schedules: ReadonlyArray<Pick<ActionSchedule, 'enabled' | 'scheduleType' | 'intervalSeconds' | 'cronExpression' | 'runAtUtc' | 'nextRunAtUtc'>>
  host: string
  audience?: SharingAudience
  peopleCount: number
  lastDeployedAt?: string | null
  deployError?: string | null
  failed: boolean
  now?: Date
}

/** The one plain sentence at the top of Overview. */
export function statusSentence(input: StatusInput): Segment[] {
  const now = input.now ?? new Date()
  if (input.kind === 'tool') return toolSentence(input, now)

  if (input.failed) {
    const reason = input.deployError ? `: ${input.deployError}` : '.'
    return [{ text: 'The last deploy failed', strong: true }, { text: reason }]
  }
  if (!input.host) return [{ text: 'Not live yet', strong: true }, { text: '. Deploy it from your AI or with the CLI.' }]

  const parts: Segment[] = [
    { text: 'Live', strong: true }, { text: ' at ' }, { text: input.host, mono: true }, { text: '. ' },
    ...(input.kind === 'api' && input.audience !== 'internet'
      ? [{ text: 'Callers need an ' }, { text: 'API key', strong: true }, { text: '.' }]
      : audienceSentence(input.audience, input.peopleCount)),
  ]
  if (input.lastDeployedAt) parts.push({ text: ' Last deployed ' }, { text: formatWhen(input.lastDeployedAt, now), strong: true }, { text: '.' })
  return parts
}

function toolSentence(input: StatusInput, now: Date): Segment[] {
  const enabled = input.schedules.filter((schedule) => schedule.enabled)
  const parts: Segment[] = enabled.length === 0
    ? [{ text: 'Runs ' }, { text: 'when you start it', strong: true }, { text: '.' }]
    : [{ text: 'Runs ' }, { text: describeSchedule(enabled[0]!), strong: true }, ...(enabled.length > 1 ? [{ text: ` and ${enabled.length - 1} more` }] : []), { text: '.' }]

  const run = input.lastRun
  if (!run) parts.push({ text: ' It has not run yet.' })
  else if (isActiveRun(run.status)) parts.push({ text: ' It ' }, { text: 'is running now', strong: true }, { text: '.' })
  else {
    const verb = run.status === 'Succeeded' ? 'succeeded' : isFailedRun(run.status) ? 'failed' : run.status.toLowerCase()
    parts.push({ text: ' The last run ' }, { text: `${verb} ${formatWhen(run.createdAtUtc, now)}`, strong: true }, { text: '.' })
  }

  const next = enabled.map((schedule) => schedule.nextRunAtUtc).filter((value): value is string => !!value).sort()[0]
  if (next) parts.push({ text: ' Next run ' }, { text: formatWhen(next, now), strong: true }, { text: '.' })
  return parts
}
