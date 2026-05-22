import apiClient from '@/shared/services/api/axios-instance'
import type { AppDeployRulesSettings } from '../types/admin.types'

class AdminSettingsService {
  async getAppDeployRules(): Promise<AppDeployRulesSettings> {
    const { data } = await apiClient.get<AppDeployRulesSettings>('/api/settings/app-deploy-rules')
    return data
  }

  async saveAppDeployRules(settings: AppDeployRulesSettings): Promise<AppDeployRulesSettings> {
    await apiClient.put('/api/settings/app-deploy-rules', settings)
    return this.getAppDeployRules()
  }
}

export const adminSettingsService = new AdminSettingsService()
