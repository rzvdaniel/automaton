import { describe, expect, it } from 'vitest'
import { groupByApp, requestLine, toRequests } from './permissionRequests'

describe('permission requests', () => {
  it('names the app and the permission in plain words', () => {
    expect(requestLine({ appId: 'a', appName: 'e2e-heartbeat', permission: 'notify:email' })).toBe('e2e-heartbeat wants to send email (notify:email)')
    expect(requestLine({ appId: 'a', appName: 'x', permission: 'foo:bar' })).toBe('x wants to use foo:bar (foo:bar)')
  })

  it('flattens apps to one request per permission and groups them back by app', () => {
    const requests = toRequests([
      { id: 'a', name: 'One', waitingPermissions: ['notify:email', 'backups:read'] },
      { id: 'b', name: 'Two', waitingPermissions: ['server:read'] },
    ] as never)
    expect(requests).toHaveLength(3)
    expect(groupByApp(requests).map((group) => [group.appName, group.items.length])).toEqual([['One', 2], ['Two', 1]])
  })
})
