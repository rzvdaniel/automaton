import { describe, expect, it } from 'vitest'
import { ApiKeyScope, ApiKeySource, getScopeDisplayName, getScopeSeverity, getSourceDisplayName } from './apikeys.types'

describe('API key scope names', () => {
  it.each([
    ['ReadOnly', 'Read Only'],
    ['WorkflowExecution', 'Workflow Execution'],
    ['Runner', 'Runner'],
    ['FullAccess', 'Full Access'],
    ['WebApp', 'Web App'],
    ['Developer', 'Developer / CLI'],
    ['PermissionsAdmin', 'Admin: approve app permissions'],
  ])('names %s as sent by the API', (name, label) => {
    expect(getScopeDisplayName(name)).toBe(label)
  })

  it('still names numeric scopes and marks unknown ones', () => {
    expect(getScopeDisplayName(ApiKeyScope.Developer)).toBe('Developer / CLI')
    expect(getScopeDisplayName('Nope')).toBe('Unknown')
    expect(getScopeSeverity('PermissionsAdmin')).toBe('danger')
  })
})

describe('API key source names', () => {
  it.each([
    ['Manual', 'Manual'],
    ['CliAuthorization', 'Automaton CLI authorization'],
    ['RunnerProvisioning', 'Runner provisioning'],
    [ApiKeySource.CliAuthorization, 'Automaton CLI authorization'],
    ['Nope', 'Unknown'],
  ])('names %s', (source, label) => {
    expect(getSourceDisplayName(source)).toBe(label)
  })
})
