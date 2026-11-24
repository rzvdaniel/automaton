import { describe, expect, it } from 'vitest'
import { buildAiReport, redactSecrets } from './ai-report'

describe('buildAiReport', () => {
  const base = { appName: 'Home', appId: 'a1', environment: 'Live', version: 'abc1234', what: 'Run home-card', at: '2026-11-24T17:13:00Z', error: 'Boom' }

  it('has the app, time, error, last 40 log lines and the ask', () => {
    const logs = Array.from({ length: 60 }, (_, i) => `line ${i + 1}`).join('\n')
    const text = buildAiReport({ ...base, logs, settingNames: ['CITY'], secretNames: ['API_KEY'] })
    expect(text).toContain('Home (id a1)')
    expect(text).toContain('Environment: Live')
    expect(text).toContain('Version: abc1234')
    expect(text).toContain('Settings (names only): CITY')
    expect(text).toContain('line 60')
    expect(text).toContain('line 21')
    expect(text).not.toContain('line 20\n')
    expect(text).toContain('Find the cause and fix it, then deploy and check it runs.')
  })

  it('hides secret-looking values in logs and errors', () => {
    const text = buildAiReport({ ...base, error: 'failed with password=hunter2', logs: 'API_KEY=sk-123456\nAuthorization: Bearer abcdefghijkl\nurl postgres://u:pw123@db/x\nok' })
    for (const leaked of ['hunter2', 'sk-123456', 'abcdefghijkl', 'pw123']) expect(text).not.toContain(leaked)
    expect(text).toContain('API_KEY=[hidden]')
    expect(text).toContain('ok')
  })

  it('leaves ordinary text alone', () => {
    expect(redactSecrets('Connected to 3 hosts')).toBe('Connected to 3 hosts')
  })
})
