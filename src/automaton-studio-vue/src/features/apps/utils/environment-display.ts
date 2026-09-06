import type { AppDeploymentTarget } from '../types/apps.types'

export function getEnvironmentDisplayName(target?: AppDeploymentTarget | null): string {
  if (!target) return 'Live'
  if (target.isDefault) return 'Live'
  const name = target.name.trim()
  if (name && name.toLowerCase() !== target.slug.trim().toLowerCase()) return name
  const parts = target.slug.split('-').filter(Boolean)
  const suffix = parts[parts.length - 1]
  return suffix ? suffix.charAt(0).toUpperCase() + suffix.slice(1) : 'Environment'
}
