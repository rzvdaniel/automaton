import { describe, expect, it } from 'vitest'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import { getDeployableTargets, getLiveTarget, getDeployTargetOptionLabel, resolveStudioDeployAction } from './deployment-target-actions'

function target(overrides: Partial<AppDeploymentTarget> = {}): AppDeploymentTarget {
  return {
    id: 'live-id', appId: 'app-id', name: 'Live', slug: 'hosted-control', isDefault: true,
    status: DeploymentTargetStatus.Running, genericRuntimeDeployed: true,
    cleanupPending: false, routePath: '/',
    runner: { id: 'runner-id', name: 'Runner', purpose: 'Application', isHostedRunner: true, isOnline: true },
    ...overrides,
  }
}

describe('deployment target actions', () => {
  it('deploys immediately only when exactly one target is deployable', () => {
    const live = target()
    const canary = target({ id: 'canary-id', name: 'Canary', slug: 'privileged-canary', isDefault: false })
    const offline = target({ id: 'offline-id', runner: { ...live.runner!, isOnline: false }, isDefault: false })

    expect(getDeployableTargets([live])).toEqual([live])
    expect(getDeployableTargets([live, canary])).toEqual([live, canary])
    expect(getDeployableTargets([live, offline])).toEqual([live])
  })

  it('always resolves publishing context to Live, not the viewed canary', () => {
    const live = target()
    const canary = target({ id: 'canary-id', name: 'Canary', slug: 'privileged-canary', isDefault: false })

    expect(getLiveTarget([live, canary])).toBe(live)
    expect(getLiveTarget([canary, live])).toBe(live)
  })

  it('identifies the viewed target without selecting it', () => {
    const canary = target({ id: 'canary-id', name: 'Canary', slug: 'privileged-canary', isDefault: false, runner: { ...target().runner!, isHostedRunner: false, applicationProfile: 'PrivilegedHost' } })
    expect(getDeployTargetOptionLabel(canary, 'canary-id')).toBe('Canary · Additional · Retired self-hosted destination · Running · Viewing')
  })

  it('always uses CLI guidance when Studio deployment is disabled', () => {
    expect(resolveStudioDeployAction(false, 0)).toBe('cli')
    expect(resolveStudioDeployAction(false, 1)).toBe('cli')
    expect(resolveStudioDeployAction(false, 3)).toBe('cli')
  })

  it('only inspects targets for a real Studio deployment capability', () => {
    expect(resolveStudioDeployAction(true, 0)).toBe('no-target')
    expect(resolveStudioDeployAction(true, 1)).toBe('deploy')
    expect(resolveStudioDeployAction(true, 2)).toBe('select-target')
  })
})
