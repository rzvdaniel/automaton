/**
 * Public Apps feature types
 * TypeScript interfaces for public user profile and apps
 */

/**
 * Public user profile information
 */
export interface PublicUserProfile {
  username: string
  displayName?: string
  avatarUrl?: string
  bio?: string
  publicAppCount: number
}

/**
 * Public app card for grid display
 */
export interface PublicAppCard {
  id: string
  name: string
  summary?: string
  description?: string
  thumbnailUrl?: string
  category?: string
  publishedAt?: string // ISO date string
  appUrl: string
  publicUrl?: string
  viewCount: number
  starCount: number
  isFeatured: boolean
  isStarredByCurrentUser: boolean
}

/**
 * Public app details (extended card with more info)
 */
export interface PublicAppDetails extends PublicAppCard {
  tags?: string // Comma-separated
}

/**
 * API response for public apps endpoint
 */
export interface PublicAppsResponse {
  profile: PublicUserProfile
  apps: PublicAppCard[]
}

/**
 * App categories for filtering/display
 */
export const APP_CATEGORIES = [
  'Business',
  'Productivity',
  'Entertainment',
  'Education',
  'Finance',
  'Health',
  'Social',
  'Utilities',
  'Other',
] as const

export type AppCategory = (typeof APP_CATEGORIES)[number]

/**
 * Format date for display
 */
export function formatPublishedDate(dateString?: string): string {
  if (!dateString) return ''
  const date = new Date(dateString)
  return date.toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

/**
 * Get initials from name for avatar fallback
 */
export function getInitials(name?: string): string {
  if (!name) return '?'
  return name
    .split(' ')
    .map((n) => n[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
}

/**
 * Parse tags string into array
 */
export function parseTags(tags?: string): string[] {
  if (!tags) return []
  return tags
    .split(',')
    .map((t) => t.trim())
    .filter((t) => t.length > 0)
}
