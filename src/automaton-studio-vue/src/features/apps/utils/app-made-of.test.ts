import { describe, expect, it } from 'vitest'
import { getMadeOf } from './app-made-of'
import type { App } from '../types/apps.types'

function app(overrides: Record<string, unknown>, extra: Partial<App> = {}) {
  return {
    developmentMode: 'studio',
    runtimeSource: 'studio',
    forkedFromAppName: null,
    application: {
      runtime: { command: null, port: 0 },
      storage: { dataDirectory: '', mounts: [] },
      secrets: [],
      ...overrides,
    },
    ...extra,
  } as unknown as App
}

const byKey = (items: ReturnType<typeof getMadeOf>) => Object.fromEntries(items.map((item) => [item.key, item.value]))

describe('getMadeOf', () => {
  it('describes a web app with data and secrets', () => {
    const result = byKey(getMadeOf(app({
      runtime: { command: { executable: 'node' }, port: 3000 },
      storage: { dataDirectory: '/data', mounts: [{ name: 'db', relativePath: 'db', readOnly: false }] },
      secrets: [{ name: 'KEY' }, { name: 'TOKEN' }],
    })))
    expect(result).toEqual({ runs: 'Web app', source: 'Studio', data: '1 folder', secrets: '2 secrets', backup: 'Not scheduled' })
  })

  it('describes a static catalog app', () => {
    const result = byKey(getMadeOf(app({ isStaticSite: true }, { runtimeSource: 'cli-deploy' })))
    expect(result).toMatchObject({ runs: 'Static site', source: 'Local project', data: 'None', secrets: 'None', backup: 'Nothing to back up' })
  })

  it('describes an action', () => {
    const result = byKey(getMadeOf(app({}, { forkedFromAppName: 'Digest' })))
    expect(result).toMatchObject({ runs: 'Actions only', source: 'App store', data: 'None', backup: 'Nothing to back up' })
  })
})
