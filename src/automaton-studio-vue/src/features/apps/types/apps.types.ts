/**
 * Apps feature types
 * TypeScript interfaces for apps data structures
 */

/**
 * GitHub sync status
 */
export type GitHubSyncStatus = 'None' | 'Synced' | 'LocalChanges' | 'RemoteChanges' | 'Conflict'

/**
 * GitHub settings for an app
 */
export interface GitHubSettings {
  repositoryUrl?: string
  branch?: string
  path?: string
  syncEnabled: boolean
  lastSyncedCommitSha?: string
  lastSyncedAt?: string // ISO date string
  syncStatus: GitHubSyncStatus
}

/**
 * App appearance configuration
 */
export interface AppAppearance {
  backgroundColor?: string
  backgroundImageUrl?: string
  showBackgroundImage: boolean
}

/**
 * App definition containing the content
 */
export interface AppDefinition {
  htmlContent?: string
  version: number
  gitHub?: GitHubSettings
}

/**
 * App model from API
 * Matches src/Automaton.Studio.Server/Models/App.cs
 */
export interface App {
  id: string
  name: string
  displayName: string
  description: string
  isPublished: boolean
  publicUrl?: string
  publishedAt?: string // ISO date string
  version: number
  requiresAuthentication: boolean
  created: string // ISO date string
  updated: string // ISO date string
  executionRunnerId?: string
  appearance: AppAppearance
  definition: AppDefinition
}

/**
 * Request to create a new app
 * Matches src/Automaton.Studio.Server/Models/NewApp.cs
 */
export interface CreateAppRequest {
  name: string
  executionRunnerId?: string
}

/**
 * Request to publish an app
 */
export interface PublishAppRequest {
  publicUrl: string
  requiresAuthentication: boolean
  executionRunnerId?: string
}

/**
 * App list filters for UI
 */
export interface AppListFilters {
  search: string
  status: 'all' | 'published' | 'draft'
  projectId?: string
}

/**
 * Linked step info for apps
 */
export interface LinkedStepInfo {
  id: string
  name: string
  displayName: string
  description: string
  language: string
  isLinked: boolean
}

/**
 * App file info
 */
export interface AppFile {
  path: string
  name: string
  size: number
  lastModified: string
}

/**
 * App statistics for display
 */
export interface AppStats {
  fileCount: number
  backendStepCount: number
}
