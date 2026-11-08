import { describe, expect, it } from 'vitest'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import { getAppOperationalSummary, isActionOnlyApp } from './app-operational-summary'
import { getAppHealth } from './app-overview'

function target(overrides: Partial<AppDeploymentTarget> = {}): AppDeploymentTarget {
  return {
    id: 'live-id',
    appId: 'app-id',
    name: 'Live',
    slug: 'live',
    isDefault: true,
    status: DeploymentTargetStatus.NotDeployed,
    genericRuntimeDeployed: false,
    lastSuccessfulDeploymentAt: null,
    cleanupPending: false,
    routePath: '/',
    runner: { id: 'runner-id', name: 'Runner', purpose: 'Application', isHostedRunner: true, isOnline: true },
    ...overrides,
  }
}

describe('getAppOperationalSummary', () => {
  it('has no target: not deployed', () => {
    expect(getAppOperationalSummary(null).status).toBe('not-deployed')
  })

  it('shows an undeployed target as not deployed', () => {
    expect(getAppOperationalSummary(target())).toMatchObject({ status: 'not-deployed', label: 'Not deployed' })
  })

  it('shows a deployed online target as running', () => {
    expect(getAppOperationalSummary(target({ status: DeploymentTargetStatus.Running, genericRuntimeDeployed: true })))
      .toMatchObject({ status: 'running', hasDeployedRuntime: true })
  })

  it('treats a deployed runtime on an online Runner as running even when the target says NotDeployed', () => {
    const summary = getAppOperationalSummary(target({ status: DeploymentTargetStatus.NotDeployed, genericRuntimeDeployed: true }))
    expect(summary.status).toBe('running')
    expect(getAppHealth(summary)).toBe('healthy')
    expect(getAppHealth(getAppOperationalSummary(target({ status: DeploymentTargetStatus.Running })))).toBe('failing')
  })

  it('reports a failed deployment, an offline Runner and a missing Runner', () => {
    expect(getAppOperationalSummary(target({ status: DeploymentTargetStatus.Failed })).status).toBe('deployment-failed')
    expect(getAppOperationalSummary(target({ status: DeploymentTargetStatus.RunnerOffline })).status).toBe('runtime-unavailable')
    expect(getAppOperationalSummary(target({ runner: null, status: DeploymentTargetStatus.Unassigned })).status).toBe('no-runtime-assigned')
  })

  it('treats a healthy app with only Actions as actions-only', () => {
    expect(getAppOperationalSummary(target(), true).status).toBe('actions-only')
    expect(getAppOperationalSummary(target({ status: DeploymentTargetStatus.Failed }), true).status).toBe('deployment-failed')
    expect(getAppOperationalSummary(target({ status: DeploymentTargetStatus.RunnerOffline }), true).status).toBe('runtime-unavailable')
  })
})

describe('isActionOnlyApp', () => {
  const contract = (application: Record<string, unknown>) => ({ application } as never)
  it('is true without a runtime command, static site or compose', () => {
    expect(isActionOnlyApp(contract({ runtime: { command: null } }))).toBe(true)
    expect(isActionOnlyApp(contract({ runtime: { command: { executable: 'node' } } }))).toBe(false)
    expect(isActionOnlyApp(contract({ isStaticSite: true }))).toBe(false)
  })
})
