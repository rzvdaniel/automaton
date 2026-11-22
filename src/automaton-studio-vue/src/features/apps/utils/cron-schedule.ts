/**
 * Cron is the only stored timing format. The schedule form's "every" and "daily at" choices only build a
 * standard five-field cron expression (minute hour day-of-month month day-of-week); this reads one back into
 * the friendly choice when it fits, and lists the next run times for the "Next runs" line.
 */
export type TimingMode = 'every' | 'daily' | 'cron'
export type EveryUnit = 'minutes' | 'hours'

export interface Timing {
  mode: TimingMode
  count: number
  unit: EveryUnit
  /** HH:mm, for "daily at". */
  time: string
  cron: string
}

const RANGES: ReadonlyArray<readonly [number, number]> = [[0, 59], [0, 23], [1, 31], [1, 12], [0, 7]]
const pad = (n: number) => String(n).padStart(2, '0')

function parseField(field: string, [min, max]: readonly [number, number]): Set<number> | null {
  const values = new Set<number>()
  for (const part of field.split(',')) {
    const match = /^(?:\*|(\d+)(?:-(\d+))?)(?:\/(\d+))?$/.exec(part)
    if (!match) return null
    const step = match[3] === undefined ? 1 : Number(match[3])
    if (step < 1) return null
    let from = min
    let to = max
    if (match[1] !== undefined) {
      from = Number(match[1])
      to = match[2] !== undefined ? Number(match[2]) : match[3] !== undefined ? max : from
    }
    if (from < min || to > max || from > to) return null
    for (let v = from; v <= to; v += step) values.add(v)
  }
  return values
}

export interface ParsedCron { minute: Set<number>; hour: Set<number>; dayOfMonth: Set<number>; month: Set<number>; dayOfWeek: Set<number>; anyDay: boolean; anyWeekday: boolean }

/** Null when the text is not a standard five-field cron expression. */
export function parseCron(expression: string): ParsedCron | null {
  const fields = expression.trim().split(/\s+/)
  if (fields.length !== 5) return null
  const sets = fields.map((field, i) => parseField(field, RANGES[i]!))
  if (sets.some((set) => !set || set.size === 0)) return null
  const [minute, hour, dayOfMonth, month, weekday] = sets as Set<number>[]
  const dayOfWeek = new Set([...weekday!].map((d) => d % 7))
  return { minute: minute!, hour: hour!, dayOfMonth: dayOfMonth!, month: month!, dayOfWeek, anyDay: fields[2] === '*', anyWeekday: fields[4] === '*' }
}

export function isValidCron(expression: string): boolean { return parseCron(expression) !== null }

function offsetMinutes(at: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone, hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric' }).formatToParts(new Date(at))
  const get = (type: string) => Number(parts.find((p) => p.type === type)!.value)
  return Math.round((Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute'), get('second')) - at) / 60000)
}

/** The next run times of a cron expression in a time zone, after `from`. Empty when none fits within two years. */
export function nextRuns(expression: string, timeZone: string, count = 3, from: Date = new Date()): Date[] {
  const cron = parseCron(expression)
  if (!cron) return []
  const runs: Date[] = []
  const hourMs = 3600_000
  let hourStart = Math.floor(from.getTime() / hourMs) * hourMs
  for (let i = 0; i < 24 * 731 && runs.length < count; i++, hourStart += hourMs) {
    let offset: number
    try { offset = offsetMinutes(hourStart, timeZone || 'UTC') } catch { return [] }
    const local = new Date(hourStart + offset * 60000)
    const monthOk = cron.month.has(local.getUTCMonth() + 1)
    const domOk = cron.dayOfMonth.has(local.getUTCDate())
    const dowOk = cron.dayOfWeek.has(local.getUTCDay())
    const dayOk = cron.anyDay || cron.anyWeekday ? domOk && dowOk : domOk || dowOk
    if (!monthOk || !dayOk || !cron.hour.has(local.getUTCHours())) continue
    for (const minute of [...cron.minute].sort((a, b) => a - b)) {
      const at = hourStart + (minute - local.getUTCMinutes()) * 60000
      if (at > from.getTime() && at >= hourStart) { runs.push(new Date(at)); if (runs.length === count) break }
    }
  }
  return runs
}

/** The expression the "every" and "daily at" choices stand for; the cron text itself for "cron". */
export function buildCron(timing: Timing): string {
  if (timing.mode === 'cron') return timing.cron.trim()
  if (timing.mode === 'daily') {
    const [h, m] = timing.time.split(':').map(Number)
    return `${m} ${h} * * *`
  }
  if (timing.unit === 'hours') return timing.count === 1 ? '0 * * * *' : `0 */${timing.count} * * *`
  return timing.count === 1 ? '* * * * *' : `*/${timing.count} * * * *`
}

/** Null when the choice is complete and fits; otherwise the reason, in the words the form shows. */
export function timingProblem(timing: Timing): string | null {
  if (timing.mode === 'every') {
    const max = timing.unit === 'hours' ? 23 : 59
    if (!Number.isInteger(timing.count) || timing.count < 1 || timing.count > max) return `Enter a whole number from 1 to ${max} ${timing.unit}.`
  }
  if (timing.mode === 'daily' && !/^\d{1,2}:\d{2}$/.test(timing.time)) return 'Pick the time of day.'
  if (timing.mode === 'cron' && !isValidCron(timing.cron)) return 'This is not a cron expression: five fields, minute hour day-of-month month day-of-week.'
  return null
}

/** Reads a stored cron expression back into a friendly choice when it fits one, else opens on "cron". */
export function timingFromCron(expression: string): Timing {
  const cron = expression.trim()
  const base: Timing = { mode: 'cron', count: 15, unit: 'minutes', time: '02:00', cron }
  const f = cron.split(/\s+/)
  if (f.length !== 5) return base
  const [minute, hour, dom, month, dow] = f as [string, string, string, string, string]
  if (dom !== '*' || month !== '*' || dow !== '*') return base
  const everyMinutes = /^\*\/(\d{1,2})$/.exec(minute)
  if (hour === '*' && everyMinutes && Number(everyMinutes[1]) >= 2 && Number(everyMinutes[1]) <= 59) return { ...base, mode: 'every', count: Number(everyMinutes[1]), unit: 'minutes' }
  if (hour === '*' && minute === '*') return { ...base, mode: 'every', count: 1, unit: 'minutes' }
  if (minute === '0') {
    if (hour === '*') return { ...base, mode: 'every', count: 1, unit: 'hours' }
    const everyHours = /^\*\/(\d{1,2})$/.exec(hour)
    if (everyHours && Number(everyHours[1]) >= 2 && Number(everyHours[1]) <= 23) return { ...base, mode: 'every', count: Number(everyHours[1]), unit: 'hours' }
  }
  if (/^\d{1,2}$/.test(minute) && /^\d{1,2}$/.test(hour) && Number(minute) <= 59 && Number(hour) <= 23) {
    return { ...base, mode: 'daily', time: `${pad(Number(hour))}:${pad(Number(minute))}` }
  }
  return base
}

/** The same choice for a schedule stored as a fixed interval (older schedules); saving turns it into cron. */
export function timingFromInterval(seconds: number): Timing {
  const base: Timing = { mode: 'every', count: 15, unit: 'minutes', time: '02:00', cron: '' }
  if (seconds >= 3600 && seconds % 3600 === 0 && seconds / 3600 <= 23) return { ...base, count: seconds / 3600, unit: 'hours' }
  if (seconds >= 60 && seconds % 60 === 0 && seconds / 60 <= 59) return { ...base, count: seconds / 60 }
  return { ...base, mode: 'cron', cron: '0 2 * * *' }
}

/** "11:15, 11:30 and 11:45 UTC"; with a date when a run is not today in that zone. */
export function formatNextRuns(runs: ReadonlyArray<Date>, timeZone: string, now: Date = new Date()): string {
  const zone = timeZone || 'UTC'
  const day = (d: Date) => new Intl.DateTimeFormat('en-CA', { timeZone: zone }).format(d)
  const text = runs.map((run) => {
    const time = new Intl.DateTimeFormat('en-GB', { timeZone: zone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(run)
    if (day(run) === day(now)) return time
    return `${new Intl.DateTimeFormat('en-GB', { timeZone: zone, weekday: 'short', day: 'numeric', month: 'short' }).format(run)} ${time}`
  })
  const list = text.length > 1 ? `${text.slice(0, -1).join(', ')} and ${text[text.length - 1]}` : text.join('')
  return `${list} ${zone === 'UTC' ? 'UTC' : zone}`
}
