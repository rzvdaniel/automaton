/**
 * Public Apps service for API calls
 * No authentication required - these are public endpoints
 */
import axios from 'axios'
import apiClient from '@/shared/services/api/axios-instance'
import type {
  PublicAppsResponse,
  PublicUserProfile,
  PublicAppDetails,
} from '../types/publicApps.types'

// Create a separate axios instance for public endpoints (no auth interceptors)
const publicApiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '',
  headers: {
    'Content-Type': 'application/json',
  },
})

class PublicAppsService {
  /**
   * Get user profile and their public apps
   * GET /api/public/{username}/apps
   */
  async getUserApps(username: string): Promise<PublicAppsResponse> {
    const { data } = await publicApiClient.get<PublicAppsResponse>(
      `/api/public/${encodeURIComponent(username)}/apps`,
    )
    return data
  }

  /**
   * Get user profile only
   * GET /api/public/{username}/profile
   */
  async getUserProfile(username: string): Promise<PublicUserProfile> {
    const { data } = await publicApiClient.get<PublicUserProfile>(
      `/api/public/${encodeURIComponent(username)}/profile`,
    )
    return data
  }

  /**
   * Get details of a specific public app
   * GET /api/public/{username}/apps/{appId}
   */
  async getAppDetails(username: string, appId: string): Promise<PublicAppDetails> {
    const { data } = await publicApiClient.get<PublicAppDetails>(
      `/api/public/${encodeURIComponent(username)}/apps/${appId}`,
    )
    return data
  }

  async starApp(appId: string): Promise<void> {
    await apiClient.post(`/api/marketplace/apps/${appId}/star`)
  }

  async unstarApp(appId: string): Promise<void> {
    await apiClient.delete(`/api/marketplace/apps/${appId}/star`)
  }
}

export const publicAppsService = new PublicAppsService()
