import { describe, expect, it } from 'vitest'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import { getViewingTargetPreviewState, resolveViewingTarget } from './viewing-target'

function target(overrides: Partial<AppDeploymentTarget> = {}): AppDeploymentTarget {
  return {
    id: 'live-id', appId: 'app-id', name: 'Live', slug: 'live', isDefault: true,
    status: DeploymentTargetStatus.Running, genericRuntimeDeployed: true,
    cleanupPending: false, routePath: '/',
    runner: { id: 'runner-id', name: 'Hosted', purpose: 'Application', isHostedRunner: true, isOnline: true },
    ...overrides,
  }
}

describe('viewing targets', () => {
  it('selects a target by its public slug and falls back to Live for an invalid slug', () => {
    const live = target()
    const canary = target({ id: 'canary-id', name: 'Canary', slug: 'privileged-canary', isDefault: false })

    expect(resolveViewingTarget([live, canary], 'privileged-canary')).toEqual({ target: canary, usedFallback: false })
    expect(resolveViewingTarget([live, canary], 'removed-target')).toEqual({ target: live, usedFallback: true })
  })

  it.each([
    [target({ runner: null, status: DeploymentTargetStatus.Unassigned }), 'No runtime assigned'],
    [target({ genericRuntimeDeployed: false, status: DeploymentTargetStatus.NotDeployed }), 'Not deployed'],
    [target({ runner: { id: 'runner-id', name: 'Offline', purpose: 'Application', isHostedRunner: false, isOnline: false }, status: DeploymentTargetStatus.RunnerOffline }), 'Runtime is offline'],
  ])('does not create a preview for unavailable targets', (unavailableTarget, message) => {
    expect(getViewingTargetPreviewState(unavailableTarget)).toEqual({ available: false, message })
  })
})
