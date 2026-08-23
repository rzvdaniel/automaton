import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'

export function resolveViewingTarget(
  targets: AppDeploymentTarget[],
  requestedSlug?: string | null,
): { target: AppDeploymentTarget | null; usedFallback: boolean } {
  const requested = requestedSlug?.trim().toLowerCase()
  const target = targets.find((candidate) => candidate.slug === requested)
  if (target) return { target, usedFallback: false }

  return {
    target: targets.find((candidate) => candidate.isDefault) ?? targets[0] ?? null,
    usedFallback: true,
  }
}

export function getViewingTargetPreviewState(target?: AppDeploymentTarget | null): {
  available: boolean
  message: string
} {
  if (!target?.runner) return { available: false, message: 'No runtime assigned' }
  if (!target.runtimeDeployed) return { available: false, message: 'Not deployed' }
  if (!target.runner.isOnline || target.status === DeploymentTargetStatus.RunnerOffline) {
    return { available: false, message: 'Runtime is offline' }
  }

  return { available: true, message: '' }
}
