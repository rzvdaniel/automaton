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
