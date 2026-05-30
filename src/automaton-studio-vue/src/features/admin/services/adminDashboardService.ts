import apiClient from '@/shared/services/api/axios-instance'
import type { AdminDashboardSummary } from '../types/admin.types'

class AdminDashboardService {
  async getSummary(): Promise<AdminDashboardSummary> {
    const { data } = await apiClient.get<AdminDashboardSummary>('/api/admin/dashboard')
    return data
  }
}

export const adminDashboardService = new AdminDashboardService()
