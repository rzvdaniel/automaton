/**
 * User Settings Service
 * Handles API calls for user-specific settings
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  EditorSettings,
  UpdateEditorSettingsRequest,
  AppearanceSettings,
  UpdateAppearanceSettingsRequest,
} from '../types/settings.types'

class UserSettingsService {
  private readonly baseUrl = '/api/user-settings'

  /**
   * Get editor settings for the current user
   * GET /api/user-settings/editor
   */
  async getEditorSettings(): Promise<EditorSettings> {
    const { data } = await apiClient.get<EditorSettings>(`${this.baseUrl}/editor`)
    return data
  }

  /**
   * Update editor settings for the current user
   * PUT /api/user-settings/editor
   */
  async updateEditorSettings(settings: UpdateEditorSettingsRequest): Promise<EditorSettings> {
    const { data } = await apiClient.put<EditorSettings>(`${this.baseUrl}/editor`, settings)
    return data
  }

  /**
   * Get appearance settings for the current user
   * GET /api/user-settings/appearance
   */
  async getAppearanceSettings(): Promise<AppearanceSettings> {
    const { data } = await apiClient.get<AppearanceSettings>(`${this.baseUrl}/appearance`)
    return data
  }

  /**
   * Update appearance settings for the current user
   * PUT /api/user-settings/appearance
   */
  async updateAppearanceSettings(
    settings: UpdateAppearanceSettingsRequest
  ): Promise<AppearanceSettings> {
    const { data } = await apiClient.put<AppearanceSettings>(`${this.baseUrl}/appearance`, settings)
    return data
  }
}

export const userSettingsService = new UserSettingsService()
