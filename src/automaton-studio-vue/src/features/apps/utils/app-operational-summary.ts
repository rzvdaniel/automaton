import { DeploymentTargetStatus, type App, type AppDeploymentTarget } from '../types/apps.types'

export type AppOperationalStatus =
  | 'not-deployed'
  | 'operation-in-progress'
  | 'running'
  | 'actions-only'
  | 'degraded'
  | 'deployment-failed'
  | 'runtime-unavailable'
  | 'no-runtime-assigned'

export type AppOperationalSeverity = 'secondary' | 'info' | 'success' | 'warn' | 'danger'

export interface AppOperationalSummary {
  status: AppOperationalStatus
  label: string
  severity: AppOperationalSeverity
  hasDeployedRuntime: boolean
}

const statusDetails: Record<
  AppOperationalStatus,
  Pick<AppOperationalSummary, 'label' | 'severity'>
> = {
  'not-deployed': { label: 'Not deployed', severity: 'secondary' },
  'operation-in-progress': { label: 'Operation in progress', severity: 'info' },
  running: { label: 'Running', severity: 'success' },
  'actions-only': { label: 'Actions only', severity: 'success' },
  degraded: { label: 'Degraded', severity: 'warn' },
  'deployment-failed': { label: 'Deployment failed', severity: 'danger' },
  'runtime-unavailable': { label: 'Runtime unavailable', severity: 'warn' },
  'no-runtime-assigned': { label: 'No runtime assigned', severity: 'secondary' },
}

/** An app with no runtime command and no static site: it only has Actions. */
export function isActionOnlyApp(app?: Pick<App, 'application'> | null): boolean {
  const application = app?.application
  if (!application) return false
  return !application.runtime?.command && !application.isStaticSite && !application.compose
}

export function getAppOperationalSummary(
  target?: AppDeploymentTarget | null,
  actionOnly = false,
): AppOperationalSummary {
  if (!target) return { ...statusDetails['not-deployed'], status: 'not-deployed', hasDeployedRuntime: false }

  const hasDeployedRuntime = target.genericRuntimeDeployed
  const hasRunner = !!target.runner && target.status !== DeploymentTargetStatus.Unassigned
  const runnerUnavailable =
    target.status === DeploymentTargetStatus.RunnerOffline ||
    (!!target.runner && !target.runner.isOnline)

  const healthyActionOnly =
    actionOnly &&
    !target.cleanupPending &&
    target.status !== DeploymentTargetStatus.Deploying &&
    target.status !== DeploymentTargetStatus.Failed &&
    !runnerUnavailable
  if (healthyActionOnly) {
    return { ...statusDetails['actions-only'], status: 'actions-only', hasDeployedRuntime: false }
  }

  let status: AppOperationalStatus
  if (target.cleanupPending) status = 'degraded'
  else if (target.status === DeploymentTargetStatus.Deploying) status = 'operation-in-progress'
  else if (target.status === DeploymentTargetStatus.Failed) status = 'deployment-failed'
  else if (!hasRunner) status = 'no-runtime-assigned'
  else if (runnerUnavailable) status = 'runtime-unavailable'
  else if (target.status === DeploymentTargetStatus.Running && hasDeployedRuntime) status = 'running'
  else if (target.status === DeploymentTargetStatus.Running || hasDeployedRuntime) status = 'degraded'
  else status = 'not-deployed'

  return { ...statusDetails[status], status, hasDeployedRuntime }
}
