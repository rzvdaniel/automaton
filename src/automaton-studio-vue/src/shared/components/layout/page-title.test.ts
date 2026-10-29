import { describe, expect, it } from 'vitest'
import { pageTitleForRoute } from './page-title'

describe('pageTitleForRoute', () => {
  it('uses the mapped title', () => {
    expect(pageTitleForRoute('cli-authorize')).toBe('Authorize CLI')
    expect(pageTitleForRoute('apikeys')).toBe('API Keys')
  })

  it('turns unmapped route names into title-case words', () => {
    expect(pageTitleForRoute('organization-members')).toBe('Organization Members')
    expect(pageTitleForRoute('billing')).toBe('Billing')
  })

  it('falls back to Dashboard without a route name', () => {
    expect(pageTitleForRoute(undefined)).toBe('Dashboard')
  })
})
