import { beforeEach, describe, expect, it, vi } from 'vitest'

const signalRMock = vi.hoisted(() => {
  const connection = {
    state: 'Connected',
    start: vi.fn().mockResolvedValue(undefined),
    invoke: vi.fn().mockResolvedValue(undefined),
    on: vi.fn(),
    onreconnected: vi.fn(),
    onreconnecting: vi.fn(),
    onclose: vi.fn(),
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
    signalRMock.connection.start.mockReset().mockResolvedValue(undefined)
    signalRMock.connection.invoke.mockReset().mockResolvedValue(undefined)
    signalRMock.connection.on.mockClear()
    signalRMock.connection.onreconnected.mockClear()
    signalRMock.connection.onreconnecting.mockClear()
    signalRMock.connection.onclose.mockClear()
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
    expect(client.isSubscribedTo('app-id', 'canary-target-id')).toBe(true)
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(false)
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

  it('reports truthful SignalR connection changes to the UI without changing subscriptions', async () => {
    const client = new BackendRuntimeLogClient()
    const observedStates: string[] = []
    client.onConnectionStateChange((state) => observedStates.push(state))

    await client.subscribe('app-id', 'live-target-id')

    expect(client.connectionState).toBe('connected')
    expect(observedStates).toEqual(['disconnected', 'connecting', 'connected'])
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(true)

    const calls = signalRMock.connection.onreconnecting.mock.calls
    const onReconnecting = calls[calls.length - 1]?.[0]
    onReconnecting?.()
    expect(client.connectionState).toBe('reconnecting')
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(false)
  })

  it('retries the desired target after a reconnect group rejoin fails', async () => {
    const client = new BackendRuntimeLogClient()
    await client.subscribe('app-id', 'live-target-id')
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(true)

    const reconnectingCalls = signalRMock.connection.onreconnecting.mock.calls
    const onReconnecting = reconnectingCalls[reconnectingCalls.length - 1]?.[0]
    onReconnecting?.()
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(false)

    signalRMock.connection.invoke.mockRejectedValueOnce(new Error('Group join rejected'))
    const reconnectedCalls = signalRMock.connection.onreconnected.mock.calls
    const onReconnected = reconnectedCalls[reconnectedCalls.length - 1]?.[0]
    await onReconnected?.()

    expect(client.connectionState).toBe('disconnected')
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(false)

    await client.subscribe('app-id', 'live-target-id')

    expect(signalRMock.connection.invoke).toHaveBeenNthCalledWith(
      1, AutomatonHubMethods.JoinAppRuntimeLogs, 'app-id', 'live-target-id',
    )
    expect(signalRMock.connection.invoke).toHaveBeenNthCalledWith(
      2, AutomatonHubMethods.JoinAppRuntimeLogs, 'app-id', 'live-target-id',
    )
    expect(signalRMock.connection.invoke).toHaveBeenNthCalledWith(
      3, AutomatonHubMethods.JoinAppRuntimeLogs, 'app-id', 'live-target-id',
    )
    expect(client.connectionState).toBe('connected')
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(true)
  })

  it('rejoins the desired target when subscribe restarts a closed connection', async () => {
    const client = new BackendRuntimeLogClient()
    await client.subscribe('app-id', 'live-target-id')
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(true)

    signalRMock.connection.state = 'Disconnected'
    const closeCalls = signalRMock.connection.onclose.mock.calls
    const onClose = closeCalls[closeCalls.length - 1]?.[0]
    onClose?.()
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(false)

    signalRMock.connection.start.mockImplementation(async () => {
      signalRMock.connection.state = 'Connected'
    })
    await client.subscribe('app-id', 'live-target-id')

    expect(signalRMock.connection.start).toHaveBeenCalledTimes(2)
    expect(signalRMock.connection.invoke).toHaveBeenNthCalledWith(
      1, AutomatonHubMethods.JoinAppRuntimeLogs, 'app-id', 'live-target-id',
    )
    expect(signalRMock.connection.invoke).toHaveBeenNthCalledWith(
      2, AutomatonHubMethods.JoinAppRuntimeLogs, 'app-id', 'live-target-id',
    )
    expect(client.connectionState).toBe('connected')
    expect(client.isSubscribedTo('app-id', 'live-target-id')).toBe(true)
  })
})
