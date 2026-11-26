export type SettingsTabKey = 'account' | 'connect' | 'keys' | 'email'

export interface SettingsTab { key: SettingsTabKey; label: string; badge?: string }

/** The tabs this person sees. Email is for system administrators, with a badge while email is not set up. */
export function settingsTabs(isSystemAdministrator: boolean, emailMissing: boolean): SettingsTab[] {
  const tabs: SettingsTab[] = [
    { key: 'account', label: 'Account' },
    { key: 'connect', label: 'Connect your AI' },
    { key: 'keys', label: 'API keys' },
  ]
  if (isSystemAdministrator) tabs.push({ key: 'email', label: 'Email', badge: emailMissing ? '!' : undefined })
  return tabs
}

/** Older links (#agents, #server) still open the tab that now holds their content. */
const aliases: Record<string, SettingsTabKey> = {
  account: 'account', connect: 'connect', keys: 'keys', email: 'email', agents: 'connect', server: 'email',
}

/** The tab a URL hash opens; an unknown hash, or Email for a non-administrator, opens Account. */
export function resolveSettingsTab(hash: string, isSystemAdministrator: boolean): SettingsTabKey {
  const key = aliases[hash.replace(/^#/, '')]
  return !key || (key === 'email' && !isSystemAdministrator) ? 'account' : key
}
