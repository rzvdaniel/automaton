import api from './axios-instance'
import type { AppActivityItem } from '@/shared/types/ai.types'

export const adminAppActivityService = {
  async getAppActivity(): Promise<AppActivityItem[]> {
    const response = await api.get<AppActivityItem[]>('/api/admin/app-activity')
    return response.data
  },
}
