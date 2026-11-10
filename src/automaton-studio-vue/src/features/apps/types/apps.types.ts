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
  /** Stopped on purpose: schedules off, address shows a paused page. */
  isPaused?: boolean
  canLaunchPublicly?: boolean
  isForkable: boolean
  publicUrl?: string
  publishedAt?: string // ISO date string
  version: number
  requiresAuthentication: boolean
  runtimeVisibility?: 'private' | 'public'
  catalogVisibility?: 'unlisted' | 'listed'
  marketplacePreviewMode?: 'launchable' | 'mediaOnly'
  created: string // ISO date string
  updated: string // ISO date string
  executionRunnerId?: string
  canUpdate?: boolean // set on a single-app read
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
  application: ApplicationContract
}

/** The stack-neutral application contract persisted by the API and CLI. */
export interface ApplicationCommandSpec {
  executable: string
  arguments: string[]
  workingDirectory: string
  environment: Record<string, string>
  useShell: boolean
}

export interface ApplicationBuildSpec {
  image?: string | null
  command: ApplicationCommandSpec | null
  outputDirectory?: string | null
  singlePageApp?: boolean
  timeoutSeconds: number
}

export interface ApplicationRuntimeSpec {
  image?: string | null
  command: ApplicationCommandSpec | null
  port: number
}

export interface ApplicationHealthSpec {
  path?: string | null
  port?: number | null
  startupTimeoutSeconds: number
}

export interface ApplicationStorageMount {
  name: string
  relativePath: string
  readOnly: boolean
}

export interface ApplicationStorageSpec {
  dataDirectory: string
  mounts: ApplicationStorageMount[]
}

export interface ApplicationContract {
  build: ApplicationBuildSpec
  runtime: ApplicationRuntimeSpec
  health: ApplicationHealthSpec
  storage: ApplicationStorageSpec
  secrets?: ApplicationSecretDeclaration[]
  settings?: ApplicationSettingDeclaration[]
  permissions?: string[]
  isStaticSite?: boolean
  compose?: unknown
  /** 'api' when the app declares it is an API; web is assumed otherwise (#864). */
  kind?: string | null
  /** One-line description of the app, from automaton.json. */
  description?: string | null
  /** Key paths of a web app, each with a label. */
  pages?: Array<{ path: string; label?: string; method?: string; description?: string }>
  /** Path of an API app's OpenAPI document. */
  openApi?: string | null
}

export interface ApplicationSecretDeclaration {
  name: string
  description?: string | null
  from?: string | null
  optional?: boolean
}

export interface ApplicationSettingDeclaration {
  name: string
  description?: string | null
  default?: string | null
  required?: boolean
}

export interface AppListItem {
  id: string
  name: string
  summary: string
  description: string
  thumbnailUrl?: string | null
  tags: string[]
  isPublished: boolean
  isPaused?: boolean
  canLaunchPublicly?: boolean
  publicUrl?: string | null
  updatedAtUtc: string
  canDelete: boolean
  /** May deploy, pause and resume the app. */
  canUpdate?: boolean
  runner?: { id: string; name: string; purpose: string; connectivityStatus: 'None' | 'Online' | 'Offline' } | null
  liveEnvironment?: {
    name: string
    status: string
    isHostedRunner: boolean
    applicationProfile?: string | null
    runnerOnline: boolean
    genericRuntimeDeployed: boolean
    lastSuccessfulDeploymentAtUtc?: string | null
    isStaticHosting?: boolean
    routePath: string
  } | null
  runtime: { genericRuntimeDeployed: boolean }
  /** No web runtime or static site: the app only has Actions. */
  actionsOnly?: boolean
  lastActionRun?: { moduleKey: string; status: string; createdAtUtc: string } | null
  /** Soonest run of an enabled schedule of an Actions-only app. */
  nextActionRunAtUtc?: string | null
}

export const DeploymentTargetStatus = {
  NotDeployed: 'NotDeployed',
  Deploying: 'Deploying',
  Running: 'Running',
  Failed: 'Failed',
  RunnerOffline: 'RunnerOffline',
  Unassigned: 'Unassigned',
} as const

export type DeploymentTargetStatus = (typeof DeploymentTargetStatus)[keyof typeof DeploymentTargetStatus]

export interface AppDeploymentTarget {
  id: string
  appId: string
  name: string
  slug: string
  isDefault: boolean
  status: DeploymentTargetStatus
  genericRuntimeDeployed: boolean
  lastDeploymentAt?: string | null
  lastDeploymentError?: string | null
  lastSuccessfulDeploymentAt?: string | null
  cleanupPending: boolean
  lastCleanupError?: string | null
  lastCleanupAttemptAt?: string | null
  runner?: {
    id: string
    name: string
    purpose: string
    applicationProfile?: string | null
    isHostedRunner: boolean
    isOnline: boolean
  } | null
  routePath: string
}

export interface AppTagSettings {
  tags: string[]
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
 * Request to publish an app
 */
export interface PublishAppRequest {
  publicUrl: string
  requiresAuthentication: boolean
  executionRunnerId?: string
  category?: string | null
  tags?: string | null
  isForkable?: boolean
  runtimeVisibility?: 'private' | 'public'
  catalogVisibility?: 'unlisted' | 'listed'
  marketplacePreviewMode?: 'launchable' | 'mediaOnly'
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
 * App statistics for display
 */
export interface AppStats {
  fileCount: number
}

// ============================================
// File tree types used by administrative source inspection
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
 * Application viewport mode for responsive testing
 */
export type ApplicationViewportMode = 'desktop' | 'tablet' | 'mobile'

/**
 * Application viewport sizes
 */
export const APPLICATION_VIEWPORT_SIZES: Record<ApplicationViewportMode, { width: string; height: string }> = {
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

/** One requested platform permission of an app and its administrator decision. */
export interface AppPermissionStatus {
  permission: string
  approved: boolean
  status: 'Pending' | 'Approved' | 'Rejected' | 'Revoked' | string
}

export interface AppPermissionsResponse {
  permissions: AppPermissionStatus[]
  waiting: string[]
  approvalUrl?: string | null
}

export interface AppSecretSummary {
  name: string
  version: number
  updatedAtUtc: string
}

export interface SecretDelivery {
  name: string
  scope: string
  version?: number | null
  from?: string | null
}

/** One audit entry about a vault: a change or a delivery. Never carries a value. */
export interface SecretAuditEntry {
  createdAtUtc: string
  eventType: string
  message: string
  userId?: string | null
  userName?: string | null
  name?: string | null
  version?: number | null
  appId?: string | null
  runnerId?: string | null
  delivered: SecretDelivery[]
}

export interface DeploymentOperation {
  operationId: string
  status: 'Pending' | 'Running' | 'Succeeded' | 'Failed'
  success?: boolean | null
  error?: string | null
  result?: { message?: string | null } | null
}

export interface InstallSource {
  id: string
  name: string
  summary?: string | null
  description?: string | null
  creatorDisplayName?: string
  application?: InstallManifest | null
  /** Set when the app comes from a Git repository (a catalog entry or a pasted URL) rather than this server. */
  git?: GitAppSource
}

/** A public GitHub repository folder pinned to one commit. */
export interface GitAppSource {
  repo: string
  folder: string
  commit: string
}

export interface CatalogEntry extends GitAppSource {
  id: string
  name: string
  summary: string
  category: string
  minServerVersion: string
  permissions: string[]
  requiresNewerServer: boolean
  installedAppId: string | null
  sourceId: string
  sourceName: string
  customSource: boolean
  tags?: string[]
  icon?: string | null
}

/** The filters and page the Catalog tab sends; the server filters and pages. */
export interface CatalogQuery {
  q?: string
  category?: string
  source?: string
  installed?: boolean
  page?: number
  pageSize?: number
}

export interface CatalogFacet {
  value: string
  label: string
  count: number
}

export interface CatalogSource {
  id: string
  name: string
  repo: string
  branch: string
  official: boolean
  enabled: boolean
  hasToken: boolean
  /** Only in the answer to adding a source: how many apps its catalog lists. */
  appCount?: number | null
}

export interface CatalogSourceInput {
  repo: string
  branch?: string
  name?: string
  token?: string
}

export interface CatalogResponse {
  apps: CatalogEntry[]
  fetchedAt: string | null
  stale: boolean
  unavailable: boolean
  message: string | null
  /** Apps matching the filters; `apps` is one page of them. */
  total?: number
  /** Apps in the whole catalog. */
  totalApps?: number
  page?: number
  pageSize?: number
  categories?: CatalogFacet[]
  sources?: CatalogFacet[]
}

export interface InstallManifest {
  settings?: Array<{ name: string; description?: string | null; default?: string | null; required?: boolean }>
  secrets?: Array<{ name: string; description?: string | null; from?: string | null; optional?: boolean }>
  permissions?: string[]
  schedules?: Array<{
    name: string
    action: string
    description?: string | null
    cron?: string | null
    intervalSeconds?: number | null
    timeZone?: string
    input?: Record<string, unknown> | null
  }>
}

/** Who can open an app: only the owner, chosen people, everyone signed in on the server, or anyone on the internet. */
export type SharingAudience = 'me' | 'people' | 'team' | 'server' | 'internet'

export interface SharedPerson {
  userId: string
  email: string
  displayName: string
}

/** The answer to pausing or resuming an app. */
export interface AppPauseState {
  appId: string
  paused: boolean
  pausedAt?: string | null
  schedulesChanged: number
  warnings: string[]
}

export interface AppSharing {
  audience: SharingAudience
  people: SharedPerson[]
  publicUrlSlug?: string | null
  /** The team whose members open the app (audience 'team'). */
  team?: { id: string, name: string } | null
  /** For 'internet': whether the app is listed in the store. */
  listed?: boolean
}

/** A team (project) the signed-in person belongs to. */
export interface MyTeam {
  id: string
  displayName: string
}
