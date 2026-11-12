import { describe, expect, it } from 'vitest'
import { accessLabel, appOriginLabel, appKindOf, describeSchedule, formatWhen, getAppHealth, secretsPill, statusSentence, type StatusInput } from './app-overview'
import type { AppOperationalSummary } from './app-operational-summary'

const now = new Date('2026-11-08T12:00:00Z')
const text = (input: Partial<StatusInput>) => statusSentence({
  kind: 'tool', schedules: [], host: '', peopleCount: 0, failed: false, now, ...input,
}).map((part) => part.text).join('')

const summary = (status: AppOperationalSummary['status']) => ({ status, label: '', severity: 'success', hasDeployedRuntime: true }) as AppOperationalSummary

describe('app kind', () => {
  const app = (application: Record<string, unknown>) => ({ application }) as never
  it('tells tools, sites, APIs and web apps apart', () => {
    expect(appKindOf(app({ runtime: { command: null } }))).toBe('tool')
    expect(appKindOf(app({ isStaticSite: true }))).toBe('site')
    expect(appKindOf(app({ runtime: { command: { executable: 'node' } }, kind: 'api' }))).toBe('api')
    expect(appKindOf(app({ runtime: { command: { executable: 'node' } } }))).toBe('web')
  })
})

describe('health', () => {
  it('is healthy, failing or stopped', () => {
    expect(getAppHealth(summary('running'))).toBe('healthy')
    expect(getAppHealth(summary('actions-only'), { status: 'Succeeded', createdAtUtc: '' })).toBe('healthy')
    expect(getAppHealth(summary('actions-only'), { status: 'Failed', createdAtUtc: '' })).toBe('failing')
    expect(getAppHealth(summary('deployment-failed'))).toBe('failing')
    expect(getAppHealth(summary('runtime-unavailable'))).toBe('failing')
    expect(getAppHealth(summary('not-deployed'))).toBe('stopped')
    expect(getAppHealth(summary('no-runtime-assigned'))).toBe('stopped')
  })
})

describe('time and schedules', () => {
  it('words times relative to now', () => {
    expect(formatWhen('2026-11-08T11:59:50Z', now)).toBe('just now')
    expect(formatWhen('2026-11-08T11:48:00Z', now)).toBe('12 minutes ago')
    expect(formatWhen('2026-11-08T00:00:00Z', now)).toBe('12 hours ago')
    expect(formatWhen('2026-11-08T15:00:00Z', now)).toBe('in 3 hours')
    expect(formatWhen('2026-11-06T12:00:00Z', now)).toBe('2 days ago')
  })

  it('describes intervals and cron', () => {
    expect(describeSchedule({ scheduleType: 'Interval', intervalSeconds: 3600 })).toBe('every hour')
    expect(describeSchedule({ scheduleType: 'Interval', intervalSeconds: 900 })).toBe('every 15 minutes')
    expect(describeSchedule({ scheduleType: 'Interval', intervalSeconds: 86400 })).toBe('every day')
    expect(describeSchedule({ scheduleType: 'Cron', cronExpression: '30 3 * * *' })).toBe('every day at 03:30 UTC')
  })

  it('describes common cron shapes in words with the time zone, UTC when none', () => {
    const cron = (cronExpression: string, timeZone?: string) => describeSchedule({ scheduleType: 'Cron', cronExpression, timeZone })
    expect(cron('30 4 * * *')).toBe('every day at 04:30 UTC')
    expect(cron('30 4 * * *', 'Etc/UTC')).toBe('every day at 04:30 UTC')
    expect(cron('30 4 * * *', 'Europe/Bucharest')).toBe('every day at 04:30 Europe/Bucharest')
    expect(cron('*/15 * * * *')).toBe('every 15 minutes')
    expect(cron('* * * * *')).toBe('every minute')
    expect(cron('0 * * * *')).toBe('every hour')
    expect(cron('5 * * * *')).toBe('every hour at :05')
    expect(cron('0 9 * * 1')).toBe('every Monday at 09:00 UTC')
    expect(cron('0 9 * * 0')).toBe('every Sunday at 09:00 UTC')
    expect(cron('0 9 * * 7')).toBe('every Sunday at 09:00 UTC')
    expect(cron('0 6 15 * *')).toBe('on day 15 of every month at 06:00 UTC')
  })

  it('falls back to the cron text for anything it cannot describe', () => {
    const cron = (cronExpression: string) => describeSchedule({ scheduleType: 'Cron', cronExpression })
    expect(cron('0 9 * * 1-5')).toBe('on schedule 0 9 * * 1-5')
    expect(cron('0,30 9 * * *')).toBe('on schedule 0,30 9 * * *')
    expect(cron('0 9 1 6 *')).toBe('on schedule 0 9 1 6 *')
    expect(cron('*/90 * * * *')).toBe('on schedule */90 * * * *')
    expect(cron('0 24 * * *')).toBe('on schedule 0 24 * * *')
    expect(cron('0 9 * * 8')).toBe('on schedule 0 9 * * 8')
    expect(cron('0 9 0 * *')).toBe('on schedule 0 9 0 * *')
    expect(cron('@daily')).toBe('on schedule @daily')
    expect(cron('')).toBe('on schedule')
  })
})

describe('status sentence', () => {
  it('says when a tool runs, its last run and the next run', () => {
    expect(text({
      schedules: [{ enabled: true, scheduleType: 'Interval', intervalSeconds: 3600, nextRunAtUtc: '2026-11-08T13:00:00Z' }],
      lastRun: { status: 'Succeeded', createdAtUtc: '2026-11-08T00:00:00Z' },
    })).toBe('Runs every hour. The last run succeeded 12 hours ago. Next run in 1 hour.')
  })

  it('says a tool without schedules runs when started and has not run', () => {
    expect(text({})).toBe('Runs when you start it. It has not run yet.')
  })

  it('says where a web app is live, who can open it and when it was deployed', () => {
    expect(text({ kind: 'web', host: 'lunch.example.com', audience: 'people', peopleCount: 6, lastDeployedAt: '2026-11-08T11:00:00Z' }))
      .toBe('Live at lunch.example.com. 6 people can open it. Last deployed 1 hour ago.')
    expect(text({ kind: 'site', host: 'a.example.com', audience: 'me', peopleCount: 0 })).toBe('Live at a.example.com. Only you can open it.')
  })

  it('says an API needs a key unless it is open to the internet', () => {
    expect(text({ kind: 'api', host: 'api.example.com', audience: 'server', peopleCount: 0 })).toBe('Live at api.example.com. Callers need an API key.')
  })

  it('says a web app is not live or that its deploy failed', () => {
    expect(text({ kind: 'web' })).toBe('Not live yet. Deploy it from your AI or with the CLI.')
    expect(text({ kind: 'web', failed: true, deployError: 'build broke' })).toBe('The last deploy failed: build broke')
  })
})

describe('access label', () => {
  it('names who can open the app', () => {
    expect(['internet', 'people', 'server', 'me'].map((audience) => accessLabel(audience as never)))
      .toEqual(['Public', 'People I choose', 'Everyone on this server', 'Only you'])
  })
})

describe('appOriginLabel', () => {
  const base = { developmentMode: 'local', runtimeSource: 'cli-deploy' } as const
  it('names the repository, folder and commit of a catalog install', () => {
    expect(appOriginLabel({ ...base, installedFromRepo: 'acme/apps', installedFromFolder: 'backup', installedFromCommit: '0123456789abcdef' })).toBe('Installed from acme/apps/backup @ 0123456')
  })
  it('falls back to fork, CLI and Studio wording', () => {
    expect(appOriginLabel({ ...base, forkedFromAppName: 'Todo' })).toBe('From the catalog · Todo')
    expect(appOriginLabel(base)).toBe('Deployed with the CLI')
    expect(appOriginLabel({ developmentMode: 'studio', runtimeSource: 'studio' })).toBe('Built in Studio')
  })
})

describe('secretsPill', () => {
  it('counts only required secrets as missing', () => {
    expect(secretsPill([{ name: 'A' }, { name: 'B', optional: true }], [])).toEqual({ text: '1 missing', bad: true })
  })

  it('says an unset optional secret is optional', () => {
    expect(secretsPill([{ name: 'A' }, { name: 'WEBHOOK_URL', optional: true }], ['A'])).toEqual({ text: 'optional, not set', bad: false })
  })

  it('says all set when nothing is unset', () => {
    expect(secretsPill([{ name: 'A' }], ['A'])).toEqual({ text: 'all set', bad: false })
  })
})
