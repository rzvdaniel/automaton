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
 * App definition containing the content
 */
export interface AppDefinition {
  htmlContent?: string
  version: number
  gitHub?: GitHubSettings
}

export interface AppScreenshot {
  id: string
  imageUrl: string
  caption?: string | null
  sortOrder: number
}

/**
 * App model from API
 * Matches src/Automaton.Api/Models/App.cs
 */
export interface App {
  id: string
  userId?: string // Owner user ID (for file path resolution)
  name: string
  summary: string
  description: string
  isPublished: boolean
  isForkable: boolean
  publicUrl?: string
  publishedAt?: string // ISO date string
  version: number
  requiresAuthentication: boolean
  created: string // ISO date string
  updated: string // ISO date string
  executionRunnerId?: string
  // Project context
  scope: AccessScope
  projectId?: string
  definition: AppDefinition
  // Analytics
  viewCount: number
  starCount: number
  lastViewedAt?: string // ISO date string
  // Sample/Featured flags
  isSample: boolean
  isFeatured: boolean
  isProfileFeatured: boolean
  // Public profile display
  thumbnailUrl?: string | null
  screenshots: AppScreenshot[]
  category?: string | null
  tags?: string | null
  marketplaceStatus: string
  reviewStatus: string
  accessStatus: string
  marketplaceModerationReason?: string | null
  marketplaceModeratedAt?: string
  marketplaceModeratedByUserId?: string
  forkedFromAppId?: string | null
  forkedFromAuthorUserId?: string | null
  forkedFromAppName?: string | null
  forkedFromAuthorName?: string | null
  developmentMode: AppDevelopmentMode
  sourceAvailable: boolean
  runtimeSource: AppRuntimeSource
  hasDeployedFrontend: boolean
  frontend: AppComponentOwnership
  backend: AppComponentOwnership
}

export interface AppComponentOwnership {
  sourceAvailable: boolean
  runtimeSource: AppRuntimeSource
}

export const AppDevelopmentMode = {
  Studio: 'studio',
  Local: 'local',
} as const

export type AppDevelopmentMode = (typeof AppDevelopmentMode)[keyof typeof AppDevelopmentMode]

export const AppRuntimeSource = {
  Studio: 'studio',
  CliBuild: 'cli-build',
  CliDeploy: 'cli-deploy',
} as const

export type AppRuntimeSource = (typeof AppRuntimeSource)[keyof typeof AppRuntimeSource]

/**
 * Request to create a new app
 * Matches src/Automaton.Api/Models/NewApp.cs
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
  category?: string | null
  tags?: string | null
  isForkable?: boolean
}

export interface ForkAppRequest {
  name?: string
  publishImmediately?: boolean
}

export interface ForkAppSource {
  id: string
  name: string
  publicUrl?: string | null
  summary?: string | null
  description?: string | null
  thumbnailUrl?: string | null
  creatorPublicUsername?: string | null
  creatorDisplayName: string
  hasBackendExecution: boolean
  runtimeLabel: string
}

export interface ExportAppResponse {
  blob: Blob
  fileName: string
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
 * Access scope enum for resources (matches Automaton.Api.Enums.AccessScope)
 */
export const AccessScope = {
  Personal: 0,
  Project: 1,
  Public: 2,
} as const

export type AccessScope = (typeof AccessScope)[keyof typeof AccessScope]

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

export const AppFileSystemScope = {
  Source: 'source',
  Versions: 'versions',
} as const

export type AppFileSystemScope = (typeof AppFileSystemScope)[keyof typeof AppFileSystemScope]

export interface ScopedFilesResponse {
  success: boolean
  scope: AppFileSystemScope
  editable: boolean
  files: AppFile[]
}

export interface ScopedFileContentResponse {
  success: boolean
  scope: AppFileSystemScope
  editable: boolean
  content: string
}

export interface RunnerBackendFile {
  path: string
  name: string
  size: number
  lastModifiedUtc: string
  isText: boolean
}

export const RunnerFileArea = {
  Backend: 'backend',
  Data: 'data',
} as const

export type RunnerFileArea = (typeof RunnerFileArea)[keyof typeof RunnerFileArea]

export interface RunnerBackendFilesResponse {
  success: boolean
  area: RunnerFileArea
  error?: string
  files: RunnerBackendFile[]
}

export interface RunnerBackendFileContent {
  success: boolean
  area: RunnerFileArea
  error?: string
  path: string
  content?: string
  size: number
  isText: boolean
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
  scope?: AppFileSystemScope
  isReadOnly?: boolean
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
/**
 * App Version Trigger enum
 */
export type AppVersionTrigger = 'ManualSave' | 'Publish' | 'Restore' | 'NewGeneration'

export const AppVersionSource = {
  AiChat: 'AiChat',
  CliDeploy: 'CliDeploy',
  Studio: 'Studio',
} as const

export type AppVersionSource = (typeof AppVersionSource)[keyof typeof AppVersionSource]

/**
 * Basic version info for list view
 */
export interface AppVersion {
  id: string
  appId: string
  versionNumber: number
  summary: string
  trigger: AppVersionTrigger
  source?: AppVersionSource | string
  reason?: string
  createdAt: string // ISO date string
  createdBy: string
  aiSessionId?: string
  aiModel?: string
  filesChangeSummary?: string
  snapshotCreated?: boolean
}

/**
 * Detailed version info with file changes
 */
export interface AppVersionDetail extends AppVersion {
  snapshotPath: string
}

/**
 * Paginated list of versions
 */
export interface AppVersionList {
  appId: string
  totalCount: number
  pageNumber: number
  pageSize: number
  versions: AppVersion[]
}

/**
 * Request to restore a version
 */
export interface RestoreVersionRequest {
  versionNumber: number
}

/**
 * Response after restoring a version
 */
export interface RestoreVersionResponse {
  appId: string
  message: string
}
