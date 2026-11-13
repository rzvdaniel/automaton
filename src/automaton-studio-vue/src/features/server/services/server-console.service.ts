import apiClient from '@/shared/services/api/axios-instance'

class ServerConsoleService {
  /** GET /api/server/console. System administrators only: the caller's own Server Console app, if installed. */
  async get(): Promise<{ appId: string | null }> {
    const { data } = await apiClient.get<{ appId: string | null }>('/api/server/console')
    return data
  }

  /** POST /api/server/console/install. System administrators only: installs Server Console for the caller, approves its permissions and deploys it. */
  async install(): Promise<{ appId: string | null }> {
    const { data } = await apiClient.post<{ appId: string | null }>('/api/server/console/install')
    return data
  }
}

export const serverConsoleService = new ServerConsoleService()
