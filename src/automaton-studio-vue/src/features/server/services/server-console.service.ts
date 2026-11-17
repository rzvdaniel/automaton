import apiClient from '@/shared/services/api/axios-instance'

/** `running` is true once the app's runtime is up, so opening it shows the app and not "not deployed yet". */
export interface ServerConsoleStatus {
  appId: string | null
  running: boolean
}

class ServerConsoleService {
  /** GET /api/server/console. System administrators only: the caller's own Server Console app, if installed. */
  async get(): Promise<ServerConsoleStatus> {
    const { data } = await apiClient.get<ServerConsoleStatus>('/api/server/console')
    return data
  }

  /** POST /api/server/console/install. System administrators only: installs Server Console for the caller, approves its permissions and deploys it. */
  async install(): Promise<ServerConsoleStatus> {
    const { data } = await apiClient.post<ServerConsoleStatus>('/api/server/console/install')
    return data
  }
}

export const serverConsoleService = new ServerConsoleService()
