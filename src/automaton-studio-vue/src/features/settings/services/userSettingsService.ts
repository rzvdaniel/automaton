/**
 * User Settings Service
 * Handles API calls for user-specific settings
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  UserSettingsDto,
  UpdateUserSettingsRequest,
} from '../types/settings.types'

class UserSettingsService {
  private readonly baseUrl = '/api/user-settings'

  /**
   * Get all user settings
   * GET /api/user-settings
   */
  async getSettings(): Promise<UserSettingsDto> {
    const { data } = await apiClient.get<UserSettingsDto>(this.baseUrl)
    return data
  }

  /**
   * Update user settings (partial update - only provided fields are updated)
   * PUT /api/user-settings
   */
  async updateSettings(settings: UpdateUserSettingsRequest): Promise<UserSettingsDto> {
    const { data } = await apiClient.put<UserSettingsDto>(this.baseUrl, settings)
    return data
  }
}

export const userSettingsService = new UserSettingsService()
