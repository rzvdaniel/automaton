export function utcToLocalInput(value?: string, offsetMinutes?: number): string {
  if (!value) return ''
  const date = new Date(value)
  const offsetMs = (offsetMinutes ?? date.getTimezoneOffset()) * 60_000
  return new Date(date.getTime() - offsetMs).toISOString().slice(0, 16)
}

export function localInputToUtc(value?: string, offsetMinutes?: number): string | undefined {
  if (!value) return undefined
  if (offsetMinutes === undefined) return new Date(value).toISOString()
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value)
  if (!match) throw new Error('Local schedule time is invalid.')
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const hour = Number(match[4])
  const minute = Number(match[5])
  return new Date(Date.UTC(year, month - 1, day, hour, minute) + offsetMinutes * 60_000).toISOString()
}
