import * as signalR from '@microsoft/signalr'
import { tokenStorage } from '@/shared/services/api/axios-instance'
import type { BackendRuntimeLogEvent, RuntimeLogConnectionState } from '../types/console.types'
import { AutomatonHubMethods } from '../types/hub.types'

type RuntimeLogHandler = (event: BackendRuntimeLogEvent) => void
type ConnectionStateHandler = (state: RuntimeLogConnectionState) => void

export class BackendRuntimeLogClient {
  private connection: signalR.HubConnection | null = null
  private startPromise: Promise<void> | null = null
  private desiredAppId: string | null = null
  private desiredTargetId: string | null = null
  private groupJoined = false
  private readonly handlers = new Set<RuntimeLogHandler>()
  private readonly connectionStateHandlers = new Set<ConnectionStateHandler>()
  private currentConnectionState: RuntimeLogConnectionState = 'disconnected'

  get connectionState(): RuntimeLogConnectionState {
    return this.currentConnectionState
  }

  isSubscribedTo(appId: string, targetId: string): boolean {
    return this.groupJoined && this.desiredAppId === appId && this.desiredTargetId === targetId
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

    if (this.desiredAppId && (this.desiredAppId !== appId || this.desiredTargetId !== targetId)) {
      if (this.groupJoined) {
        await this.connection.invoke(AutomatonHubMethods.LeaveAppRuntimeLogs, this.desiredAppId, this.desiredTargetId)
      }
      this.groupJoined = false
      this.desiredAppId = null
      this.desiredTargetId = null
    }

    if (!this.desiredAppId) {
      this.desiredAppId = appId
      this.desiredTargetId = targetId
    }

    if (!this.groupJoined) {
      await this.joinDesiredGroup()
    }
  }

  async unsubscribe(appId?: string): Promise<void> {
    if (!this.desiredAppId || !this.desiredTargetId) {
      this.desiredAppId = null
      this.desiredTargetId = null
      this.groupJoined = false
      return
    }

    if (appId && appId !== this.desiredAppId) {
      return
    }

    if (this.groupJoined && this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke(AutomatonHubMethods.LeaveAppRuntimeLogs, this.desiredAppId, this.desiredTargetId)
    }

    this.desiredAppId = null
    this.desiredTargetId = null
    this.groupJoined = false
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
        this.groupJoined = false
        this.setConnectionState('reconnecting')
        try {
          if (this.desiredAppId && this.desiredTargetId) {
            await this.joinDesiredGroup()
          } else {
            this.setConnectionState('connected')
          }
        } catch {
          this.groupJoined = false
          this.setConnectionState('disconnected')
        }
      })

      this.connection.onreconnecting(() => {
        this.groupJoined = false
        this.setConnectionState('reconnecting')
      })
      this.connection.onclose(() => {
        this.groupJoined = false
        this.setConnectionState('disconnected')
      })
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

  private async joinDesiredGroup(): Promise<void> {
    if (!this.connection || !this.desiredAppId || !this.desiredTargetId) {
      return
    }

    await this.connection.invoke(AutomatonHubMethods.JoinAppRuntimeLogs, this.desiredAppId, this.desiredTargetId)
    this.groupJoined = true
    this.setConnectionState('connected')
  }

  private setConnectionState(state: RuntimeLogConnectionState): void {
    if (this.currentConnectionState === state) return
    this.currentConnectionState = state
    for (const handler of this.connectionStateHandlers) handler(state)
  }
}

export const backendRuntimeLogClient = new BackendRuntimeLogClient()
