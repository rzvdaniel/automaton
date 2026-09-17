import * as signalR from '@microsoft/signalr'
import { tokenStorage } from '@/shared/services/api/axios-instance'
import type { BackendRuntimeLogEvent, RuntimeLogConnectionState } from '../types/console.types'
import { AutomatonHubMethods } from '../types/hub.types'

type RuntimeLogHandler = (event: BackendRuntimeLogEvent) => void
type ConnectionStateHandler = (state: RuntimeLogConnectionState) => void

export class BackendRuntimeLogClient {
  private connection: signalR.HubConnection | null = null
  private startPromise: Promise<void> | null = null
  private activeAppId: string | null = null
  private activeTargetId: string | null = null
  private readonly handlers = new Set<RuntimeLogHandler>()
  private readonly connectionStateHandlers = new Set<ConnectionStateHandler>()
  private currentConnectionState: RuntimeLogConnectionState = 'disconnected'

  get connectionState(): RuntimeLogConnectionState {
    return this.currentConnectionState
  }

  isSubscribedTo(appId: string, targetId: string): boolean {
    return this.activeAppId === appId && this.activeTargetId === targetId
  }

  onLog(handler: RuntimeLogHandler): () => void {
    this.handlers.add(handler)
    return () => {
      this.handlers.delete(handler)
    }
  }

  onConnectionStateChange(handler: ConnectionStateHandler): () => void {
    this.connectionStateHandlers.add(handler)
    handler(this.currentConnectionState)
    return () => {
      this.connectionStateHandlers.delete(handler)
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
        this.setConnectionState('reconnecting')
        try {
          if (this.activeAppId && this.activeTargetId && this.connection) {
            await this.connection.invoke(AutomatonHubMethods.JoinAppRuntimeLogs, this.activeAppId, this.activeTargetId)
          }
          this.setConnectionState('connected')
        } catch {
          this.setConnectionState('disconnected')
        }
      })

      this.connection.onreconnecting(() => this.setConnectionState('reconnecting'))
      this.connection.onclose(() => this.setConnectionState('disconnected'))
    }

    if (!this.startPromise) {
      this.setConnectionState('connecting')
      this.startPromise = this.connection.start()
        .then(() => this.setConnectionState('connected'))
        .catch((error: unknown) => {
          this.setConnectionState('disconnected')
          throw error
        })
        .finally(() => { this.startPromise = null })
    }

    await this.startPromise
  }

  private setConnectionState(state: RuntimeLogConnectionState): void {
    if (this.currentConnectionState === state) return
    this.currentConnectionState = state
    for (const handler of this.connectionStateHandlers) handler(state)
  }
}

export const backendRuntimeLogClient = new BackendRuntimeLogClient()
