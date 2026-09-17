import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import type { AppViewKey } from '../app-navigation'

export type AppOperationalStatus =
  | 'not-deployed'
  | 'operation-in-progress'
  | 'running'
  | 'degraded'
  | 'deployment-failed'
  | 'runtime-unavailable'
  | 'no-runtime-assigned'

export type AppOperationalSeverity = 'secondary' | 'info' | 'success' | 'warn' | 'danger'

export interface AppOperationalAction {
  label: string
  view: AppViewKey
}

export interface AppOperationalDiagnostic {
  title: string
  detail: string
  action: AppOperationalAction
  secondaryAction?: AppOperationalAction
}

export interface AppOperationalSummary {
  status: AppOperationalStatus
  label: string
  severity: AppOperationalSeverity
  deploymentLabel: string
  runtimeLabel: string
  hasDeployedRuntime: boolean
  cliGuidance: 'primary' | 'secondary' | 'hidden'
  diagnostic: AppOperationalDiagnostic | null
}

const statusDetails: Record<
  AppOperationalStatus,
  Pick<AppOperationalSummary, 'label' | 'severity'>
> = {
  'not-deployed': { label: 'Not deployed', severity: 'secondary' },
  'operation-in-progress': { label: 'Operation in progress', severity: 'info' },
  running: { label: 'Running', severity: 'success' },
  degraded: { label: 'Degraded', severity: 'warn' },
  'deployment-failed': { label: 'Deployment failed', severity: 'danger' },
  'runtime-unavailable': { label: 'Runtime unavailable', severity: 'warn' },
  'no-runtime-assigned': { label: 'No runtime assigned', severity: 'secondary' },
}

export function getAppOperationalSummary(
  target?: AppDeploymentTarget | null,
): AppOperationalSummary {
  if (!target) {
    return {
      ...statusDetails['not-deployed'],
      status: 'not-deployed',
      deploymentLabel: 'No Live deployment target',
      runtimeLabel: 'No runtime assigned',
      hasDeployedRuntime: false,
      cliGuidance: 'hidden',
      diagnostic: {
        title: 'No Live deployment target',
        detail: 'Create or assign a target from Deployments before deploying this app.',
        action: { label: 'View deployments', view: 'deployments' },
      },
    }
  }

  const hasDeployedRuntime = target.genericRuntimeDeployed
  const hasRunner = !!target.runner && target.status !== DeploymentTargetStatus.Unassigned
  const runnerUnavailable =
    target.status === DeploymentTargetStatus.RunnerOffline ||
    (!!target.runner && !target.runner.isOnline)

  let status: AppOperationalStatus
  if (target.cleanupPending) {
    status = 'degraded'
  } else if (target.status === DeploymentTargetStatus.Deploying) {
    status = 'operation-in-progress'
  } else if (target.status === DeploymentTargetStatus.Failed) {
    status = 'deployment-failed'
  } else if (!hasRunner) {
    status = 'no-runtime-assigned'
  } else if (runnerUnavailable) {
    status = 'runtime-unavailable'
  } else if (target.status === DeploymentTargetStatus.Running && hasDeployedRuntime) {
    status = 'running'
  } else if (target.status === DeploymentTargetStatus.Running || hasDeployedRuntime) {
    status = 'degraded'
  } else {
    status = 'not-deployed'
  }

  const deploymentLabel =
    target.status === DeploymentTargetStatus.Deploying
      ? 'Operation in progress'
      : target.status === DeploymentTargetStatus.Failed
        ? 'Last deployment failed'
        : hasDeployedRuntime && target.status === DeploymentTargetStatus.NotDeployed
          ? 'Runtime recorded; target status is not deployed'
          : hasDeployedRuntime
            ? 'Deployed'
            : 'Not deployed'

  const runtimeLabel = !hasRunner
    ? hasDeployedRuntime
      ? 'Runtime recorded without an assignment'
      : 'No runtime assigned'
    : runnerUnavailable
      ? 'Runner offline'
      : target.status === DeploymentTargetStatus.Deploying
        ? 'Operation in progress'
        : target.status === DeploymentTargetStatus.Failed
          ? hasDeployedRuntime
            ? 'Runtime deployment recorded; current health is not reported'
            : 'No deployed runtime'
          : target.status === DeploymentTargetStatus.Running && hasDeployedRuntime
            ? 'Running'
            : target.status === DeploymentTargetStatus.Running
              ? 'Running status; no deployed runtime is recorded'
              : hasDeployedRuntime
                ? `Runtime recorded; target status is ${target.status}`
                : 'Not deployed'

  let diagnostic: AppOperationalDiagnostic | null = null
  if (target.cleanupPending) {
    diagnostic = {
      title: 'Runtime cleanup needs attention',
      detail: 'This deployment target reports cleanup is still pending.',
      action: { label: 'View deployments', view: 'deployments' },
    }
  } else if (target.status === DeploymentTargetStatus.Failed) {
    diagnostic = {
      title: 'The latest deployment failed',
      detail: 'Review this target in Deployments and check Logs for recent activity.',
      action: { label: 'View deployments', view: 'deployments' },
      secondaryAction: { label: 'View logs', view: 'logs' },
    }
  } else if (!hasRunner) {
    diagnostic = {
      title: 'No runtime is assigned to this target',
      detail: 'Assign a runtime from Deployments before deploying the app.',
      action: { label: 'View deployments', view: 'deployments' },
    }
  } else if (runnerUnavailable) {
    diagnostic = {
      title: 'The assigned runtime is offline',
      detail: 'Runtime operations are unavailable until the assigned runtime reconnects.',
      action: { label: 'View runtime', view: 'runtime' },
      secondaryAction: { label: 'View logs', view: 'logs' },
    }
  } else if (status === 'degraded') {
    diagnostic = {
      title: 'Deployment status needs review',
      detail: `Studio reports ${target.status}, while the deployed-runtime record is ${hasDeployedRuntime ? 'present' : 'absent'}.`,
      action: { label: 'View deployments', view: 'deployments' },
    }
  }

  const cliGuidance =
    status === 'not-deployed'
      ? 'primary'
      : hasDeployedRuntime || status === 'deployment-failed'
        ? 'secondary'
        : 'hidden'

  return {
    ...statusDetails[status],
    status,
    deploymentLabel,
    runtimeLabel,
    hasDeployedRuntime,
    cliGuidance,
    diagnostic,
  }
}
