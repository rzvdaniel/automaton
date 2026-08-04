import * as signalR from '@microsoft/signalr'
import { tokenStorage } from '@/shared/services/api/axios-instance'
import type { BackendRuntimeLogEvent } from '../types/console.types'
import { AutomatonHubMethods } from '../types/hub.types'

type RuntimeLogHandler = (event: BackendRuntimeLogEvent) => void

export class BackendRuntimeLogClient {
  private connection: signalR.HubConnection | null = null
  private startPromise: Promise<void> | null = null
  private activeAppId: string | null = null
  private activeTargetId: string | null = null
  private readonly handlers = new Set<RuntimeLogHandler>()

  onLog(handler: RuntimeLogHandler): () => void {
    this.handlers.add(handler)
    return () => {
      this.handlers.delete(handler)
    }
  }

  async subscribe(appId: string, targetId: string): Promise<void> {
    await this.ensureConnection()
    if (!this.connection) {
      return
    }

    if (this.activeAppId && (this.activeAppId !== appId || this.activeTargetId !== targetId)) {
      await this.connection.invoke(AutomatonHubMethods.LeaveAppRuntimeLogs, this.activeAppId, this.activeTargetId)
      this.activeAppId = null
    }

    if (!this.activeAppId) {
      await this.connection.invoke(AutomatonHubMethods.JoinAppRuntimeLogs, appId, targetId)
      this.activeAppId = appId
      this.activeTargetId = targetId
    }
  }

  async unsubscribe(appId?: string): Promise<void> {
    if (!this.activeAppId || !this.activeTargetId) {
      this.activeAppId = null
      this.activeTargetId = null
      return
    }

    if (appId && appId !== this.activeAppId) {
      return
    }

    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke(AutomatonHubMethods.LeaveAppRuntimeLogs, this.activeAppId, this.activeTargetId)
    }

    this.activeAppId = null
    this.activeTargetId = null
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
        if (this.activeAppId && this.activeTargetId && this.connection) {
          await this.connection.invoke(AutomatonHubMethods.JoinAppRuntimeLogs, this.activeAppId, this.activeTargetId)
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
