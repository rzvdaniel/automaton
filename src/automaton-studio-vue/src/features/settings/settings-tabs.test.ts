import { describe, expect, it } from 'vitest'
import { resolveSettingsTab, settingsTabs } from './settings-tabs'

describe('settings tabs', () => {
  it('shows Email only to system administrators, with a badge while email is missing', () => {
    expect(settingsTabs(false, true).map((tab) => tab.key)).toEqual(['account', 'connect', 'keys'])
    expect(settingsTabs(true, false)[3]).toEqual({ key: 'email', label: 'Email', badge: undefined })
    expect(settingsTabs(true, true)[3]?.badge).toBe('!')
  })

  it('opens the tab named in the hash', () => {
    expect(resolveSettingsTab('#keys', false)).toBe('keys')
    expect(resolveSettingsTab('#connect', false)).toBe('connect')
    expect(resolveSettingsTab('#email', true)).toBe('email')
  })

  it('keeps the old links working and falls back to Account', () => {
    expect(resolveSettingsTab('#agents', false)).toBe('connect')
    expect(resolveSettingsTab('#server', true)).toBe('email')
    expect(resolveSettingsTab('#server', false)).toBe('account')
    expect(resolveSettingsTab('', true)).toBe('account')
    expect(resolveSettingsTab('#nonsense', true)).toBe('account')
  })
})
