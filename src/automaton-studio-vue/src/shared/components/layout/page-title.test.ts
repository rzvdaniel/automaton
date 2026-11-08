import { describe, expect, it } from 'vitest'
import { pageTitleForRoute } from './page-title'

describe('pageTitleForRoute', () => {
  it('uses the mapped title', () => {
    expect(pageTitleForRoute('cli-authorize')).toBe('Authorize CLI')
    expect(pageTitleForRoute('apikeys')).toBe('Settings')
  })

  it('turns unmapped route names into title-case words', () => {
    expect(pageTitleForRoute('organization-members')).toBe('Organization Members')
    expect(pageTitleForRoute('organization-settings')).toBe('Organization Settings')
  })

  it('prefers the title declared in route meta', () => {
    expect(pageTitleForRoute('admin-logs', 'Audit Events')).toBe('Audit Events')
  })

  it('falls back to Apps without a route name', () => {
    expect(pageTitleForRoute(undefined)).toBe('Apps')
  })
})
