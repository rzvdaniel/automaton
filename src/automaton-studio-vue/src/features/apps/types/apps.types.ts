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
 * App appearance configuration for card display
 * Matches backend AppAppearance.cs
 */
export interface AppAppearance {
  backgroundColor?: string
  backgroundImageUrl?: string
  backgroundImageOpacity?: number
  titleFontColor?: string
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
  code?: string // Step code for editing in the IDE
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

// ============================================
// App Editor Types (Phase 3.3b - Visual IDE)
// ============================================

/**
 * File tree node for displaying in file explorer
 */
export interface FileNode {
  key: string
  name: string
  path: string
  type: 'file' | 'folder'
  extension?: string
  icon?: string
  children?: FileNode[]
  isOpen?: boolean
}

/**
 * Open file in editor with content and state
 */
export interface OpenFile {
  path: string
  content: string
  language: string // 'html', 'css', 'javascript', 'json', etc.
  isDirty: boolean // Has unsaved changes
  originalContent: string // For detecting changes
  isBackendStep?: boolean // True if this is a backend step file
  stepId?: string // Backend step ID if isBackendStep is true
  stepName?: string // Backend step name for display
}

/**
 * Step variable definition
 */
export interface StepVariable {
  name: string
  description?: string
  type?: string
  isSensitive: boolean
}

/**
 * Extended linked step info with variable details
 */
export interface LinkedStepInfoExtended extends LinkedStepInfo {
  inputCount: number
  outputCount: number
  hasInputVariables: boolean
  hasOutputVariables: boolean
  inputVariables: StepVariable[]
  outputVariables: StepVariable[]
}

/**
 * Preview mode for responsive testing
 */
export type PreviewMode = 'desktop' | 'tablet' | 'mobile'

/**
 * Preview viewport sizes
 */
export const PREVIEW_SIZES: Record<PreviewMode, { width: string; height: string }> = {
  desktop: { width: '100%', height: '100%' },
  tablet: { width: '768px', height: '1024px' },
  mobile: { width: '375px', height: '667px' },
}

/**
 * Create file request
 */
export interface CreateFileRequest {
  path: string
  content: string
}

/**
 * Create step for app request
 */
export interface CreateAppStepRequest {
  name: string
  language?: string
  description?: string
}

/**
 * File API response
 */
export interface FileApiResponse {
  success: boolean
  message?: string
  files?: string[]
  content?: string
  path?: string
}

/**
 * Get language from file extension
 */
export function getLanguageFromExtension(filePath: string): string {
  const ext = filePath.split('.').pop()?.toLowerCase()
  switch (ext) {
    case 'html':
    case 'htm':
      return 'html'
    case 'css':
      return 'css'
    case 'js':
      return 'javascript'
    case 'ts':
      return 'typescript'
    case 'json':
      return 'json'
    case 'md':
      return 'markdown'
    case 'xml':
    case 'svg':
      return 'xml'
    case 'py':
      return 'python'
    case 'ps1':
      return 'powershell'
    case 'sh':
    case 'bash':
      return 'shell'
    default:
      return 'plaintext'
  }
}

/**
 * Get file icon based on extension
 */
export function getFileIcon(filePath: string): string {
  const ext = filePath.split('.').pop()?.toLowerCase()
  switch (ext) {
    case 'html':
    case 'htm':
      return 'pi pi-file'
    case 'css':
      return 'pi pi-palette'
    case 'js':
    case 'ts':
      return 'pi pi-code'
    case 'json':
      return 'pi pi-file'
    case 'md':
      return 'pi pi-file'
    case 'svg':
    case 'png':
    case 'jpg':
    case 'jpeg':
    case 'gif':
    case 'webp':
      return 'pi pi-image'
    default:
      return 'pi pi-file'
  }
}
