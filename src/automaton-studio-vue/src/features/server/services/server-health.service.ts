import apiClient from '@/shared/services/api/axios-instance'
import type { ServerHealth } from '../types/server.types'

class ServerHealthService {
  /** GET /api/server/health. System administrators only; anyone else gets 403. */
  async get(): Promise<ServerHealth> {
    const { data } = await apiClient.get<ServerHealth>('/api/server/health')
    return data
  }
}

export const serverHealthService = new ServerHealthService()
