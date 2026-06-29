import * as signalR from '@microsoft/signalr'
import { tokenStorage } from '@/shared/services/api/axios-instance'
import type { BackendRuntimeLogEvent } from '../types/console.types'
import { AutomatonHubMethods } from '../types/hub.types'

type RuntimeLogHandler = (event: BackendRuntimeLogEvent) => void

class BackendRuntimeLogClient {
  private connection: signalR.HubConnection | null = null
  private startPromise: Promise<void> | null = null
  private activeAppId: string | null = null
  private readonly handlers = new Set<RuntimeLogHandler>()

  onLog(handler: RuntimeLogHandler): () => void {
    this.handlers.add(handler)
    return () => {
      this.handlers.delete(handler)
    }
  }

  async subscribe(appId: string): Promise<void> {
    await this.ensureConnection()
    if (!this.connection) {
      return
    }

    if (this.activeAppId && this.activeAppId !== appId) {
      await this.connection.invoke(AutomatonHubMethods.LeaveAppRuntimeLogs, this.activeAppId)
    }

    if (this.activeAppId !== appId) {
      await this.connection.invoke(AutomatonHubMethods.JoinAppRuntimeLogs, appId)
      this.activeAppId = appId
    }
  }

  async unsubscribe(appId?: string): Promise<void> {
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      if (!appId || this.activeAppId === appId) {
        this.activeAppId = null
      }
      return
    }

    const targetAppId = appId ?? this.activeAppId
    if (!targetAppId) {
      return
    }

    await this.connection.invoke(AutomatonHubMethods.LeaveAppRuntimeLogs, targetAppId)
    if (this.activeAppId === targetAppId) {
      this.activeAppId = null
    }
  }

  private async ensureConnection(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return
    }

    if (!this.connection) {
      this.connection = new signalR.HubConnectionBuilder()
        .withUrl(`${import.meta.env.VITE_API_BASE_URL}/api/workflow/hub`, {
          accessTokenFactory: () => tokenStorage.getAccessToken() ?? '',
          withCredentials: true,
        })
        .withAutomaticReconnect()
        .build()

      this.connection.on(AutomatonHubMethods.BackendRuntimeLogEvent, (event: BackendRuntimeLogEvent) => {
        for (const handler of this.handlers) {
          handler(event)
        }
      })

      this.connection.onreconnected(async () => {
        if (this.activeAppId && this.connection) {
          await this.connection.invoke(AutomatonHubMethods.JoinAppRuntimeLogs, this.activeAppId)
        }
      })
    }

    if (!this.startPromise) {
      this.startPromise = this.connection.start().finally(() => {
        this.startPromise = null
      })
    }

    await this.startPromise
  }
}

export const backendRuntimeLogClient = new BackendRuntimeLogClient()
