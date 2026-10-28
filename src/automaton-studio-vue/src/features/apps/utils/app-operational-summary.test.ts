import { describe, expect, it } from 'vitest'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import { getAppOperationalSummary } from './app-operational-summary'

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
    runner: {
      id: 'runner-id',
      name: 'Runner',
      purpose: 'Application',
      isHostedRunner: true,
      isOnline: true,
    },
    ...overrides,
  }
}

describe('getAppOperationalSummary', () => {
  it('shows an undeployed target as the primary CLI next step', () => {
    expect(getAppOperationalSummary(target())).toMatchObject({
      status: 'not-deployed',
      label: 'Not deployed',
      deploymentLabel: 'Not deployed',
      runtimeLabel: 'Not deployed',
      cliGuidance: 'primary',
      diagnostic: null,
    })
  })

  it('shows a deployed online target as running without claiming health', () => {
    expect(getAppOperationalSummary(target({
      status: DeploymentTargetStatus.Running,
      genericRuntimeDeployed: true,
    }))).toMatchObject({
      status: 'running',
      label: 'Running',
      deploymentLabel: 'Deployed',
      runtimeLabel: 'Running',
      cliGuidance: 'secondary',
      diagnostic: null,
    })
  })

  it('surfaces deployment failure with direct deployment and logs destinations', () => {
    expect(getAppOperationalSummary(target({
      status: DeploymentTargetStatus.Failed,
      lastDeploymentAt: '2026-09-16T10:00:00Z',
    }))).toMatchObject({
      status: 'deployment-failed',
      deploymentLabel: 'Last deployment failed',
      diagnostic: {
        title: 'The latest deployment failed',
        action: { label: 'View deployments', view: 'deployments' },
        secondaryAction: { label: 'View logs', view: 'logs' },
      },
    })
  })

  it('shows the useful deployment failure reason when the API provides one', () => {
    expect(getAppOperationalSummary(target({
      status: DeploymentTargetStatus.Failed,
      lastDeploymentError: 'Machine storage is below the configured 1024 MB free-space reserve.',
    }))).toMatchObject({
      diagnostic: {
        detail: 'Machine storage is below the configured 1024 MB free-space reserve.',
      },
    })
  })

  it('keeps the prior deployment fact separate from a failed redeploy', () => {
    expect(getAppOperationalSummary(target({
      status: DeploymentTargetStatus.Failed,
      genericRuntimeDeployed: true,
    }))).toMatchObject({
      status: 'deployment-failed',
      hasDeployedRuntime: true,
      runtimeLabel: 'Runtime deployment recorded; current health is not reported',
    })
  })

  it('surfaces an offline runtime with a Runtime destination', () => {
    expect(getAppOperationalSummary(target({
      status: DeploymentTargetStatus.RunnerOffline,
      genericRuntimeDeployed: true,
      runner: { ...target().runner!, isOnline: false },
    }))).toMatchObject({
      status: 'runtime-unavailable',
      label: 'Runtime unavailable',
      diagnostic: { action: { label: 'View runtime', view: 'runtime' } },
    })
  })

  it('surfaces an unassigned runtime with a Deployments destination', () => {
    expect(getAppOperationalSummary(target({
      status: DeploymentTargetStatus.Unassigned,
      runner: null,
    }))).toMatchObject({
      status: 'no-runtime-assigned',
      label: 'No runtime assigned',
      diagnostic: { action: { label: 'View deployments', view: 'deployments' } },
      cliGuidance: 'hidden',
    })
  })

  it('surfaces pending cleanup as degraded', () => {
    expect(getAppOperationalSummary(target({
      status: DeploymentTargetStatus.Running,
      genericRuntimeDeployed: true,
      cleanupPending: true,
    }))).toMatchObject({
      status: 'degraded',
      label: 'Degraded',
      diagnostic: { title: 'Runtime cleanup needs attention', action: { view: 'deployments' } },
    })
  })

  it('reports an in-progress target operation without assuming a deployment is running', () => {
    expect(getAppOperationalSummary(target({ status: DeploymentTargetStatus.Deploying }))).toMatchObject({
      status: 'operation-in-progress',
      label: 'Operation in progress',
      deploymentLabel: 'Operation in progress',
    })
  })

  it('reports conflicting runtime facts instead of collapsing them into Running', () => {
    expect(getAppOperationalSummary(target({ status: DeploymentTargetStatus.Running }))).toMatchObject({
      status: 'degraded',
      runtimeLabel: 'Running status; no deployed runtime is recorded',
      diagnostic: { title: 'Deployment status needs review' },
    })
  })

  it('asks for a target before deployment when none is available', () => {
    expect(getAppOperationalSummary(null)).toMatchObject({
      status: 'not-deployed',
      deploymentLabel: 'No Live deployment target',
      diagnostic: { action: { view: 'deployments' } },
      cliGuidance: 'hidden',
    })
  })
})
