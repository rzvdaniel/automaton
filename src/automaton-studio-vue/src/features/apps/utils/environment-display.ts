import type { AppDeploymentTarget } from '../types/apps.types'

/** A legacy placeholder that is safe to hide once the explicit repair path has run. */
export function isRemovableLegacyDefault(target: AppDeploymentTarget): boolean {
  const name = target.name.trim().toLowerCase()
  const slug = target.slug.trim().toLowerCase()
  return !target.isDefault &&
    !target.runner &&
    !target.runtimeDeployed &&
    !target.lastDeploymentAt &&
    !target.lastSuccessfulDeploymentAt &&
    (target.status === 'Unassigned' || target.status === 'NotDeployed') &&
    (name === 'default' || slug === 'default')
}

export function getEnvironmentDisplayName(target?: AppDeploymentTarget | null): string {
  if (!target) return 'Live'
  if (target.isDefault) return 'Live'
  const name = target.name.trim()
  if (name && name.toLowerCase() !== target.slug.trim().toLowerCase()) return name
  const parts = target.slug.split('-').filter(Boolean)
  const suffix = parts[parts.length - 1]
  return suffix ? suffix.charAt(0).toUpperCase() + suffix.slice(1) : 'Environment'
}
