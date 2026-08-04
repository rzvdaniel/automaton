import { beforeEach, describe, expect, it, vi } from 'vitest'

const signalRMock = vi.hoisted(() => {
  const connection = {
    state: 'Connected',
    start: vi.fn().mockResolvedValue(undefined),
    invoke: vi.fn().mockResolvedValue(undefined),
    on: vi.fn(),
    onreconnected: vi.fn(),
  }

  return { connection }
})

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 'Connected' },
  HubConnectionBuilder: class {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    build() { return signalRMock.connection }
  },
}))

vi.mock('@/shared/services/api/axios-instance', () => ({
  tokenStorage: { getAccessToken: () => null },
}))

import { AutomatonHubMethods } from '../types/hub.types'
import { BackendRuntimeLogClient } from './backend-runtime-log-client'

describe('BackendRuntimeLogClient', () => {
  beforeEach(() => {
    signalRMock.connection.state = 'Connected'
    signalRMock.connection.start.mockClear()
    signalRMock.connection.invoke.mockClear()
    signalRMock.connection.on.mockClear()
    signalRMock.connection.onreconnected.mockClear()
  })

  it('switches SignalR log groups by target ID', async () => {
    const client = new BackendRuntimeLogClient()

    await client.subscribe('app-id', 'live-target-id')
    await client.subscribe('app-id', 'canary-target-id')

    expect(signalRMock.connection.invoke).toHaveBeenNthCalledWith(
      1, AutomatonHubMethods.JoinAppRuntimeLogs, 'app-id', 'live-target-id',
    )
    expect(signalRMock.connection.invoke).toHaveBeenNthCalledWith(
      2, AutomatonHubMethods.LeaveAppRuntimeLogs, 'app-id', 'live-target-id',
    )
    expect(signalRMock.connection.invoke).toHaveBeenNthCalledWith(
      3, AutomatonHubMethods.JoinAppRuntimeLogs, 'app-id', 'canary-target-id',
    )
  })

  it('does not invoke SignalR with a null target after unsubscribing', async () => {
    const client = new BackendRuntimeLogClient()

    await client.subscribe('app-id', 'live-target-id')
    await client.unsubscribe('app-id')
    await client.unsubscribe('app-id')

    expect(signalRMock.connection.invoke).toHaveBeenCalledTimes(2)
    expect(signalRMock.connection.invoke).toHaveBeenLastCalledWith(
      AutomatonHubMethods.LeaveAppRuntimeLogs, 'app-id', 'live-target-id',
    )
    expect(signalRMock.connection.invoke).not.toHaveBeenCalledWith(
      AutomatonHubMethods.LeaveAppRuntimeLogs, 'app-id', null,
    )
  })

  it('does not invoke SignalR when an unassigned target has no active subscription', async () => {
    const client = new BackendRuntimeLogClient()

    await client.unsubscribe('app-id')

    expect(signalRMock.connection.invoke).not.toHaveBeenCalled()
  })
})
