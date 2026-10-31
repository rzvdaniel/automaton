import apiClient from '@/shared/services/api/axios-instance'
import type { AppDeployRulesSettings, StoreSettings } from '../types/admin.types'

class AdminSettingsService {
  async getAppDeployRules(): Promise<AppDeployRulesSettings> {
    const { data } = await apiClient.get<AppDeployRulesSettings>('/api/admin/settings/app-deploy-rules')
    return data
  }

  async getStoreSettings(): Promise<StoreSettings> {
    const { data } = await apiClient.get<StoreSettings>('/api/admin/settings/store')
    return data
  }

  async saveStoreSettings(settings: StoreSettings): Promise<StoreSettings> {
    await apiClient.put('/api/admin/settings/store', settings)
    return this.getStoreSettings()
  }

  async saveAppDeployRules(settings: AppDeployRulesSettings): Promise<AppDeployRulesSettings> {
    await apiClient.put('/api/admin/settings/app-deploy-rules', settings)
    return this.getAppDeployRules()
  }
}

export const adminSettingsService = new AdminSettingsService()
