import apiClient from '@/shared/services/api/axios-instance'
import type { ServerCleanup, ServerCleanupSettings } from '../types/server.types'

class ServerCleanupService {
  /** GET /api/server/cleanup. System administrators only; anyone else gets 403. */
  async get(): Promise<ServerCleanup> {
    const { data } = await apiClient.get<ServerCleanup>('/api/server/cleanup')
    return data
  }

  async saveSettings(settings: ServerCleanupSettings): Promise<ServerCleanup> {
    const { data } = await apiClient.put<ServerCleanup>('/api/server/cleanup/settings', settings)
    return data
  }

  /** Starts a run now; 409 when no server Runner is online. */
  async run(): Promise<ServerCleanup> {
    const { data } = await apiClient.post<ServerCleanup>('/api/server/cleanup/run')
    return data
  }
}

export const serverCleanupService = new ServerCleanupService()
