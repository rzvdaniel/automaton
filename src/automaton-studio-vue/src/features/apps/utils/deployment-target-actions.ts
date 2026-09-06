import type { AppDeploymentTarget } from '../types/apps.types'

export function getDeployTargetOptionLabel(target: AppDeploymentTarget, viewingTargetId?: string): string {
  const destination = target.runner?.isHostedRunner
    ? 'Automaton Hosted'
    : target.runner?.applicationProfile === 'PrivilegedHost'
      ? 'Self-hosted · Privileged Host'
      : 'Self-hosted · Standard'
  const viewing = target.id === viewingTargetId ? ' · Viewing' : ''
  return `${target.name} · ${target.isDefault ? 'Live' : 'Additional'} · ${destination} · ${target.status}${viewing}`
}

export function getDeployableTargets(targets: AppDeploymentTarget[]): AppDeploymentTarget[] {
  return targets.filter((target) =>
    !!target.runner && target.runner.isOnline && target.status !== 'Deploying',
  )
}

export type StudioDeployAction = 'cli' | 'deploy' | 'select-target' | 'no-target'

export function resolveStudioDeployAction(canDeployFromStudio: boolean, deployableTargetCount: number): StudioDeployAction {
  if (!canDeployFromStudio) return 'cli'
  if (deployableTargetCount === 1) return 'deploy'
  if (deployableTargetCount > 1) return 'select-target'
  return 'no-target'
}

export function getLiveTarget(targets: AppDeploymentTarget[]): AppDeploymentTarget | null {
  return targets.find((target) => target.isDefault) ?? null
}
