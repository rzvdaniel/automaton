/**
 * Install service for API calls
 * Connects to ASP.NET backend endpoints for installation
 */
import apiClient from '@/shared/services/api/axios-instance'
import type { InstallCommand, InstallationStatusResponse } from '../types/install.types'

class InstallService {
  /**
   * Check if installation is complete
   * GET /api/install/status
   */
  async isInstallationComplete(): Promise<boolean> {
    try {
      const { data } = await apiClient.get<InstallationStatusResponse>('/api/install/status')
      return data.installationComplete ?? false
    } catch {
      // If we can't check status, assume installation is not complete
      return false
    }
  }

  /**
   * Install Automaton with admin account
   * POST /api/install
   */
  async install(command: InstallCommand): Promise<void> {
    await apiClient.post('/api/install', command)
  }
}

export const installService = new InstallService()
