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
}

/**
 * Request model for updating user settings (partial update)
 * Only provided fields will be updated
 */
export interface UpdateUserSettingsRequest {
  theme?: ThemeMode
  fontSize?: number
}
