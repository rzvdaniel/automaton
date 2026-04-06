/**
 * User preferences for the editor
 */
export interface EditorSettings {
  /**
   * Font size for code editors (Monaco)
   * Default: 14, Range: 10-24
   */
  fontSize: number
}

/**
 * Request model for updating editor settings
 */
export interface UpdateEditorSettingsRequest {
  fontSize: number
}

/**
 * Theme preference options
 */
export type ThemeMode = 'light' | 'dark' | 'system'

export const AppVerificationStrategy = {
  Continuous: 'continuous',
  Balanced: 'balanced',
  FinalGate: 'final-gate',
} as const

export type AppVerificationStrategyType =
  typeof AppVerificationStrategy[keyof typeof AppVerificationStrategy]

/**
 * User appearance settings
 */
export interface AppearanceSettings {
  /**
   * Theme preference: light, dark, or system
   * Default: 'system'
   */
  theme: ThemeMode
}

/**
 * Request model for updating appearance settings
 */
export interface UpdateAppearanceSettingsRequest {
  theme: ThemeMode
}

/**
 * Unified user settings (all preferences in one object)
 */
export interface UserSettingsDto {
  /**
   * Theme preference: light, dark, or system
   * Default: 'system'
   */
  theme: ThemeMode

  /**
   * Font size for code editors (Monaco)
   * Default: 14, Range: 10-24
   */
  fontSize: number

  /**
   * User's preferred default AI model ID.
   * Falls back to the admin-configured global default when undefined.
   */
  defaultAiModel?: string

  /** Provider name for the default AI model (e.g. "OpenAI"). */
  defaultAiProvider?: string

  /**
   * User's preferred planning AI model ID.
   * Falls back to the admin-configured global planning model when undefined.
   */
  defaultPlanningModel?: string

  /** Provider name for the planning AI model. */
  defaultPlanningProvider?: string

  /** Enables verbose AI chat debug rendering in UI (display-only). */
  showAiDebugDetails?: boolean

  /** Optional user override for app verification strategy. */
  appVerificationStrategy?: AppVerificationStrategyType
}

/**
 * Request model for updating user settings (partial update)
 * Only provided fields will be updated
 */
export interface UpdateUserSettingsRequest {
  theme?: ThemeMode
  fontSize?: number
  defaultAiModel?: string
  defaultAiProvider?: string
  defaultPlanningModel?: string
  defaultPlanningProvider?: string
  showAiDebugDetails?: boolean
  appVerificationStrategy?: string
}
